using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BreakersOfE.Data;
using BreakersOfE.DeckBuilder;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>What the builder was given, and what it made.</summary>
    public sealed class BuildRun
    {
        public DraftDeck Draft { get; init; } = new();
        /// <summary>Cards it could use, and why the rest were left out.</summary>
        public int EligibleCount { get; init; }
        public Exclusions Left { get; init; } = new();
        /// <summary>What it had to go on ("card tags", "EDHREC", "combos" …) and what was missing.</summary>
        public List<string> Sources { get; } = new();
    }

    /// <summary>What a build works from, gathered once (PrepareAsync): kept by Guided Build for all its steps.</summary>
    public sealed class BuildContext
    {
        /// <summary>With the commanders' names and color identity filled in.</summary>
        public BuildConstraints Constraints { get; init; } = new();
        public List<Candidate> Commanders { get; init; } = new();
        public List<Eligible> Eligible { get; init; } = new();
        public Exclusions Left { get; init; } = new();
        public List<ComboInfo> Combos { get; init; } = new();
        public List<string> Sources { get; } = new();
    }

    /// <summary>
    /// What can I build: one idea and how much of a deck your free cards make
    /// for it — a commander you own (or a partner pair), or for 60-card decks
    /// a color combination.
    /// </summary>
    public sealed class BuildIdea
    {
        /// <summary>The commander (Commander ideas; empty for 60-card).</summary>
        public DeckBuildService.CommanderChoice Commander { get; init; } = new("", 0, 0);
        /// <summary>The second commander of a partner pair (Partner, Background …), or null.</summary>
        public DeckBuildService.CommanderChoice? Partner { get; init; }
        /// <summary>60-card: the colors ("RG"); Commander: the commanders' identity.</summary>
        public string ColorLetters { get; init; } = "";
        /// <summary>Free spells (not lands; 60-card: copies, up to 4 each) in its colors, and the deck's spell slots.</summary>
        public int Spells { get; init; }
        public int SpellSlots { get; init; }
        /// <summary>Free nonbasic lands in its colors.</summary>
        public int Lands { get; init; }
        /// <summary>How far the free cards go toward the guide's roles (0–1).</summary>
        public double RoleFill { get; init; }
        /// <summary>The roles they fall short in ("Ramp 4/10").</summary>
        public List<string> ShortOf { get; init; } = new();
        /// <summary>Commander: "23 of its top 100" (EDHREC page saved). 60-card: playsets and the most common creature type.</summary>
        public string Edhrec { get; init; } = "";
        public double Score { get; init; }

        public bool IsConstructed => Commander.Name.Length == 0;
        public string Name => IsConstructed ? ColorNames(ColorLetters)
            : Partner == null ? Commander.Name : $"{Commander.Name} + {Partner.Name}";
        public string Colors => ColorLetters.Length == 0 ? "C" : ColorLetters;
        public string SpellsText => $"{Math.Min(Spells, SpellSlots)} of {SpellSlots}" + (Spells > SpellSlots ? $" (+{Spells - SpellSlots})" : "");
        public string RolesText => $"{RoleFill * 100:0}%";
        public string ShortText => ShortOf.Count == 0 ? "—" : string.Join(", ", ShortOf);
        public string EdhrecText => Edhrec;
        public string ReadyText => $"{Score * 100:0}%";
        public string CommanderText => IsConstructed ? "—"
            : Commander.IsFree && (Partner == null || Partner.IsFree) ? "Free" : "Yours, in use";

        private static string ColorNames(string letters) => letters.Length == 0 ? "Colorless" :
            string.Join(" + ", letters.Select(ch => ch switch
            {
                'W' => "White", 'U' => "Blue", 'B' => "Black", 'R' => "Red", 'G' => "Green", _ => ch.ToString(),
            }));
    }

    /// <summary>
    /// Feeds the deck builder (BreakersOfE.DeckBuilder) with your data: your
    /// collection's free copies, the card pool (when cards to get are allowed),
    /// Scryfall's card tags, EDHREC for the commander, salt scores, Game
    /// Changers and Commander Spellbook's combos. Anything not downloaded is
    /// simply left out (and said so); the builder still works from the cards'
    /// rules text.
    /// </summary>
    public static class DeckBuildService
    {
        /// <summary>Build a draft Commander deck for the commander (partners: both names).</summary>
        public static async Task<BuildRun> BuildCommanderAsync(IReadOnlyList<string> commanderNames, BuildConstraints constraints,
                                                               CancellationToken ct = default, IProgress<string>? progress = null)
        {
            var ctx = await PrepareAsync(commanderNames, constraints, ct, progress, "commander");
            progress?.Report("Picking the cards…");
            return Build(ctx, ctx.Constraints, ct);
        }

        /// <summary>
        /// Everything a build needs, gathered once: the cards it may use (with
        /// EDHREC, tags, salt), the commanders, the combos. Guided Build keeps
        /// it for all its steps; a plain build uses it once.
        /// <paramref name="legalityKey"/>: the format whose card list counts
        /// ("commander", or a 60-card format's: "standard", "modern" …).
        /// </summary>
        public static async Task<BuildContext> PrepareAsync(IReadOnlyList<string> commanderNames, BuildConstraints constraints,
                                                            CancellationToken ct = default, IProgress<string>? progress = null,
                                                            string legalityKey = "commander")
        {
            var sources = new List<string>();
            progress?.Report(constraints.Sources == SourceMode.CollectionAndPool
                ? "Reading your collection and the card pool…" : "Reading your collection…");
            var candidates = await Task.Run(() => Candidates(constraints.Sources == SourceMode.CollectionAndPool, sources, ct, legalityKey), ct);
            ct.ThrowIfCancellationRequested();
            if (constraints.IsConstructed) return await PrepareConstructedAsync(candidates, constraints, sources, ct, progress);

            // The commander(s): their color identity limits the deck.
            var commanders = commanderNames
                .Select(n => candidates.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                             ?? PoolCandidate(n))
                .Where(x => x != null).Select(x => x!).ToList();
            if (commanders.Count == 0) throw new InvalidOperationException($"\"{string.Join(" + ", commanderNames)}\" isn't in the card pool.");
            string identity = new string("WUBRG".Where(ch => commanders.Any(x => x.ColorIdentity.Contains(ch))).ToArray());
            var c = constraints.With(commanders.Select(x => x.Name).ToList(), identity);

            // EDHREC for this commander (saved for 7 days; never required).
            try
            {
                progress?.Report("Getting EDHREC's suggestions for the commander…");
                var page = await EdhrecService.GetCommanderAsync(c.Commanders, refresh: false, ct);
                var byName = page.Cards.GroupBy(x => DeckDataService.FrontName(x.Name), StringComparer.OrdinalIgnoreCase)
                                       .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Select(x => byName.TryGetValue(DeckDataService.FrontName(x.Name), out var e)
                    ? With(x, inclusion: e.InclusionPercent >= 0 ? e.InclusionPercent : null, synergy: e.Synergy) : x).ToList();
                sources.Add($"EDHREC ({page.Cards.Count:N0} recommended cards" + (page.Note.Length > 0 ? ", saved copy" : "") + ")");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                sources.Add($"EDHREC: not available ({ex.Message})");
            }
            ct.ThrowIfCancellationRequested();

            progress?.Report("Sorting the cards by what they do…");
            var (eligible, left) = CandidatePool.Build(candidates, c);
            ct.ThrowIfCancellationRequested();

            // Combos among the cards it could use (Commander Spellbook).
            var combos = CombosAmong(eligible.Select(e => e.Card.Name).Concat(c.Commanders), sources, progress);
            ct.ThrowIfCancellationRequested();

            var ctx = new BuildContext { Constraints = c, Commanders = commanders, Eligible = eligible, Left = left, Combos = combos };
            ctx.Sources.AddRange(sources);
            return ctx;
        }

        /// <summary>A 60-card deck: no commander or EDHREC; the colors chosen; combos among the legal cards.</summary>
        private static Task<BuildContext> PrepareConstructedAsync(List<Candidate> candidates, BuildConstraints constraints,
                                                                  List<string> sources, CancellationToken ct, IProgress<string>? progress)
        {
            string identity = new string("WUBRG".Where(ch => constraints.ColorIdentity.ToUpperInvariant().Contains(ch)).ToArray());
            var c = constraints.With(Array.Empty<string>(), identity);
            sources.Add("EDHREC: commander decks only (not used for 60-card)");
            progress?.Report("Sorting the cards by what they do…");
            var (eligible, left) = CandidatePool.Build(candidates, c);
            ct.ThrowIfCancellationRequested();
            var combos = CombosAmong(eligible.Select(e => e.Card.Name), sources, progress);
            ct.ThrowIfCancellationRequested();
            var ctx = new BuildContext { Constraints = c, Commanders = new(), Eligible = eligible, Left = left, Combos = combos };
            ctx.Sources.AddRange(sources);
            return Task.FromResult(ctx);
        }

        /// <summary>Commander Spellbook's combos among these cards (empty, and said so, when not downloaded).</summary>
        private static List<ComboInfo> CombosAmong(IEnumerable<string> names, List<string> sources, IProgress<string>? progress)
        {
            if (!DeckDataService.HasCombos) { sources.Add("Combos: not downloaded (run Update Database)"); return new(); }
            progress?.Report("Looking for combos…");
            var combos = DeckDataService.CombosWithin(names.ToList())
                .Select(k => new ComboInfo
                {
                    Cards = k.Cards, Results = string.Join("; ", k.Results),
                    TwoCardInfinite = k.Cards.Count == 2 && DeckDataService.IsInfiniteOrWin(k),
                }).ToList();
            sources.Add($"Commander Spellbook ({combos.Count:N0} combos among these cards)");
            return combos;
        }

        /// <summary>Build from a prepared context (<paramref name="c"/>: its constraints, perhaps with other locked cards).</summary>
        public static BuildRun Build(BuildContext ctx, BuildConstraints c, CancellationToken ct = default)
        {
            var draft = Builder.Build(ctx.Eligible, c, ctx.Commanders, ctx.Combos, ct);
            var run = new BuildRun { Draft = draft, EligibleCount = ctx.Eligible.Count, Left = ctx.Left };
            run.Sources.AddRange(ctx.Sources);
            return run;
        }

        /// <summary>Guided Build: the best options for one step, with <paramref name="chosen"/> already in the deck.</summary>
        public static List<Pick> Options(BuildContext ctx, IReadOnlyCollection<string> chosen, Role? role, int max) =>
            Builder.Options(ctx.Eligible, ctx.Constraints.WithLocked(chosen), role, max, ctx.Commanders, ctx.Combos);

        // ══ Commanders to choose from ═══════════════════════════════════════

        /// <summary>A card that can be a commander: its name, copies you own, and how many are free (not in a deck or the Trade Binder).</summary>
        public sealed record CommanderChoice(string Name, int Owned, int Free)
        {
            public string TypeLine { get; init; } = "";
            public string OracleText { get; init; } = "";
            public string ColorIdentity { get; init; } = "";
            /// <summary>A Background: only a second commander (with Choose a Background), never the first.</summary>
            public bool IsBackground { get; init; }
            /// <summary>It can have a second commander (Partner, Choose a Background …).</summary>
            public bool CanHaveSecond => Partners.CanHaveSecond(TypeLine, OracleText);
            public bool CanPairWith(CommanderChoice other) =>
                Partners.CanPair(Name, TypeLine, OracleText, other.Name, other.TypeLine, other.OracleText);
            public bool IsFree => Free > 0;
            public string Display => Free > 0 ? Name : Owned > 0 ? Name + "  (yours, in use)" : Name + "  (not owned)";
            public override string ToString() => Name;
        }

        /// <summary>Every Commander-legal legendary creature, "can be your commander" card and Background, A→Z.</summary>
        public static List<CommanderChoice> CommanderChoices()
        {
            var owned = OwnedByName();
            using var db = new AppDbContext();
            var rows = db.PoolCards.AsNoTracking()
                .Where(x => !x.IsToken && (x.TypeLine.Contains("Legendary") && (x.TypeLine.Contains("Creature") || x.TypeLine.Contains("Background")) ||
                                           x.OracleText.Contains("can be your commander")) &&
                            x.LegalitiesJson.Contains("\"commander\":\"legal\""))
                .Select(x => new { x.Name, x.TypeLine, x.OracleText, x.ColorIdentity })
                .ToList()
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
            return rows.Select(r =>
                {
                    var o = owned.GetValueOrDefault(r.Name);
                    string front = r.TypeLine.Split(" // ")[0];
                    return new CommanderChoice(r.Name, o.Owned, o.Free)
                    {
                        TypeLine = r.TypeLine ?? "", OracleText = r.OracleText ?? "", ColorIdentity = r.ColorIdentity ?? "",
                        IsBackground = front.Contains("Background") && !front.Contains("Creature"),
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();   // A→Z; the label says free / in use / not owned
        }

        /// <summary>Copies you own and free copies, by card name.</summary>
        private static Dictionary<string, (int Owned, int Free)> OwnedByName()
        {
            var owned = new Dictionary<string, (int Owned, int Free)>(StringComparer.OrdinalIgnoreCase);
            using var cdb = new CollectionDbContext();
            foreach (var x in cdb.CollectionEntries.AsNoTracking().Where(x => x.Quantity > 0)
                         .Select(x => new { x.Name, x.Quantity, x.UsedCount }).ToList())
            {
                var had = owned.GetValueOrDefault(x.Name);
                owned[x.Name] = (had.Owned + x.Quantity, had.Free + Math.Max(0, x.Quantity - Math.Max(0, x.UsedCount)));
            }
            return owned;
        }

        /// <summary>A card's pool row by name (the printing shown in the card panel), or null.</summary>
        public static Models.PoolCard? PoolCardByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            using var db = new AppDbContext();
            return db.PoolCards.AsNoTracking().FirstOrDefault(x => x.Name == name && !x.IsToken)
                ?? db.PoolCards.AsNoTracking().FirstOrDefault(x => x.Name.StartsWith(name + " // ") && !x.IsToken);
        }

        /// <summary>A card typed by name (any case; a double-faced card by its front name), or null. Not a token.</summary>
        public static Models.PoolCard? FindCardByName(string typed)
        {
            string name = (typed ?? "").Trim();
            if (name.Length == 0) return null;
            if (PoolCardByName(name) is { } exact) return exact;
            string lower = name.ToLowerInvariant();
            using var db = new AppDbContext();
            return db.PoolCards.AsNoTracking().FirstOrDefault(x => !x.IsToken && x.Name.ToLower() == lower)
                ?? db.PoolCards.AsNoTracking().FirstOrDefault(x => !x.IsToken && x.Name.ToLower().StartsWith(lower + " // "));
        }

        // ══ Rule 0 sheet for a deck ═════════════════════════════════════════

        /// <summary>
        /// The Rule 0 sheet for a saved Commander deck (Deck Statistics): its
        /// main deck and commander as the engine sees them — roles from card
        /// tags (else rules text), Game Changers, salt, and the combos among its
        /// cards. Bracket aimed for isn't known for a deck, so it's left out.
        /// </summary>
        public static string Rule0For(Models.Deck deck)
        {
            var lines = deck.Cards.Where(c => !c.IsTokenLine && c.Category != Models.DeckCardCategory.Sideboard).ToList();
            var sids = lines.Select(c => c.ScryfallId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            Dictionary<string, string> oracleOf;
            using (var db = new AppDbContext())
                oracleOf = db.PoolCards.AsNoTracking().Where(p => sids.Contains(p.ScryfallId))
                             .Select(p => new { p.ScryfallId, p.OracleId }).ToList()
                             .GroupBy(p => p.ScryfallId).ToDictionary(g => g.Key, g => g.First().OracleId ?? "");
            var tags = DeckDataService.TagsForMany(oracleOf.Values.Where(x => x.Length > 0).Distinct());
            bool salt = EdhrecService.HasSalt;

            var draft = new DraftDeck();
            foreach (var g in lines.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var c = g.First();
                string oracle = oracleOf.GetValueOrDefault(c.ScryfallId) ?? "";
                var card = new Candidate
                {
                    Name = c.Name, OracleId = oracle, TypeLine = c.TypeLine, OracleText = c.OracleText, ManaCost = c.ManaCost,
                    ManaValue = c.ManaValue, ColorIdentity = c.ColorIdentity, IsGameChanger = g.Any(x => x.IsGameChanger),
                    Tags = oracle.Length > 0 && tags.TryGetValue(oracle, out var t) ? t : Array.Empty<string>(),
                    Price = c.PriceUsd, Salt = salt ? EdhrecService.SaltOf(c.Name)?.Score : null,
                };
                var (roles, _) = RoleClassifier.Classify(card);
                bool commander = g.Any(x => DeckEditService.SectionOf(x) == Models.DeckCardCategory.Commander);
                draft.Picks.Add(new Pick { Card = card, Roles = roles, Copies = g.Sum(x => x.TotalQuantity), IsCommander = commander });
            }
            draft.CardCount = draft.Picks.Sum(p => p.Copies);
            if (DeckDataService.HasCombos)
            {
                var names = new HashSet<string>(draft.Picks.Select(p => p.Card.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var k in DeckDataService.CombosWithin(names).Where(k => k.Cards.All(names.Contains)))
                    draft.Combos.Add(new ComboInfo
                    {
                        Cards = k.Cards, Results = string.Join("; ", k.Results),
                        TwoCardInfinite = k.Cards.Count == 2 && DeckDataService.IsInfiniteOrWin(k),
                    });
            }
            var commanders = draft.Picks.Where(p => p.IsCommander).Select(p => p.Card).ToList();
            string identity = new string("WUBRG".Where(ch => commanders.Any(x => x.ColorIdentity.ToUpperInvariant().Contains(ch))).ToArray());
            var c0 = new BuildConstraints { Commanders = commanders.Select(x => x.Name).ToList(), ColorIdentity = identity, Bracket = 0 };
            return Rule0.Sheet(draft, c0, deck.Name);
        }

        // ══ What can I build? ═══════════════════════════════════════════════

        /// <summary>A free card for What can I build: its roles, colors and free copies.</summary>
        private sealed record FreeCard(Candidate Card, HashSet<Role> Roles, HashSet<char> Colors);

        private static List<FreeCard> FreeCards(string legalityKey, CancellationToken ct, IProgress<string>? progress)
        {
            progress?.Report("Reading your collection…");
            var sources = new List<string>();
            var free = Candidates(false, sources, ct, legalityKey).Where(x => x.FreeCopies > 0 && x.Legal && !x.IsBasicLand).ToList();
            ct.ThrowIfCancellationRequested();
            progress?.Report("Sorting your cards by what they do…");
            return free.Select(x => new FreeCard(x, RoleClassifier.Classify(x).Roles,
                                                 new HashSet<char>(x.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)))))
                       .ToList();
        }

        /// <summary>
        /// For each commander you own (a free copy; with <paramref name="includeInUse"/>
        /// also ones in decks) — and each partner pair you own (Partner, Partner
        /// with, Friends forever, a Background …) — how much of a deck your free
        /// cards already make: spells in its colors, how far they go toward the
        /// guide's roles, and (when EDHREC's page is saved) how many of its most-played
        /// cards are free. Best first. Only your collection is read; nothing is downloaded.
        /// </summary>
        public static List<BuildIdea> WhatCanIBuild(bool includeInUse, CancellationToken ct = default, IProgress<string>? progress = null)
        {
            var cards = FreeCards("commander", ct, progress);
            var guide = RoleGuide.CommanderDefault();
            int spellSlots = 99 - (guide.For(Role.Land)?.Count ?? 37);

            progress?.Report("Finding your commanders…");
            var all = CommanderChoices().Where(x => x.IsFree || includeInUse && x.Owned > 0).ToList();
            var commanders = all.Where(x => !x.IsBackground).ToList();
            var ideas = new List<BuildIdea>();

            BuildIdea Evaluate(DeckBuildService.CommanderChoice cmd, DeckBuildService.CommanderChoice? partner)
            {
                var identity = new HashSet<char>((cmd.ColorIdentity + (partner?.ColorIdentity ?? "")).ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)));
                var fits = cards.Where(x => x.Colors.IsSubsetOf(identity) &&
                                            !x.Card.Name.Equals(cmd.Name, StringComparison.OrdinalIgnoreCase) &&
                                            !(partner != null && x.Card.Name.Equals(partner.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                int spells = fits.Count(x => !x.Card.IsLand);
                int lands = fits.Count(x => x.Card.IsLand);
                var (roleFill, shortOf) = RoleFill(guide, fits, copies: false);
                double spellFill = Math.Min(1.0, spells / (double)Math.Max(1, spellSlots - (partner != null ? 1 : 0)));

                // EDHREC's page, only if it's saved already.
                string edhrec = "";
                double edhrecFill = -1;
                var names = partner == null ? new[] { cmd.Name } : new[] { cmd.Name, partner.Name };
                var page = EdhrecService.Saved(names);
                if (page != null)
                {
                    var top = page.Cards.Where(x => x.InclusionPercent >= 0).OrderByDescending(x => x.InclusionPercent).Take(100)
                                        .Select(x => DeckDataService.FrontName(x.Name)).ToList();
                    var freeNames = new HashSet<string>(fits.Select(x => DeckDataService.FrontName(x.Card.Name)), StringComparer.OrdinalIgnoreCase);
                    int have = top.Count(freeNames.Contains);
                    if (top.Count > 0) { edhrecFill = have / (double)top.Count; edhrec = $"{have} of its top {top.Count}"; }
                }
                double score = 0.55 * spellFill + 0.45 * roleFill;
                if (edhrecFill >= 0) score = 0.7 * score + 0.3 * Math.Min(1.0, edhrecFill * 2);
                return new BuildIdea
                {
                    Commander = cmd, Partner = partner, ColorLetters = new string("WUBRG".Where(identity.Contains).ToArray()),
                    Spells = spells, Lands = lands, SpellSlots = spellSlots - (partner != null ? 1 : 0),
                    RoleFill = roleFill, ShortOf = shortOf, Edhrec = edhrec, Score = score,
                };
            }

            int done = 0;
            foreach (var cmd in commanders)
            {
                ct.ThrowIfCancellationRequested();
                if (++done % 20 == 0) progress?.Report($"Checking commanders… {done:N0} of {commanders.Count:N0}");
                ideas.Add(Evaluate(cmd, null));
            }

            // Partner pairs you own: each pair once (a Background always second).
            progress?.Report("Checking partner pairs…");
            var pairable = all.Where(x => x.CanHaveSecond).ToList();
            foreach (var a in pairable.Where(x => !x.IsBackground))
                foreach (var b in pairable)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!b.IsBackground && string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!a.CanPairWith(b)) continue;
                    ideas.Add(Evaluate(a, b));
                }
            return ideas.OrderByDescending(x => x.Score).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 60-card: for each color combination (one, two and three colors, and
        /// colorless), how much of a deck your free cards legal in the format
        /// make — spell copies (up to 4 each), playsets, the guide's roles, and the
        /// most common creature type (a tribe to build around). Best first.
        /// </summary>
        public static List<BuildIdea> WhatCanIBuild60(string legalityKey, CancellationToken ct = default, IProgress<string>? progress = null)
        {
            var cards = FreeCards(legalityKey, ct, progress).Where(x => !CandidatePool.NeedsACommander(x.Card)).ToList();
            var guide = RoleGuide.ConstructedDefault();
            int spellSlots = 60 - (guide.For(Role.Land)?.Count ?? 24);
            var combos = new List<string> { "" };
            const string wubrg = "WUBRG";
            for (int a = 0; a < 5; a++)
            {
                combos.Add(wubrg[a].ToString());
                for (int b = a + 1; b < 5; b++)
                {
                    combos.Add($"{wubrg[a]}{wubrg[b]}");
                    for (int c = b + 1; c < 5; c++) combos.Add($"{wubrg[a]}{wubrg[b]}{wubrg[c]}");
                }
            }
            var ideas = new List<BuildIdea>();
            foreach (var letters in combos)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Checking {(letters.Length == 0 ? "colorless" : letters)}…");
                var set = new HashSet<char>(letters);
                var fits = cards.Where(x => CandidatePool.FitsColors(x.Card, set)).ToList();
                var spellsList = fits.Where(x => !x.Card.IsLand).ToList();
                int Copies(FreeCard x) => Math.Min(x.Card.Restricted ? 1 : 4, x.Card.FreeCopies);
                int spells = spellsList.Sum(Copies);
                int playsets = spellsList.Count(x => x.Card.FreeCopies >= 4);
                int lands = fits.Count(x => x.Card.IsLand && x.Colors.Count >= 2 && x.Colors.IsSubsetOf(set));
                var (roleFill, shortOf) = RoleFill(guide, fits, copies: true);
                // The creature type most of these creatures share: a tribe to build around.
                var tribe = spellsList.Where(x => x.Card.IsCreature)
                    .SelectMany(x => CreatureTypes(x.Card.TypeLine).Select(t => (t, n: Copies(x))))
                    .GroupBy(x => x.t).Select(g => (Type: g.Key, N: g.Sum(x => x.n)))
                    .OrderByDescending(x => x.N).FirstOrDefault();
                string notes = $"{playsets} playset{(playsets == 1 ? "" : "s")}" +
                               (tribe.N >= 6 ? $" · most common type: {tribe.Type} ({tribe.N})" : "");
                double score = 0.5 * Math.Min(1.0, spells / (double)spellSlots) + 0.3 * roleFill + 0.2 * Math.Min(1.0, playsets / 9.0);
                // More colors make a shakier mana base: a little less for three.
                if (letters.Length == 3) score *= 0.95;
                ideas.Add(new BuildIdea
                {
                    ColorLetters = letters, Spells = spells, SpellSlots = spellSlots, Lands = lands,
                    RoleFill = roleFill, ShortOf = shortOf, Edhrec = notes, Score = score,
                });
            }
            return ideas.OrderByDescending(x => x.Score).ThenBy(x => x.ColorLetters.Length).ToList();
        }

        /// <summary>How far these free cards go toward the guide's roles (0–1), and the roles they fall short in.</summary>
        private static (double Fill, List<string> ShortOf) RoleFill(RoleGuide guide, List<FreeCard> fits, bool copies)
        {
            int need = 0, got = 0;
            var shortOf = new List<string>();
            foreach (var t in guide.Targets.Where(t => t.Role != Role.Land && t.Enabled && t.Count > 0))
            {
                int have = fits.Where(x => !x.Card.IsLand && x.Roles.Contains(t.Role))
                               .Sum(x => copies ? Math.Min(4, x.Card.FreeCopies) : 1);
                need += t.Count;
                got += Math.Min(have, t.Count);
                if (have < t.Count) shortOf.Add($"{RoleClassifier.Name(t.Role)} {have}/{t.Count}");
            }
            return (need == 0 ? 1 : got / (double)need, shortOf);
        }

        /// <summary>"Legendary Creature — Elf Druid" → Elf, Druid (the front face's subtypes).</summary>
        private static IEnumerable<string> CreatureTypes(string typeLine)
        {
            string front = typeLine.Split(" // ")[0];
            int dash = front.IndexOf('—');
            return dash < 0 ? Array.Empty<string>()
                : front[(dash + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // ══ From draft to deck ═══════════════════════════════════════════════

        /// <summary>
        /// The draft as a deck (not saved): each card in the printings you have
        /// free copies of (non-foil first, copies spread over them), the rest in
        /// the cheapest printing in the pool; the commander in the command zone.
        /// 60-card: a Constructed deck in <paramref name="constructedFormat"/>.
        /// </summary>
        public static Models.Deck ToDeck(DraftDeck draft, string name, string? constructedFormat = null)
        {
            var deck = DeckService.CreateNew(name, constructedFormat != null ? Models.DeckType.Standard : Models.DeckType.Commander);
            if (constructedFormat != null) deck.ConstructedFormat = constructedFormat;
            using var cdb = new CollectionDbContext();
            using var db = new AppDbContext();
            // The deck, then the swap options in the sideboard.
            foreach (var (p, swap) in draft.Picks.Select(x => (x, false)).Concat(draft.SwapOptions.Select(x => (x, true))))
            {
                string cardName = p.Card.Name;
                var section = swap ? Models.DeckCardCategory.Sideboard
                    : p.IsCommander ? Models.DeckCardCategory.Commander : Models.DeckCardCategory.Mainboard;
                int left = Math.Max(1, p.Copies);
                // Printings you have free copies of (basic lands too: yours before buying any) …
                {
                    var own = cdb.CollectionEntries.AsNoTracking()
                        .Where(x => x.Name == cardName && x.Quantity > x.UsedCount)
                        .Select(x => new { x.ScryfallId, x.Finish, Free = x.Quantity - x.UsedCount })
                        .ToList()
                        .OrderBy(x => x.Finish == CardFinish.NonFoil ? 0 : 1).ThenByDescending(x => x.Free);
                    foreach (var o in own)
                    {
                        if (left <= 0) break;
                        var printing = db.PoolCards.AsNoTracking().FirstOrDefault(x => x.ScryfallId == o.ScryfallId);
                        if (printing == null) continue;
                        int n = Math.Min(left, o.Free);
                        DeckEditService.Add(deck, DeckService.FromPoolCard(printing), CardFinish.Normalize(o.Finish), n, section);
                        left -= n;
                    }
                }
                if (left <= 0) continue;
                // … the rest: the cheapest printing (non-foil when it comes in it).
                var printings = db.PoolCards.AsNoTracking().Where(x => x.Name == cardName && !x.IsToken).ToList();
                var pool = printings.Where(x => x.IsNonFoil && x.PriceUsd > 0).OrderBy(x => x.PriceUsd).FirstOrDefault()
                           ?? printings.FirstOrDefault(x => x.IsNonFoil) ?? printings.FirstOrDefault();
                if (pool == null) continue;                          // not in the pool any more: left out (said so by the caller)
                string finish = pool.IsNonFoil ? CardFinish.NonFoil : pool.IsFoil ? CardFinish.Foil : CardFinish.Etched;
                DeckEditService.Add(deck, DeckService.FromPoolCard(pool), finish, left, section);
            }
            return deck;
        }

        /// <summary>
        /// Accept a draft: save it as a new deck (Commander, or Constructed in
        /// <paramref name="constructedFormat"/>), claim your free
        /// copies for it, and put everything it still misses on the Want List
        /// (for this deck). Returns the deck and what happened.
        /// </summary>
        public static (Models.Deck Deck, string Message, bool Warning) Accept(DraftDeck draft, string name, string? constructedFormat = null)
        {
            var deck = ToDeck(draft, name, constructedFormat);
            int swaps = deck.Cards.Where(c => c.Category == Models.DeckCardCategory.Sideboard).Sum(c => c.TotalQuantity);
            int lost = draft.Picks.Sum(p => p.Copies) + draft.SwapOptions.Sum(p => p.Copies) - deck.Cards.Sum(c => c.TotalQuantity);
            deck.FilePath = DeckService.NewDeckPath(name);
            DeckService.Save(deck);                                  // gives it its id (claims are keyed on it)

            var (claimed, wanted, problems) = ClaimAndWant(deck);
            if (lost > 0) problems.Add($"{lost} card(s) aren't in the card pool and were left out");
            DeckVersionService.SaveVersion(deck, "Built by the deck builder");

            int main = deck.Cards.Where(c => c.Category != Models.DeckCardCategory.Sideboard).Sum(c => c.TotalQuantity);
            string msg = $"Created \"{deck.Name}\": {main} cards, {claimed} claimed from your collection" +
                         (wanted > 0 ? $", {wanted} on the Want List for this deck" : "") +
                         (swaps > 0 ? $", {swaps} swap options in the sideboard (not claimed)" : "") + "." +
                         (problems.Count > 0 ? "  " + string.Join("; ", problems.Take(3)) + "." : "");
            return (deck, msg, problems.Count > 0);
        }

        /// <summary>
        /// Claim your free copies for a saved deck's lines (the sideboard's swap
        /// options aren't claimed), then put whatever the deck still needs on the
        /// Want List for it. Used by Accept and by restoring a deck version.
        /// </summary>
        public static (int Claimed, int Wanted, List<string> Problems) ClaimAndWant(Models.Deck deck)
        {
            int claimed = 0, wanted = 0;
            var problems = new List<string>();
            var finishes = new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched };
            foreach (var line in deck.Cards.Where(c => c.Category != Models.DeckCardCategory.Sideboard))
                foreach (var f in finishes)
                {
                    int n = line.CountOf(f);
                    if (n <= 0) continue;
                    var r = CollectionEditService.ClaimOwned(CollectionEditService.TableOf(line), deck, line.ScryfallId, f, n, line.Name);
                    if (r.Changed > 0) claimed += r.Changed;
                }

            // Still needed: the deck's demand without the sideboard, less what's claimed.
            foreach (var need in CollectionEditService.ClaimStatus(deck, withSideboard: false).Where(l => l.Table == CollectionEditService.CardsTable))
            {
                int missing = need.Needed;
                if (missing <= 0) continue;
                var pool = CollectionEditService.FindPoolCard(CollectionEditService.CardsTable, need.ScryfallId);
                if (pool == null) { problems.Add($"{need.Name}: not in the card pool"); continue; }
                var r = CollectionEditService.TopUpWantList(pool, need.Finish, missing, deck);
                if (r.Changed > 0) wanted += r.Changed; else if (r.Warning) problems.Add(r.Message);
            }
            return (claimed, wanted, problems);
        }

        private static Candidate With(Candidate x, double? inclusion, double? synergy) => new()
        {
            Name = x.Name, OracleId = x.OracleId, TypeLine = x.TypeLine, OracleText = x.OracleText, ManaCost = x.ManaCost,
            ManaValue = x.ManaValue, ColorIdentity = x.ColorIdentity, Keywords = x.Keywords, Tags = x.Tags, Legal = x.Legal,
            IsGameChanger = x.IsGameChanger, OwnedCopies = x.OwnedCopies, FreeCopies = x.FreeCopies, Price = x.Price,
            EdhrecInclusion = inclusion, EdhrecSynergy = synergy, Salt = x.Salt,
        };

        /// <summary>
        /// One candidate per card name: your collection's cards (free copies),
        /// plus — with <paramref name="includePool"/> — every Commander-legal
        /// card in the pool (to get). Tags, salt and Game Changer flags added.
        /// </summary>
        private static List<Candidate> Candidates(bool includePool, List<string> sources, CancellationToken ct = default,
                                                  string legalityKey = "commander")
        {
            var rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

            // Your collection (cards): owned and free per name.
            using (var cdb = new CollectionDbContext())
            {
                foreach (var e in cdb.CollectionEntries.AsNoTracking()
                             .Select(x => new { x.Name, x.OracleId, x.TypeLine, x.OracleText, x.ManaCost, x.ManaValue, x.ColorIdentity,
                                                x.Keywords, x.LegalitiesJson, x.Price, x.Quantity, x.UsedCount })
                             .ToList())
                {
                    if (string.IsNullOrWhiteSpace(e.Name)) continue;
                    var r = Get(rows, e.Name, e.OracleId, e.TypeLine, e.OracleText, e.ManaCost, e.ManaValue, e.ColorIdentity, e.Keywords, e.LegalitiesJson, legalityKey);
                    r.Owned += Math.Max(0, e.Quantity);
                    r.Free += Math.Max(0, e.Quantity - Math.Max(0, e.UsedCount));
                    if (e.Price is { } p && p > 0 && (r.Price == null || p < r.Price)) r.Price = p;
                }
            }
            ct.ThrowIfCancellationRequested();
            sources.Add($"Your collection ({rows.Count:N0} card names, {rows.Values.Count(x => x.Free > 0):N0} with free copies)");

            // The card pool: Game Changers always; every legal card when cards to get are allowed.
            using (var db = new AppDbContext())
            {
                var gameChangers = new HashSet<string>(db.PoolCards.AsNoTracking().Where(x => x.IsGameChanger).Select(x => x.Name).ToList(),
                                                       StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows.Values) r.GameChanger = gameChangers.Contains(r.Name);
                if (includePool)
                {
                    int before = rows.Count;
                    string legalOne = $"\"{legalityKey}\":\"legal\"", restrictedOne = $"\"{legalityKey}\":\"restricted\"";
                    foreach (var p in db.PoolCards.AsNoTracking()
                                 .Where(x => !x.IsToken && (x.LegalitiesJson.Contains(legalOne) || x.LegalitiesJson.Contains(restrictedOne)))
                                 .Select(x => new { x.Name, x.OracleId, x.TypeLine, x.OracleText, x.ManaCost, x.ManaValue, x.ColorIdentity,
                                                    x.Keywords, x.LegalitiesJson, x.PriceUsd, x.PriceUsdFoil, x.IsGameChanger })
                                 .ToList())
                    {
                        var r = Get(rows, p.Name, p.OracleId, p.TypeLine, p.OracleText, p.ManaCost, p.ManaValue, p.ColorIdentity, p.Keywords, p.LegalitiesJson, legalityKey);
                        var price = new[] { p.PriceUsd, p.PriceUsdFoil }.Where(x => x is > 0).Min();
                        if (price is { } pr && (r.Price == null || pr < r.Price)) r.Price = pr;
                        r.GameChanger |= p.IsGameChanger;
                    }
                    sources.Add($"Card pool ({rows.Count - before:N0} more cards to get)");
                }
            }

            ct.ThrowIfCancellationRequested();

            // Scryfall card tags, salt.
            var tags = DeckDataService.TagsForMany(rows.Values.Select(x => x.OracleId));
            sources.Add(tags.Count > 0 ? $"Card tags ({tags.Count:N0} cards tagged)" : "Card tags: not downloaded (roles read from rules text)");
            bool salt = EdhrecService.HasSalt;
            if (!salt) sources.Add("Salt scores: not downloaded");

            return rows.Values.Select(r => new Candidate
            {
                Name = r.Name, OracleId = r.OracleId, TypeLine = r.TypeLine, OracleText = r.OracleText, ManaCost = r.ManaCost,
                ManaValue = r.ManaValue, ColorIdentity = r.ColorIdentity,
                Keywords = (r.Keywords ?? "").Split('|', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Tags = tags.TryGetValue(r.OracleId, out var t) ? t : Array.Empty<string>(),
                Legal = r.Legal, Restricted = r.Restricted, IsGameChanger = r.GameChanger,
                OwnedCopies = r.Owned, FreeCopies = r.Free, Price = r.Price,
                Salt = salt ? EdhrecService.SaltOf(r.Name)?.Score : null,
            }).ToList();
        }

        /// <summary>A card from the pool by name (the commander, when you don't own it).</summary>
        private static Candidate? PoolCandidate(string name)
        {
            using var db = new AppDbContext();
            var p = db.PoolCards.AsNoTracking().FirstOrDefault(x => x.Name == name);
            return p == null ? null : new Candidate
            {
                Name = p.Name, OracleId = p.OracleId, TypeLine = p.TypeLine, OracleText = p.OracleText, ManaCost = p.ManaCost,
                ManaValue = p.ManaValue, ColorIdentity = p.ColorIdentity, Price = p.PriceUsd,
            };
        }

        private sealed class Row
        {
            public string Name = "", OracleId = "", TypeLine = "", OracleText = "", ManaCost = "", ColorIdentity = "", Keywords = "";
            public double ManaValue;
            public bool Legal = true, Restricted, GameChanger;
            public int Owned, Free;
            public decimal? Price;
        }

        private static Row Get(Dictionary<string, Row> rows, string name, string? oracleId, string? type, string? text, string? cost,
                               double mv, string? identity, string? keywords, string? legalities, string legalityKey)
        {
            if (rows.TryGetValue(name, out var r)) return r;
            r = new Row
            {
                Name = name, OracleId = oracleId ?? "", TypeLine = type ?? "", OracleText = text ?? "", ManaCost = cost ?? "",
                ManaValue = mv, ColorIdentity = identity ?? "", Keywords = keywords ?? "",
            };
            (r.Legal, r.Restricted) = Legality(legalities, legalityKey);
            rows[name] = r;
            return r;
        }

        /// <summary>Legal (or restricted) in the format, and restricted (one copy). Unknown: allowed (Format Check says otherwise).</summary>
        private static (bool Legal, bool Restricted) Legality(string? legalitiesJson, string key)
        {
            if (string.IsNullOrWhiteSpace(legalitiesJson)) return (true, false);
            try
            {
                using var doc = JsonDocument.Parse(legalitiesJson);
                if (!doc.RootElement.TryGetProperty(key, out var v)) return (true, false);
                string status = v.GetString() ?? "";
                return (status is "legal" or "restricted", status == "restricted");
            }
            catch { return (true, false); }
        }
    }
}
