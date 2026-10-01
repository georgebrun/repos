using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>One line of a deck check.</summary>
    public sealed class DeckRuleCheck
    {
        public string Title { get; init; } = "";
        public string Summary { get; init; } = "";
        /// <summary>The rule is met (or the line is just information).</summary>
        public bool IsOk { get; init; }
        /// <summary>The cards behind a problem (one per line).</summary>
        public List<string> Details { get; init; } = new();

        public static DeckRuleCheck Ok(string title, string summary) =>
            new() { Title = title, Summary = summary, IsOk = true };

        public static DeckRuleCheck Problem(string title, string summary, List<string>? details = null) =>
            new() { Title = title, Summary = summary, IsOk = false, Details = details ?? new() };
    }

    /// <summary>
    /// Checks a deck against its format's construction rules
    /// (<see cref="DeckFormats"/>): leader, size, copies, sideboard, color
    /// identity and card legality (Scryfall's, per card). Read-only; a deck in
    /// progress is expected to break rules, so these are warnings, never blocks.
    /// </summary>
    public static class DeckRulesService
    {
        public static List<DeckRuleCheck> Check(Deck deck, DeckFormatRule rule)
        {
            var checks = new List<DeckRuleCheck>();
            var all = deck.PlayCards;                 // tokens aren't part of the deck
            var side = all.Where(c => c.Category == DeckCardCategory.Sideboard).ToList();
            var main = all.Where(c => c.Category != DeckCardCategory.Sideboard).ToList();
            var leaders = main.Where(IsLeaderCard).ToList();

            // ── Leader (command zone) ──────────────────────────────────
            List<DeckCard> identityFrom = leaders;
            if (rule.HasLeader)
                checks.Add(LeaderCheck(rule, leaders, out identityFrom));

            // ── Size ───────────────────────────────────────────────────
            int size = Qty(main);
            string what = rule.HasLeader ? $"{rule.SizeText} cards, {LeaderWord(rule)} included" : $"{rule.SizeText} cards";
            bool sizeOk = rule.ExactSize ? size == rule.Size : size >= rule.Size;
            checks.Add(sizeOk
                ? DeckRuleCheck.Ok("Deck size", $"{size} cards ({what}).")
                : DeckRuleCheck.Problem("Deck size", $"{size} cards — {rule.Name} needs {what}."));

            // ── Copies ─────────────────────────────────────────────────
            if (rule.CopyLimit == 0)
            {
                checks.Add(DeckRuleCheck.Ok("Copies", "Any number of copies of a card."));
            }
            else
            {
                // Singleton formats have no sideboard: count the deck. 60-card
                // Constructed counts main deck and sideboard together.
                var counted = rule.SideboardMax == null ? main : all;
                // Seven Dwarves / Nazgûl: "up to seven / nine" of that card.
                // By card NAME (front face), whatever the set or printing.
                var over = counted.Where(CopyLimited)
                    .GroupBy(c => DeckIndexService.CardKey(c.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(g => (name: g.Key, count: Qty(g), limit: Math.Max(rule.CopyLimit, NamedLimit(g.First()))))
                    .Where(x => x.count > x.limit)
                    .OrderBy(x => x.name)
                    .Select(x => x.limit > rule.CopyLimit ? $"{x.name} × {x.count} (up to {x.limit})" : $"{x.name} × {x.count}")
                    .ToList();
                string limit = rule.IsSingleton
                    ? "Every card other than basic lands appears once."
                    : $"No card appears more than {rule.CopyLimit} times" +
                      (rule.SideboardMax != null ? " (main deck and sideboard together)." : ".");
                checks.Add(over.Count == 0
                    ? DeckRuleCheck.Ok(rule.IsSingleton ? "Singleton" : "Copies", limit)
                    : DeckRuleCheck.Problem(rule.IsSingleton ? "Singleton" : "Copies",
                        $"{over.Count} card name(s) appear more than {(rule.IsSingleton ? "once" : $"{rule.CopyLimit} times")}:", over));

                // Restricted (Vintage): at most one copy.
                if (rule.LegalityKey != null && !rule.HasLeader)
                {
                    var restricted = counted
                        .Where(c => c.Legality[rule.LegalityKey].Status == "restricted")
                        .GroupBy(c => DeckIndexService.CardKey(c.Name), StringComparer.OrdinalIgnoreCase)
                        .Select(g => (name: g.Key, count: Qty(g)))
                        .Where(x => x.count > 1)
                        .Select(x => $"{x.name} × {x.count}")
                        .ToList();
                    if (restricted.Count > 0)
                        checks.Add(DeckRuleCheck.Problem("Restricted",
                            $"{restricted.Count} restricted card(s) with more than 1 copy:", restricted));
                }
            }

            // ── Sideboard ──────────────────────────────────────────────
            int sb = Qty(side);
            if (rule.SideboardMax is int max)
                checks.Add(sb <= max
                    ? DeckRuleCheck.Ok("Sideboard", sb == 0 ? "No sideboard." : $"{sb} cards (maximum {max}).")
                    : DeckRuleCheck.Problem("Sideboard", $"{sb} cards — maximum {max}."));
            else if (rule.Type == DeckType.Limited)
                checks.Add(DeckRuleCheck.Ok("Sideboard",
                    sb == 0 ? "Every card you opened and didn't play is your sideboard (no limit)."
                            : $"{sb} card(s) — in Limited the cards you don't play are your sideboard (no limit)."));
            else if (sb > 0)
                checks.Add(DeckRuleCheck.Ok("Sideboard",
                    $"{sb} sideboard card(s) — {rule.Name} has no sideboard, so they aren't counted as part of the deck."));

            // ── Color identity ─────────────────────────────────────────
            if (rule.HasLeader && identityFrom.Count > 0)
            {
                var identity = new HashSet<char>(identityFrom.SelectMany(c => c.ColorIdentity).Where(IsColor));
                string idText = identity.Count == 0 ? "colorless" : string.Concat("WUBRG".Where(identity.Contains));
                var outside = main.Where(c => !identityFrom.Contains(c))
                    .Where(c => c.ColorIdentity.Any(ch => IsColor(ch) && !identity.Contains(ch)))
                    .Select(c => $"{c.Name} ({c.ColorIdentity})")
                    .Distinct().OrderBy(x => x).ToList();
                string whose = rule.Leader == DeckLeader.Oathbreaker ? "the Oathbreaker's" : "the commander's";
                checks.Add(outside.Count == 0
                    ? DeckRuleCheck.Ok("Color identity", $"All cards fit {whose} colors ({idText}).")
                    : DeckRuleCheck.Problem("Color identity", $"{outside.Count} card(s) outside {whose} colors ({idText}):", outside));
            }

            // ── Card legality (Scryfall) ───────────────────────────────
            if (rule.LegalityKey == null)
            {
                checks.Add(DeckRuleCheck.Ok("Legality", "Limited has no card list — any card you opened can be played."));
            }
            else
            {
                checks.Add(Legality(deck, rule, "Legality"));
            }

            return checks;
        }

        /// <summary>
        /// Card legality only (Scryfall's list for the format): which cards
        /// aren't allowed. <paramref name="title"/> names the line.
        /// </summary>
        private static DeckRuleCheck Legality(Deck deck, DeckFormatRule rule, string title)
        {
            string key = rule.LegalityKey ?? "";
            var all = deck.PlayCards;                 // tokens aren't part of the deck
            var main = all.Where(c => c.Category != DeckCardCategory.Sideboard).ToList();
            var leaders = main.Where(IsLeaderCard).ToList();
            var cards = rule.SideboardMax == null ? main : all;
            var bad = cards
                .Select(c => (card: c, status: c.Legality[key].Status))
                .Where(x => !StatusAllowed(rule, x.card, x.status, leaders))
                .Select(x => $"{x.card.Name} ({x.card.SetCode}) — {StatusText(rule, x.status)}")
                .Distinct().OrderBy(x => x).ToList();
            int unknown = cards.Count(c => string.IsNullOrEmpty(c.Legality[key].Status));
            string note = unknown > 0 ? $" {unknown} card(s) have no legality data (not in the card pool)." : "";
            string formatName = FormatName(rule);
            return bad.Count == 0
                ? DeckRuleCheck.Ok(title, $"Every card is legal in {formatName}.{note}")
                : DeckRuleCheck.Problem(title, $"{bad.Count} card(s) not legal in {formatName}.{note}", bad);
        }

        private static string FormatName(DeckFormatRule rule) =>
            rule.Name.StartsWith("Constructed — ") ? rule.Name[14..] : rule.Name;

        /// <summary>
        /// Card legality in every format this kind of deck could be played in:
        /// Constructed → each Constructed format (Standard, Pioneer, Modern, …);
        /// a command-zone deck → the command-zone formats of the same size
        /// (100: Commander, Brawl, Pauper Commander, Duel Commander; 60:
        /// Standard Brawl, Oathbreaker). Limited has no card list. One line per
        /// format, cards not allowed listed under it. The format being checked
        /// is marked. Only which cards are allowed — the other rules (size,
        /// leader, identity) are in the main check.
        /// </summary>
        public static List<(DeckRuleCheck Check, bool IsCurrent)> LegalityByFormat(Deck deck, DeckFormatRule current)
        {
            var list = new List<(DeckRuleCheck, bool)>();
            IEnumerable<DeckFormatRule> formats;
            if (current.Type == DeckType.Standard)
                formats = DeckFormats.ConstructedFormats.Select(f => DeckFormats.For(deck, f.Key));
            else if (current.HasLeader)
                formats = DeckFormats.All.Where(f => f.HasLeader && f.Size == current.Size);
            else
                return list;                            // Limited: no card list

            foreach (var f in formats)
            {
                bool isCurrent = f.LegalityKey == current.LegalityKey;
                list.Add((Legality(deck, f, FormatName(f)), isCurrent));
            }
            return list;
        }

        // ══════════════════════════════════════════════════════════════════
        // ADD WARNINGS — asked BEFORE a card goes into a deck (Edit → Decks).
        // Same rules as the check above; a warning, never a block: the user
        // can add anyway (a deck in progress, a house rule, a proxy…).
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// What adding <paramref name="adding"/> copies of <paramref name="card"/>
        /// to <paramref name="section"/> would break (empty = nothing). The copy
        /// limit counts the card's NAME across every printing and set in the
        /// deck (main deck and sideboard together for Constructed; the deck
        /// only for singleton formats).
        /// </summary>
        public static List<string> AddWarnings(Deck deck, DeckFormatRule rule, DeckCard card,
                                               DeckCardCategory section, int adding)
        {
            var warnings = new List<string>();
            // Tokens are play aids, not part of the deck: no rules apply.
            if (adding <= 0 || section == DeckCardCategory.Tokens) return warnings;
            var all = deck.PlayCards;

            bool toSide = section == DeckCardCategory.Sideboard;
            bool asLeader = section == DeckCardCategory.Commander;
            // Formats without a sideboard: sideboard cards aren't part of the deck.
            bool counts = !toSide || rule.SideboardMax != null || rule.Type == DeckType.Limited;
            var main = all.Where(c => c.Category != DeckCardCategory.Sideboard).ToList();
            string name = card.Name;
            string key = DeckIndexService.CardKey(name);

            // ── Copies, by name ─────────────────────────────────────────
            if (counts && rule.CopyLimit > 0 && CopyLimited(card))
            {
                var counted = rule.SideboardMax == null ? main : all;
                var same = counted.Where(c => string.Equals(DeckIndexService.CardKey(c.Name), key,
                                                            StringComparison.OrdinalIgnoreCase)).ToList();
                int have = Qty(same);
                int limit = Math.Max(rule.CopyLimit, NamedLimit(card));
                if (have + adding > limit)
                {
                    string where = have == 0 ? "" : " (" + string.Join(", ", same
                        .GroupBy(c => c.SetCode.ToUpperInvariant())
                        .Select(g => $"{Qty(g)}× {g.Key}")) + ")";
                    string allows = limit == 1 ? "1 copy" : $"{limit} copies";
                    string scope = rule.SideboardMax != null ? " (main deck and sideboard together, any printing)" : " (any printing)";
                    warnings.Add(have == 0
                        ? $"{name}: adding {adding} — {rule.Name} allows {allows}{scope}."
                        : $"{name}: {have} already in the deck{where} — {rule.Name} allows {allows}{scope}.");
                }

                // Vintage restricted: one copy.
                if (rule.LegalityKey != null && !rule.HasLeader &&
                    card.Legality[rule.LegalityKey].Status == "restricted" && have + adding > 1)
                    warnings.Add($"{name} is restricted in {FormatName(rule)} — 1 copy allowed.");
            }

            // ── Card legality (Scryfall) ────────────────────────────────
            if (counts && rule.LegalityKey != null)
            {
                string status = card.Legality[rule.LegalityKey].Status;
                var leaders = main.Where(IsLeaderCard).ToList();
                if (asLeader) leaders.Add(card);
                bool allowed = StatusAllowed(rule, card, status, leaders) ||
                               (status == "restricted" && rule.Leader == DeckLeader.None);   // counted above
                if (!allowed)
                    warnings.Add(status == "restricted" && rule.Leader == DeckLeader.PauperCommander
                        ? $"{name} is an uncommon — in Pauper Commander it can only be the commander."
                        : status switch
                        {
                            "banned" => $"{name} is banned in {FormatName(rule)}.",
                            "restricted" => $"{name} is restricted in {FormatName(rule)}.",
                            _ => $"{name} is not legal in {FormatName(rule)}.",
                        });
            }

            // ── Color identity (command-zone formats) ──────────────────
            if (rule.HasLeader && !toSide && !asLeader)
            {
                var leaders = main.Where(IsLeaderCard).ToList();
                var from = rule.Leader == DeckLeader.Oathbreaker
                    ? leaders.Where(c => FrontIs(c, "Planeswalker")).ToList()
                    : leaders;
                if (from.Count > 0)
                {
                    var identity = new HashSet<char>(from.SelectMany(c => c.ColorIdentity).Where(IsColor));
                    string outside = string.Concat("WUBRG".Where(ch => card.ColorIdentity.Contains(ch) && !identity.Contains(ch)));
                    if (outside.Length > 0)
                    {
                        string idText = identity.Count == 0 ? "colorless" : string.Concat("WUBRG".Where(identity.Contains));
                        string whose = rule.Leader == DeckLeader.Oathbreaker ? "the Oathbreaker's" : "the commander's";
                        warnings.Add($"{name} ({card.ColorIdentity}) is outside {whose} colors ({idText}).");
                    }
                }
            }

            // ── Deck size / sideboard size ──────────────────────────────
            if (!toSide && rule.ExactSize)
            {
                int size = Qty(main);
                if (size + adding > rule.Size)
                    warnings.Add($"The deck would have {size + adding} cards — {rule.Name} needs exactly {rule.Size}.");
            }
            if (toSide && rule.SideboardMax is int max)
            {
                int sb = Qty(all.Where(c => c.Category == DeckCardCategory.Sideboard));
                if (sb + adding > max)
                    warnings.Add($"The sideboard would have {sb + adding} cards — maximum {max}.");
            }

            return warnings;
        }

        /// <summary>
        /// Can this card lead the deck (commander, Oathbreaker or Signature
        /// Spell)? Null = yes; otherwise why not. Used when marking a card.
        /// </summary>
        public static string? LeaderProblem(DeckFormatRule rule, DeckCard c)
        {
            bool legendary = FrontIs(c, "Legendary");
            return rule.Leader switch
            {
                DeckLeader.None => $"{rule.Name} has no commander.",
                DeckLeader.Oathbreaker =>
                    FrontIs(c, "Planeswalker") || FrontIs(c, "Instant") || FrontIs(c, "Sorcery")
                        ? null : $"{c.Name} isn't a planeswalker (Oathbreaker) or an instant or sorcery (Signature Spell).",
                DeckLeader.Brawl =>
                    legendary && (FrontIs(c, "Creature") || FrontIs(c, "Planeswalker")) || CanBeCommander(c) || FrontIs(c, "Background")
                        ? null : $"{c.Name} isn't a legendary creature or planeswalker.",
                DeckLeader.PauperCommander =>
                    FrontIs(c, "Creature") &&
                    (c.Legality["paupercommander"].Status is "legal" or "restricted" ||
                     string.Equals(c.Rarity, "uncommon", StringComparison.OrdinalIgnoreCase))
                        ? null : $"{c.Name} isn't an uncommon creature.",
                _ => legendary && FrontIs(c, "Creature") || CanBeCommander(c) || FrontIs(c, "Background")
                        ? null : $"{c.Name} isn't a legendary creature (and doesn't say it can be your commander).",
            };
        }

        /// <summary>"Commander", "Oathbreaker or Signature Spell" — what a leader is called in this format.</summary>
        public static string LeaderName(DeckFormatRule rule) =>
            rule.Leader == DeckLeader.Oathbreaker ? "Oathbreaker / Signature Spell" : "Commander";

        /// <summary>One line for the deck view: "Commander · all rules met" / "… · 2 problems: Deck size, Legality".</summary>
        public static string SummaryLine(DeckFormatRule rule, List<DeckRuleCheck> checks)
        {
            var problems = checks.Where(c => !c.IsOk).Select(c => c.Title).ToList();
            return problems.Count == 0
                ? $"{rule.Name} · all deck rules met"
                : $"{rule.Name} · {problems.Count} problem{(problems.Count == 1 ? "" : "s")}: {string.Join(", ", problems)}";
        }

        // ── Leader ─────────────────────────────────────────────────────
        private static DeckRuleCheck LeaderCheck(DeckFormatRule rule, List<DeckCard> leaders, out List<DeckCard> identityFrom)
        {
            identityFrom = leaders;
            string names = string.Join(" & ", leaders.Select(c => c.Name));

            if (rule.Leader == DeckLeader.Oathbreaker)
            {
                // Front face only: a card whose BACK is a planeswalker isn't one.
                var walkers = leaders.Where(c => FrontIs(c, "Planeswalker")).ToList();
                var spells = leaders.Where(c => FrontIs(c, "Instant") || FrontIs(c, "Sorcery")).ToList();
                identityFrom = walkers;
                if (walkers.Count == 1 && spells.Count == 1 && leaders.Count == 2)
                    return DeckRuleCheck.Ok("Oathbreaker", $"{walkers[0].Name} with the signature spell {spells[0].Name}.");
                var issues = new List<string>();
                if (walkers.Count != 1) issues.Add($"{walkers.Count} planeswalker(s) marked — exactly 1 Oathbreaker is needed.");
                if (spells.Count != 1) issues.Add($"{spells.Count} instant/sorcery marked — exactly 1 Signature Spell is needed.");
                issues.AddRange(leaders.Except(walkers).Except(spells)
                    .Select(c => $"{c.Name} can't be the Oathbreaker or Signature Spell."));
                return DeckRuleCheck.Problem("Oathbreaker", "Mark one planeswalker and one instant or sorcery as the command zone.", issues);
            }

            if (leaders.Count == 0)
                return DeckRuleCheck.Problem("Commander", "No card is marked as the commander.");

            var bad = new List<string>();
            bool pairedWithBackground(DeckCard c) =>
                FrontIs(c, "Background") && leaders.Any(o => o != c && HasAbility(o, "Choose a Background"));

            foreach (var c in leaders)
            {
                bool legendary = FrontIs(c, "Legendary");
                string? why = rule.Leader switch
                {
                    DeckLeader.Brawl =>
                        legendary && (FrontIs(c, "Creature") || FrontIs(c, "Planeswalker")) || CanBeCommander(c) || pairedWithBackground(c)
                            ? null : "must be a legendary creature or planeswalker",
                    // An uncommon creature. Scryfall marks uncommons "restricted" in
                    // Pauper Commander (commander only); a creature printed at both
                    // common and uncommon shows "legal", so that's accepted too.
                    DeckLeader.PauperCommander =>
                        FrontIs(c, "Creature") &&
                        (c.Legality["paupercommander"].Status is "legal" or "restricted" ||
                         string.Equals(c.Rarity, "uncommon", StringComparison.OrdinalIgnoreCase))
                            ? null : "must be an uncommon creature",
                    _ => legendary && FrontIs(c, "Creature") || CanBeCommander(c) || pairedWithBackground(c)
                            ? null : "must be a legendary creature (or say it can be your commander)",
                };
                if (why != null) bad.Add($"{c.Name} — {why}");
            }

            if (leaders.Count > 2)
                bad.Add($"{leaders.Count} cards are marked — at most 2 (a partner pair).");
            else if (leaders.Count == 2 && PairProblem(leaders[0], leaders[1]) is { } pairWhy)
                bad.Add(pairWhy);

            return bad.Count == 0
                ? DeckRuleCheck.Ok("Commander", names)
                : DeckRuleCheck.Problem("Commander", names, bad);
        }

        /// <summary>
        /// Two commanders must be a real pair: Partner + Partner, "Partner with
        /// X" + X, Friends forever + Friends forever, Choose a Background + a
        /// Background, or Doctor's companion + a Time Lord Doctor. Null = fine.
        /// </summary>
        private static string? PairProblem(DeckCard a, DeckCard b)
        {
            bool Pair(DeckCard x, DeckCard y)
            {
                if (PartnerWith(x) is { } named)
                    return string.Equals(named, y.Name, StringComparison.OrdinalIgnoreCase) ||
                           y.Name.StartsWith(named + " //", StringComparison.OrdinalIgnoreCase);
                // "Partner—Survivors" style: only with the same kind of Partner.
                if (PartnerVariant(x) is { } kind)
                    return PartnerVariant(y) == kind;
                if (HasAbility(x, "Partner") && PartnerWith(x) == null)
                    return HasAbility(y, "Partner") && PartnerWith(y) == null && PartnerVariant(y) == null;
                if (HasAbility(x, "Friends forever")) return HasAbility(y, "Friends forever");
                if (HasAbility(x, "Choose a Background")) return FrontIs(y, "Background");
                if (FrontIs(x, "Background")) return HasAbility(y, "Choose a Background");
                if (HasAbility(x, "Doctor's companion")) return FrontIs(y, "Time Lord Doctor");
                if (FrontIs(x, "Time Lord Doctor")) return HasAbility(y, "Doctor's companion");
                return false;
            }
            return Pair(a, b) && Pair(b, a)
                ? null
                : $"{a.Name} and {b.Name} can't be commanders together — they need Partner, Partner with each other, " +
                  "Friends forever, Choose a Background + a Background, or Doctor's companion + a Doctor.";
        }

        /// <summary>A keyword ability at the start of a rules-text line ("Partner", "Friends forever", …).</summary>
        private static bool HasAbility(DeckCard c, string ability) =>
            (c.OracleText ?? "").Split('\n').Any(line =>
                line.TrimStart().StartsWith(ability, StringComparison.OrdinalIgnoreCase));

        /// <summary>"Partner with Pir, Imaginative Rascal" → "Pir, Imaginative Rascal" (null if not that ability).</summary>
        private static string? PartnerWith(DeckCard c)
        {
            foreach (var raw in (c.OracleText ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("Partner with ", StringComparison.OrdinalIgnoreCase)) continue;
                string name = line[13..];
                int cut = name.IndexOf(" (", StringComparison.Ordinal);   // reminder text
                return (cut > 0 ? name[..cut] : name).Trim();
            }
            return null;
        }

        /// <summary>"Partner—Survivors" → "Survivors" (null for plain Partner or none).</summary>
        private static string? PartnerVariant(DeckCard c)
        {
            foreach (var raw in (c.OracleText ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("Partner", StringComparison.OrdinalIgnoreCase) || line.Length < 9) continue;
                char sep = line[7];
                if (sep != '—' && sep != '-') continue;
                string kind = line[8..];
                int cut = kind.IndexOf(" (", StringComparison.Ordinal);
                return (cut > 0 ? kind[..cut] : kind).Trim().ToLowerInvariant();
            }
            return null;
        }

        /// <summary>The FRONT face's type line has this word (the rules look at the front only).</summary>
        private static bool FrontIs(DeckCard c, string type)
        {
            string front = c.TypeLine ?? "";
            int i = front.IndexOf(" // ", StringComparison.Ordinal);
            if (i >= 0) front = front[..i];
            return front.Contains(type, StringComparison.OrdinalIgnoreCase);
        }

        private static string LeaderWord(DeckFormatRule rule) =>
            rule.Leader == DeckLeader.Oathbreaker ? "Oathbreaker and Signature Spell" : "commander";

        // ── Card tests ─────────────────────────────────────────────────
        public static bool IsLeaderCard(DeckCard c) => c.IsCommander || c.Category == DeckCardCategory.Commander;

        private static bool CanBeCommander(DeckCard c) =>
            c.OracleText.Contains("can be your commander", StringComparison.OrdinalIgnoreCase);

        /// <summary>Basic lands (snow ones too) and "a deck can have any number" cards aren't copy-limited.</summary>
        private static bool CopyLimited(DeckCard c) =>
            !(c.TypeLine.StartsWith("Basic", StringComparison.OrdinalIgnoreCase) &&
              c.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)) &&
            !c.IsAnyNumber;

        private static readonly System.Text.RegularExpressions.Regex UpToRx = new(
            @"A deck can have up to (\w+) cards named", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>"A deck can have up to seven cards named Seven Dwarves" → 7 (0 when the card says nothing).</summary>
        private static int NamedLimit(DeckCard c)
        {
            var m = UpToRx.Match(c.OracleText ?? "");
            if (!m.Success) return 0;
            string w = m.Groups[1].Value.ToLowerInvariant();
            if (int.TryParse(w, out int n)) return n;
            string[] words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                               "eleven", "twelve", "thirteen", "fourteen", "fifteen" };
            int i = Array.IndexOf(words, w);
            return i >= 0 ? i : 0;
        }

        /// <summary>
        /// Is Scryfall's status fine here? "legal" always. "restricted": 1 copy
        /// in Constructed (checked above); in Pauper Commander it means "may be
        /// the commander" (uncommons). Anything else is a problem.
        /// </summary>
        private static bool StatusAllowed(DeckFormatRule rule, DeckCard card, string status, List<DeckCard> leaders)
        {
            if (string.IsNullOrEmpty(status)) return true;       // unknown: noted separately
            if (status == "legal") return true;
            if (status != "restricted") return false;
            return rule.Leader switch
            {
                DeckLeader.None => true,
                DeckLeader.PauperCommander => leaders.Contains(card),
                _ => false,
            };
        }

        private static string StatusText(DeckFormatRule rule, string status) =>
            status == "restricted" && rule.Leader == DeckLeader.PauperCommander
                ? "uncommon (commander only)"
                : LegalityInfo.ChipText(status);

        private static bool IsColor(char c) => "WUBRG".IndexOf(c) >= 0;

        private static int Qty(IEnumerable<DeckCard> cards) => cards.Sum(c => c.TotalQuantity);
    }
}
