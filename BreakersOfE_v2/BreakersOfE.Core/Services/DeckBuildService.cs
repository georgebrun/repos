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

    /// <summary>What can I build: one commander you own and how much of a deck your free cards make for it.</summary>
    public sealed class BuildIdea
    {
        public DeckBuildService.CommanderChoice Commander { get; init; } = new("", 0, 0);
        /// <summary>Free spells (not lands) in its colors, and the deck's spell slots (the guide's 99 minus lands).</summary>
        public int Spells { get; init; }
        public int SpellSlots { get; init; }
        /// <summary>Free nonbasic lands in its colors.</summary>
        public int Lands { get; init; }
        /// <summary>How far the free cards go toward the guide's roles (0–1).</summary>
        public double RoleFill { get; init; }
        /// <summary>The roles they fall short in ("Ramp 4/10").</summary>
        public List<string> ShortOf { get; init; } = new();
        /// <summary>"23 of its top 100" (EDHREC page saved), else "".</summary>
        public string Edhrec { get; init; } = "";
        public double Score { get; init; }

        public string Name => Commander.Name;
        public string Colors { get { var s = new string("WUBRG".Where(ch => Commander.ColorIdentity.ToUpperInvariant().Contains(ch)).ToArray()); return s.Length == 0 ? "C" : s; } }
        public string SpellsText => $"{Math.Min(Spells, SpellSlots)} of {SpellSlots}" + (Spells > SpellSlots ? $" (+{Spells - SpellSlots})" : "");
        public string RolesText => $"{RoleFill * 100:0}%";
        public string ShortText => ShortOf.Count == 0 ? "—" : string.Join(", ", ShortOf);
        public string EdhrecText => Edhrec.Length == 0 ? "" : Edhrec;
        public string ReadyText => $"{Score * 100:0}%";
        public string CommanderText => Commander.IsFree ? "Free" : "Yours, in use";
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
            var ctx = await PrepareAsync(commanderNames, constraints, ct, progress);
            progress?.Report("Picking the cards…");
            return Build(ctx, ctx.Constraints, ct);
        }

        /// <summary>
        /// Everything a build needs, gathered once: the cards it may use (with
        /// EDHREC, tags, salt), the commanders, the combos. Guided Build keeps
        /// it for all its steps; a plain build uses it once.
        /// </summary>
        public static async Task<BuildContext> PrepareAsync(IReadOnlyList<string> commanderNames, BuildConstraints constraints,
                                                            CancellationToken ct = default, IProgress<string>? progress = null)
        {
            var sources = new List<string>();
            progress?.Report(constraints.Sources == SourceMode.CollectionAndPool
                ? "Reading your collection and the card pool…" : "Reading your collection…");
            var candidates = await Task.Run(() => Candidates(constraints.Sources == SourceMode.CollectionAndPool, sources, ct), ct);
            ct.ThrowIfCancellationRequested();

            // The commander(s): their color identity limits the deck.
            var commanders = commanderNames
                .Select(n => candidates.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                             ?? PoolCandidate(n))
                .Where(x => x != null).Select(x => x!).ToList();
            if (commanders.Count == 0) throw new InvalidOperationException($"\"{string.Join(" + ", commanderNames)}\" isn't in the card pool.");
            string identity = new string("WUBRG".Where(ch => commanders.Any(x => x.ColorIdentity.Contains(ch))).ToArray());
            var c = new BuildConstraints
            {
                Commanders = commanders.Select(x => x.Name).ToList(), ColorIdentity = identity, DeckSize = constraints.DeckSize,
                Bracket = constraints.Bracket, Sources = constraints.Sources, SpendLimit = constraints.SpendLimit,
                DeckValueCap = constraints.DeckValueCap, Locked = constraints.Locked, Excluded = constraints.Excluded,
                UseGuide = constraints.UseGuide, Guide = constraints.Guide,
            };

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
            var combos = new List<ComboInfo>();
            if (DeckDataService.HasCombos)
            {
                var names = eligible.Select(e => e.Card.Name).Concat(c.Commanders).ToList();
                progress?.Report("Looking for combos…");
                combos = DeckDataService.CombosWithin(names)
                    .Select(k => new ComboInfo
                    {
                        Cards = k.Cards, Results = string.Join("; ", k.Results),
                        TwoCardInfinite = k.Cards.Count == 2 && DeckDataService.IsInfiniteOrWin(k),
                    }).ToList();
                sources.Add($"Commander Spellbook ({combos.Count:N0} combos among these cards)");
            }
            else sources.Add("Combos: not downloaded (run Update Database)");
            ct.ThrowIfCancellationRequested();

            var ctx = new BuildContext { Constraints = c, Commanders = commanders, Eligible = eligible, Left = left, Combos = combos };
            ctx.Sources.AddRange(sources);
            return ctx;
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

        // ══ What can I build? ═══════════════════════════════════════════════

        /// <summary>
        /// For each commander you own (a free copy; with <paramref name="includeInUse"/>
        /// also ones in decks): how much of a deck your free cards already make —
        /// spells in its colors, how far they go toward the guide's roles, and
        /// (when EDHREC's page for it is saved) how many of its most-played cards
        /// are free. Best first. Only your collection is read; nothing is downloaded.
        /// </summary>
        public static List<BuildIdea> WhatCanIBuild(bool includeInUse, CancellationToken ct = default, IProgress<string>? progress = null)
        {
            progress?.Report("Reading your collection…");
            var sources = new List<string>();
            var free = Candidates(false, sources, ct).Where(x => x.FreeCopies > 0 && x.Legal && !x.IsBasicLand).ToList();
            ct.ThrowIfCancellationRequested();
            progress?.Report("Sorting your cards by what they do…");
            var cards = free.Select(x => (card: x, roles: RoleClassifier.Classify(x).Roles,
                                          colors: new HashSet<char>(x.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)))))
                            .ToList();
            var guide = RoleGuide.CommanderDefault();
            int spellSlots = 99 - (guide.For(Role.Land)?.Count ?? 37);

            progress?.Report("Finding your commanders…");
            var commanders = CommanderChoices().Where(x => !x.IsBackground && (x.IsFree || includeInUse && x.Owned > 0)).ToList();
            var ideas = new List<BuildIdea>();
            int done = 0;
            foreach (var cmd in commanders)
            {
                ct.ThrowIfCancellationRequested();
                if (++done % 20 == 0) progress?.Report($"Checking commanders… {done:N0} of {commanders.Count:N0}");
                var identity = new HashSet<char>(cmd.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)));
                var fits = cards.Where(x => x.colors.IsSubsetOf(identity) && !x.card.Name.Equals(cmd.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                int spells = fits.Count(x => !x.card.IsLand);
                int lands = fits.Count(x => x.card.IsLand);

                // The guide's roles: how many of each target the free cards could fill.
                int need = 0, got = 0;
                var shortOf = new List<string>();
                foreach (var t in guide.Targets.Where(t => t.Role != Role.Land && t.Enabled && t.Count > 0))
                {
                    int have = fits.Count(x => !x.card.IsLand && x.roles.Contains(t.Role));
                    need += t.Count;
                    got += Math.Min(have, t.Count);
                    if (have < t.Count) shortOf.Add($"{RoleClassifier.Name(t.Role)} {have}/{t.Count}");
                }
                double roleFill = need == 0 ? 1 : got / (double)need;
                double spellFill = Math.Min(1.0, spells / (double)spellSlots);

                // EDHREC's page, only if it's saved already.
                string edhrec = "";
                double edhrecFill = -1;
                var page = EdhrecService.Saved(new[] { cmd.Name });
                if (page != null)
                {
                    var top = page.Cards.Where(x => x.InclusionPercent >= 0).OrderByDescending(x => x.InclusionPercent).Take(100)
                                        .Select(x => DeckDataService.FrontName(x.Name)).ToList();
                    var freeNames = new HashSet<string>(fits.Select(x => DeckDataService.FrontName(x.card.Name)), StringComparer.OrdinalIgnoreCase);
                    int have = top.Count(freeNames.Contains);
                    if (top.Count > 0)
                    {
                        edhrecFill = have / (double)top.Count;
                        edhrec = $"{have} of its top {top.Count}";
                    }
                }

                double score = 0.55 * spellFill + 0.45 * roleFill;
                if (edhrecFill >= 0) score = 0.7 * score + 0.3 * Math.Min(1.0, edhrecFill * 2);
                ideas.Add(new BuildIdea
                {
                    Commander = cmd, Spells = spells, Lands = lands, SpellSlots = spellSlots,
                    RoleFill = roleFill, ShortOf = shortOf, Edhrec = edhrec, Score = score,
                });
            }
            return ideas.OrderByDescending(x => x.Score).ThenBy(x => x.Commander.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ══ From draft to deck ═══════════════════════════════════════════════

        /// <summary>
        /// The draft as a deck (not saved): each card in a printing you have free
        /// copies of (non-foil first), or else the cheapest printing in the pool;
        /// the commander in the command zone.
        /// </summary>
        public static Models.Deck ToDeck(DraftDeck draft, string name)
        {
            var deck = DeckService.CreateNew(name, Models.DeckType.Commander);
            using var cdb = new CollectionDbContext();
            using var db = new AppDbContext();
            foreach (var p in draft.Picks)
            {
                string cardName = p.Card.Name;
                // A printing you have free copies of …
                var own = cdb.CollectionEntries.AsNoTracking()
                    .Where(x => x.Name == cardName && x.Quantity > x.UsedCount)
                    .Select(x => new { x.ScryfallId, x.Finish, Free = x.Quantity - x.UsedCount })
                    .ToList()
                    .OrderBy(x => x.Finish == CardFinish.NonFoil ? 0 : 1).ThenByDescending(x => x.Free)
                    .FirstOrDefault();
                Models.PoolCard? pool = null;
                string finish = CardFinish.NonFoil;
                if (own != null)
                {
                    pool = db.PoolCards.AsNoTracking().FirstOrDefault(x => x.ScryfallId == own.ScryfallId);
                    finish = CardFinish.Normalize(own.Finish);
                }
                // … else the cheapest printing (non-foil when it comes in it).
                if (pool == null)
                {
                    var printings = db.PoolCards.AsNoTracking().Where(x => x.Name == cardName && !x.IsToken).ToList();
                    pool = printings.Where(x => x.IsNonFoil && x.PriceUsd > 0).OrderBy(x => x.PriceUsd).FirstOrDefault()
                           ?? printings.FirstOrDefault(x => x.IsNonFoil) ?? printings.FirstOrDefault();
                    if (pool != null) finish = pool.IsNonFoil ? CardFinish.NonFoil : pool.IsFoil ? CardFinish.Foil : CardFinish.Etched;
                }
                if (pool == null) continue;                          // not in the pool any more: left out (said so by the caller)
                var section = p.IsCommander ? Models.DeckCardCategory.Commander : Models.DeckCardCategory.Mainboard;
                DeckEditService.Add(deck, DeckService.FromPoolCard(pool), finish, Math.Max(1, p.Copies), section);
            }
            return deck;
        }

        /// <summary>
        /// Accept a draft: save it as a new Commander deck, claim your free
        /// copies for it, and put everything it still misses on the Want List
        /// (for this deck). Returns the deck and what happened.
        /// </summary>
        public static (Models.Deck Deck, string Message, bool Warning) Accept(DraftDeck draft, string name)
        {
            var deck = ToDeck(draft, name);
            int lost = draft.Picks.Sum(p => p.Copies) - deck.Cards.Sum(c => c.TotalQuantity);
            deck.FilePath = DeckService.NewDeckPath(name);
            DeckService.Save(deck);                                  // gives it its id (claims are keyed on it)

            int claimed = 0;
            var problems = new List<string>();
            foreach (var line in deck.Cards)
                foreach (var f in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
                {
                    int n = line.CountOf(f);
                    if (n <= 0) continue;
                    var r = CollectionEditService.ClaimOwned(CollectionEditService.CardsTable, deck, line.ScryfallId, f, n, line.Name);
                    if (r.Changed > 0) claimed += r.Changed;
                }

            int wanted = 0;
            foreach (var need in CollectionEditService.ClaimStatus(deck).Where(l => l.Needed > 0 && l.Table == CollectionEditService.CardsTable))
            {
                var pool = CollectionEditService.FindPoolCard(CollectionEditService.CardsTable, need.ScryfallId);
                if (pool == null) { problems.Add($"{need.Name}: not in the card pool"); continue; }
                var r = CollectionEditService.TopUpWantList(pool, need.Finish, need.Needed, deck);
                if (r.Changed > 0) wanted += r.Changed; else if (r.Warning) problems.Add(r.Message);
            }
            if (lost > 0) problems.Add($"{lost} card(s) aren't in the card pool and were left out");

            string msg = $"Created \"{deck.Name}\": {deck.Cards.Sum(c => c.TotalQuantity)} cards, {claimed} claimed from your collection" +
                         (wanted > 0 ? $", {wanted} on the Want List for this deck" : "") + "." +
                         (problems.Count > 0 ? "  " + string.Join("; ", problems.Take(3)) + "." : "");
            return (deck, msg, problems.Count > 0);
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
        private static List<Candidate> Candidates(bool includePool, List<string> sources, CancellationToken ct = default)
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
                    var r = Get(rows, e.Name, e.OracleId, e.TypeLine, e.OracleText, e.ManaCost, e.ManaValue, e.ColorIdentity, e.Keywords, e.LegalitiesJson);
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
                    foreach (var p in db.PoolCards.AsNoTracking()
                                 .Where(x => !x.IsToken && x.LegalitiesJson.Contains("\"commander\":\"legal\""))
                                 .Select(x => new { x.Name, x.OracleId, x.TypeLine, x.OracleText, x.ManaCost, x.ManaValue, x.ColorIdentity,
                                                    x.Keywords, x.LegalitiesJson, x.PriceUsd, x.PriceUsdFoil, x.IsGameChanger })
                                 .ToList())
                    {
                        var r = Get(rows, p.Name, p.OracleId, p.TypeLine, p.OracleText, p.ManaCost, p.ManaValue, p.ColorIdentity, p.Keywords, p.LegalitiesJson);
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
                Legal = r.CommanderLegal, IsGameChanger = r.GameChanger,
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
            public bool CommanderLegal = true, GameChanger;
            public int Owned, Free;
            public decimal? Price;
        }

        private static Row Get(Dictionary<string, Row> rows, string name, string? oracleId, string? type, string? text, string? cost,
                               double mv, string? identity, string? keywords, string? legalities)
        {
            if (rows.TryGetValue(name, out var r)) return r;
            r = new Row
            {
                Name = name, OracleId = oracleId ?? "", TypeLine = type ?? "", OracleText = text ?? "", ManaCost = cost ?? "",
                ManaValue = mv, ColorIdentity = identity ?? "", Keywords = keywords ?? "",
                CommanderLegal = CommanderLegal(legalities),
            };
            rows[name] = r;
            return r;
        }

        private static bool CommanderLegal(string? legalitiesJson)
        {
            if (string.IsNullOrWhiteSpace(legalitiesJson)) return true;          // unknown: allowed (Format Check says otherwise)
            try
            {
                using var doc = JsonDocument.Parse(legalitiesJson);
                return !doc.RootElement.TryGetProperty("commander", out var v) || v.GetString() is "legal" or "restricted";
            }
            catch { return true; }
        }
    }
}
