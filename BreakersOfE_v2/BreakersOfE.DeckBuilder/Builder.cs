using System.Text.RegularExpressions;
using System.Threading;

namespace BreakersOfE.DeckBuilder
{
    /// <summary>A combo the builder knows about (Commander Spellbook), by card names.</summary>
    public sealed class ComboInfo
    {
        public IReadOnlyList<string> Cards { get; init; } = Array.Empty<string>();
        /// <summary>What it does ("Infinite mana", "Win the game").</summary>
        public string Results { get; init; } = "";
        /// <summary>Two cards, infinite or game-winning (the Brackets' "two-card combo").</summary>
        public bool TwoCardInfinite { get; init; }
    }

    /// <summary>One card in the draft, and why.</summary>
    public sealed class Pick
    {
        public Candidate Card { get; init; } = new();
        public int Copies { get; init; } = 1;
        public HashSet<Role> Roles { get; init; } = new();
        /// <summary>Why it's in ("Ramp · free in your collection · EDHREC: 62% of decks").</summary>
        public string Reason { get; init; } = "";
        public bool Locked { get; init; }
        /// <summary>The deck's commander (first in the list).</summary>
        public bool IsCommander { get; init; }
        /// <summary>From your free copies, or to get (Want List).</summary>
        public CardSource Source { get; init; }
        /// <summary>What getting one copy would cost (0 when it's yours).</summary>
        public decimal Cost { get; init; }
        /// <summary>Copies to get (the rest are your free copies).</summary>
        public int ToGet { get; init; }
        public double Score { get; init; }
        public override string ToString() => $"{(Copies > 1 ? Copies + "× " : "")}{Card.Name} — {Reason}";
    }

    /// <summary>How the draft did against one guide target.</summary>
    public sealed class RoleResult
    {
        public Role Role { get; init; }
        public int Target { get; init; }
        /// <summary>Cards in the deck that do this (a card with several roles counts in each).</summary>
        public int Got { get; init; }
        public bool Enabled { get; init; }
        /// <summary>More than the guide asked for: cards picked for other reasons that do this too.</summary>
        public int Extra => Enabled ? Math.Max(0, Got - Target) : 0;
        /// <summary>"Ramp: 10 of 10 (+9 more do it too)", "Removal: 6 of 8", "Tutor: 2 (guide off)".</summary>
        public string Text => !Enabled ? $"{RoleClassifier.Name(Role)}: {Got} (guide off)"
            : $"{RoleClassifier.Name(Role)}: {Math.Min(Got, Target)} of {Target}" +
              (Extra > 0 ? $" (+{Extra} more {(Extra == 1 ? "card does" : "cards do")} it too)" : "");
    }

    /// <summary>The draft deck: picks with reasons, how the guide went, the cost, and anything to know.</summary>
    public sealed class DraftDeck
    {
        public List<Pick> Picks { get; } = new();
        public List<RoleResult> Roles { get; } = new();
        public List<string> Notes { get; } = new();
        public List<ComboInfo> Combos { get; } = new();
        /// <summary>
        /// Swap options: the best cards that just missed the deck, a few per
        /// guide role and for the plan, each with its reason. Not part of the
        /// deck (no card count, value or claims); Accept puts them in the sideboard.
        /// </summary>
        public List<Pick> SwapOptions { get; } = new();
        /// <summary>Cards in the deck (commanders included).</summary>
        public int CardCount { get; set; }
        /// <summary>To spend on cards you don't own.</summary>
        public decimal ToSpend => Picks.Sum(p => p.Cost * p.ToGet);
        /// <summary>The deck's market value (all cards with a price).</summary>
        public decimal Value => Picks.Sum(p => (p.Card.Price ?? 0) * p.Copies);
        public int GameChangers => Picks.Count(p => p.Card.IsGameChanger);
        /// <summary>Copies to get, all cards.</summary>
        public int CardsToGet => Picks.Sum(p => p.ToGet);
    }

    /// <summary>
    /// Builds a draft Commander deck from the eligible cards (CandidatePool):
    /// locked cards, then the guide's roles one by one (best fit first), then
    /// the best of the rest for the deck's plan, then lands — every pick with
    /// a reason. Stays inside the bracket (Game Changers, two-card combos, land
    /// denial, extra turns), the spend limit and the deck value cap. A role
    /// with too few good cards is reported, never padded with poor ones.
    /// </summary>
    public static class Builder
    {
        /// <summary>Roles are filled in this order (the guide's priorities).</summary>
        public static readonly IReadOnlyList<Role> Order = new[]
        {
            Role.Ramp, Role.Draw, Role.Removal, Role.BoardWipe, Role.Protection,
            Role.Counterspell, Role.Tutor, Role.Recursion, Role.Finisher,
        };

        private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>Text that looks like mass land denial (Bracket 4+).</summary>
        public static readonly Regex LandDenial = Rx(@"\b(destroy|exile) all\b[^.]*\blands\b|\beach (player|opponent) sacrifices\b[^.]*\blands?\b|\blands (your opponents control )?(don't|do not) untap\b|\bcan't untap more than\b|\breturn all lands\b");
        /// <summary>Text that gives extra turns.</summary>
        public static readonly Regex ExtraTurn = Rx(@"\bextra turns?\b");
        private static readonly Regex Symbol = Rx(@"\{([^}]+)\}");

        /// <summary>An extra-turn card (not one that stops them).</summary>
        public static bool GivesExtraTurns(Candidate card) =>
            ExtraTurn.IsMatch(card.OracleText) && !card.OracleText.Contains("can't take extra turns", StringComparison.OrdinalIgnoreCase);

        /// <summary>Build the draft. <paramref name="commanders"/>: the commander card(s), in the order of c.Commanders.</summary>
        public static DraftDeck Build(IReadOnlyList<Eligible> eligible, BuildConstraints c,
                                      IReadOnlyList<Candidate>? commanders = null, IReadOnlyList<ComboInfo>? combos = null,
                                      CancellationToken ct = default)
        {
            var s = new Session(eligible, c, commanders, combos);
            s.AddCommanders();
            s.AddLocked();
            var deck = s.Deck;

            // 2. The guide's roles, best fit first.
            if (c.UseGuide)
            {
                foreach (var role in Order)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = c.Guide.For(role);
                    if (target is not { Enabled: true } || target.Count <= 0) continue;
                    foreach (var (e, score) in s.Ranked(role))
                    {
                        if (s.RoleCount(role) >= target.Count || s.NonLandPicked() >= s.NonLandSlots) break;
                        if (!s.TryPlan(e, target.Count - s.RoleCount(role), Session.RoleSlack, out int copies, out int toGet)) continue;
                        s.Add(e, s.Why(e, s.RoleLead(e, role, target.Count), copies, toGet), score: score, copies: copies, toGet: toGet);
                    }
                    if (s.RoleCount(role) < target.Count)
                        deck.Notes.Add($"{RoleClassifier.Name(role)}: only {s.RoleCount(role)} of {target.Count} — not enough {(c.Sources == SourceMode.CollectionOnly ? "free cards in your collection" : "cards within the spend limit")} for this role.");
                }
            }

            // 3. The rest of the spells: the best cards for the deck's plan
            //    (60-card: inside the curve first, then anything that's left).
            ct.ThrowIfCancellationRequested();
            foreach (int slack in c.IsConstructed && c.Curve != null ? new[] { 0, int.MaxValue } : new[] { int.MaxValue })
                foreach (var (e, score) in s.Ranked(null))
                {
                    if (s.NonLandPicked() >= s.NonLandSlots) break;
                    if (!s.TryPlan(e, null, slack, out int copies, out int toGet)) continue;
                    s.Add(e, s.Why(e, Session.PlanLead(e), copies, toGet), score: score, copies: copies, toGet: toGet);
                }
            if (s.NonLandPicked() < s.NonLandSlots)
                deck.Notes.Add($"Only {s.NonLandPicked()} spells found for {s.NonLandSlots} spell slots; the rest are lands.");

            // 4. Lands.
            ct.ThrowIfCancellationRequested();
            s.AddLands();
            s.Finish();
            // 5. Swap options: the runner-ups.
            ct.ThrowIfCancellationRequested();
            s.AddSwapOptions(c.SwapOptions);
            return deck;
        }

        /// <summary>
        /// Guided Build: the best options for one step — a guide role, or the
        /// deck's plan (<paramref name="role"/> null) — with what's already
        /// chosen (c.Locked) in the deck. Each comes with its reason, score and
        /// (60-card) copies; cards the bracket, spend limit or value cap rule out are left out.
        /// </summary>
        public static List<Pick> Options(IReadOnlyList<Eligible> eligible, BuildConstraints c, Role? role, int max,
                                         IReadOnlyList<Candidate>? commanders = null, IReadOnlyList<ComboInfo>? combos = null)
        {
            var s = new Session(eligible, c, commanders, combos);
            s.AddCommanders();
            s.AddLocked();
            var list = new List<Pick>();
            int target = role is { } r && c.Guide.For(r) is { } t ? t.Count : 0;
            int already = 0;
            foreach (var (e, score) in s.Ranked(role))
            {
                if (list.Count >= max) break;
                if (!s.TryPlan(e, null, int.MaxValue, out int copies, out int toGet, ignoreSlots: true)) continue;
                string lead = role is { } rr ? s.RoleLead(e, rr, target, already) : Session.PlanLead(e);
                list.Add(s.PickOf(e, s.Why(e, lead, copies, toGet), score, copies: copies, toGet: toGet));
                already += copies;
            }
            return list;
        }

        /// <summary>
        /// The state of one build: what's picked, the bracket's counters, money
        /// spent, the deck's themes — and how a card scores, how many copies
        /// (60-card) and why.
        /// </summary>
        private sealed class Session
        {
            /// <summary>60-card role steps may go this many cards over a mana value's curve target.</summary>
            public const int RoleSlack = 2;

            public readonly DraftDeck Deck = new();
            private readonly IReadOnlyList<Eligible> _eligible;
            private readonly BuildConstraints _c;
            private readonly IReadOnlyList<Candidate> _commanders;
            private readonly IReadOnlyList<ComboInfo> _combos;
            private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _themes;
            /// <summary>Your plan words: always count as specific themes, even common ones like "token".</summary>
            private readonly HashSet<string> _planWords = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, bool> _comboPieces = new(StringComparer.OrdinalIgnoreCase);   // name → in a two-card infinite
            private readonly Dictionary<string, Eligible> _byName;
            /// <summary>Combos by card name: each card only checks its own combos (partners like Thrasios have thousands).</summary>
            private readonly Dictionary<string, List<ComboInfo>> _combosOf = new(StringComparer.OrdinalIgnoreCase);
            private readonly bool _power;
            private readonly bool _constructed;
            private int _gameChangers, _extraTurns;
            private decimal _spent, _value;
            /// <summary>60-card: spells (copies) picked at mana value 1 … 6+.</summary>
            private readonly int[] _curve = new int[6];
            public readonly int Slots;
            public readonly int NonLandSlots;

            public Session(IReadOnlyList<Eligible> eligible, BuildConstraints c, IReadOnlyList<Candidate>? commanders, IReadOnlyList<ComboInfo>? combos)
            {
                _eligible = eligible;
                _c = c;
                _constructed = c.IsConstructed;
                _commanders = commanders ?? Array.Empty<Candidate>();
                _combos = combos ?? Array.Empty<ComboInfo>();
                _byName = new Dictionary<string, Eligible>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in eligible) _byName.TryAdd(e.Card.Name, e);
                // Themes: the commanders' — and the key (locked) cards' in a 60-card deck — plus your plan words.
                var sources = _commanders.ToList();
                if (_constructed)
                    sources.AddRange(c.Locked.Select(n => _byName.GetValueOrDefault(n)?.Card).Where(x => x != null).Select(x => x!));
                _themes = Themes(sources);
                foreach (var w in c.PlanWords.Select(w => w.Trim()).Where(w => w.Length > 1))
                {
                    _planWords.Add(w);
                    if (!_themes.Contains(w, StringComparer.OrdinalIgnoreCase)) _themes.Add(w);
                }
                Slots = _constructed ? Math.Max(0, c.DeckSize) : Math.Max(0, c.DeckSize - Math.Max(1, c.Commanders.Count));
                int landTarget = c.UseGuide && c.Guide.For(Role.Land) is { Enabled: true } lt ? lt.Count : _constructed ? 24 : 37;
                NonLandSlots = Slots - landTarget;

                // Higher brackets play to win: Game Changers, combos, cheap and efficient cards count for more.
                _power = c.Bracket >= 4;
                // Cards that are part of a combo the deck could assemble (every other piece is available too).
                var eligibleNames = new HashSet<string>(eligible.Select(x => x.Card.Name).Concat(c.Commanders), StringComparer.OrdinalIgnoreCase);
                foreach (var k in _combos)
                    foreach (var n in k.Cards.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!_combosOf.TryGetValue(n, out var list)) _combosOf[n] = list = new();
                        list.Add(k);
                    }
                foreach (var k in _combos)
                {
                    if (!k.Cards.All(eligibleNames.Contains)) continue;
                    if (k.TwoCardInfinite && !c.AllowTwoCardCombos) continue;
                    foreach (var n in k.Cards) _comboPieces[n] = _comboPieces.GetValueOrDefault(n) || k.TwoCardInfinite;
                }
            }

            private bool IsGeneric(string theme) => Generic.Contains(theme) && !_planWords.Contains(theme);

            private bool Completes(string name, out ComboInfo? combo)
            {
                combo = _combosOf.TryGetValue(name, out var list)
                    ? list.FirstOrDefault(k => k.Cards.Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).All(_picked.Contains))
                    : null;
                return combo != null;
            }

            private bool BreaksBracket(Eligible e)
            {
                if (e.Card.IsGameChanger && _gameChangers >= _c.GameChangerLimit) return true;
                if (!_c.AllowLandDenialAndExtraTurns)
                {
                    if (LandDenial.IsMatch(e.Card.OracleText)) return true;
                    if (GivesExtraTurns(e.Card) && (_c.Bracket <= 2 || _extraTurns >= 1)) return true;
                }
                if (!_c.AllowTwoCardCombos &&
                    _combosOf.TryGetValue(e.Card.Name, out var mine) &&
                    mine.Any(k => k.TwoCardInfinite && k.Cards.Where(n => !n.Equals(e.Card.Name, StringComparison.OrdinalIgnoreCase)).All(_picked.Contains)))
                    return true;
                return false;
            }

            private static decimal CostOf(Eligible e) => e.Card.FreeCopies > 0 || e.Card.IsBasicLand ? 0m : e.Card.Price ?? 0m;

            private bool Affordable(Eligible e)
            {
                decimal cost = CostOf(e);
                if (cost > 0 && _spent + cost > _c.SpendLimit) return false;
                if (_c.DeckValueCap is { } cap && _value + (e.Card.Price ?? 0) > cap) return false;
                return true;
            }

            private static int Bucket(Candidate card) => Math.Clamp((int)Math.Round(card.ManaValue), 1, 6) - 1;

            /// <summary>60-card: how many copies a card should have (cheap cards 4, mid 3, expensive 2; competitive one more).</summary>
            private int Wanted(Eligible e)
            {
                var card = e.Card;
                int n = card.IsLand ? 4 : card.ManaValue <= 2 ? 4 : card.ManaValue <= 4 ? 3 : 2;
                if (e.Roles.Contains(Role.BoardWipe)) n = Math.Min(n, 2);
                if (_power) n++;
                return Math.Min(n, MaxCopies(card));
            }

            private int MaxCopies(Candidate card) =>
                card.Restricted ? 1 : card.IsAnyNumber ? Math.Max(4, _c.CopyLimit) : _c.CopyLimit;

            /// <summary>
            /// Can it go in, and how: Commander — one copy, inside the bracket and
            /// the money. 60-card — as many copies as it should have (fewer when
            /// <paramref name="roleLeft"/> is small, the spell slots or the curve
            /// at its mana value are nearly full), your free copies first, the
            /// rest to get when the sources and money allow.
            /// </summary>
            public bool TryPlan(Eligible e, int? roleLeft, int curveSlack, out int copies, out int toGet, bool ignoreSlots = false)
            {
                copies = 0; toGet = 0;
                if (_picked.Contains(e.Card.Name) || BreaksBracket(e)) return false;
                if (!_constructed)
                {
                    if (!Affordable(e)) return false;
                    copies = 1;
                    toGet = CostOf(e) > 0 ? 1 : 0;
                    return true;
                }
                var card = e.Card;
                int want = Wanted(e);
                if (roleLeft is { } left) want = Math.Min(want, Math.Max(2, left));
                if (!ignoreSlots && !card.IsLand) want = Math.Min(want, NonLandSlots - NonLandPicked());
                if (!card.IsLand && _c.Curve is { } curve && curveSlack != int.MaxValue)
                {
                    int b = Bucket(card);
                    want = Math.Min(want, curve[b] + curveSlack - _curve[b]);
                }
                return Copies(e, want, mustHave: false, out copies, out toGet);
            }

            /// <summary>Up to <paramref name="want"/> copies: free ones first, then copies to get within the money.</summary>
            private bool Copies(Eligible e, int want, bool mustHave, out int copies, out int toGet)
            {
                copies = 0; toGet = 0;
                if (want <= 0) return false;
                var card = e.Card;
                decimal price = card.Price ?? 0m;
                int own = card.IsBasicLand ? want : Math.Min(Math.Max(0, card.FreeCopies), want);
                int buy = 0;
                if (own < want && (mustHave || _c.Sources == SourceMode.CollectionAndPool) && (card.Price.HasValue || mustHave))
                    while (own + buy < want && (mustHave || _spent + price * (buy + 1) <= _c.SpendLimit)) buy++;
                if (_c.DeckValueCap is { } cap && !mustHave)
                    while (own + buy > 0 && _value + price * (own + buy) > cap) { if (buy > 0) buy--; else own--; }
                copies = own + buy;
                toGet = buy;
                return copies > 0;
            }

            /// <summary>Not picked yet, and the bracket, spend limit and value cap allow it.</summary>
            public bool CanAdd(Eligible e) => TryPlan(e, null, int.MaxValue, out _, out _);

            public double Score(Eligible e, Role? forRole)
            {
                var card = e.Card;
                double s = 0;
                if (card.IsGameChanger && !_constructed) s += _power ? 20 : _c.Bracket == 3 ? 8 : 0;   // (the bracket's limit still applies)
                if (_power && _comboPieces.TryGetValue(card.Name, out bool twoCard)) s += twoCard ? 14 : 6;
                // EDHREC: what this commander's decks play.
                if (card.EdhrecSynergy is { } syn) s += syn * 60;
                if (card.EdhrecInclusion is { } inc) s += inc * 0.4;
                // The commander's themes in the card (counters, multicolored, its creature types …):
                // specific themes count a lot, common words ("spell", "token") a little.
                double theme = ThemeHits(card, _themes).Sum(t => IsGeneric(t) ? 1.5 : 10);
                s += Math.Min(25, theme) * (forRole == null ? 1.0 : 0.5);      // filling a role: the job first
                // Combos with what's already in.
                if (Completes(card.Name, out _)) s += _power ? 30 : 15;
                // Several roles in one card.
                s += Math.Max(0, e.Roles.Count(r => r != Role.Land) - 1) * 4;
                // Cheap is good; very expensive spells less so.
                if (!card.IsLand) s -= _power ? Math.Max(0, card.ManaValue - 3) * 4 : Math.Max(0, card.ManaValue - 4) * 3;
                if (forRole == Role.Ramp) s -= Math.Max(0, card.ManaValue - 2) * 4;     // ramp should be early
                // Yours already: preferred. To buy: a little worse for each dollar.
                if (card.FreeCopies > 0) s += 8;
                else s -= _power ? (double)Math.Min(10m, card.Price ?? 0m) : (double)Math.Min(20m, (card.Price ?? 0m) * 2m);
                // 60-card: a full playset of your own counts for a little more.
                if (_constructed && card.FreeCopies >= Math.Min(4, MaxCopies(card))) s += 3;
                // Low brackets: go easy on the salt.
                if (!_constructed && _c.Bracket <= 2 && card.Salt is { } salt) s -= salt * 5;
                return s;
            }

            /// <summary>Spells (not lands) not picked yet for this role (or the plan), best first.</summary>
            public List<(Eligible e, double s)> Ranked(Role? role) =>
                _eligible.Where(e => !e.Card.IsLand && (role == null || e.Roles.Contains(role.Value)) && !_picked.Contains(e.Card.Name))
                         .Select(e => (e, s: Score(e, role))).OrderByDescending(x => x.s).ToList();

            public Pick PickOf(Eligible e, string reason, double score, bool locked = false, int copies = 1, int toGet = -1)
            {
                if (toGet < 0) toGet = CostOf(e) > 0 ? copies : 0;
                return new Pick
                {
                    Card = e.Card, Roles = e.Roles, Reason = reason, Locked = locked, Score = score, Copies = copies,
                    Source = toGet > 0 ? CardSource.Pool : CardSource.Collection,
                    Cost = toGet > 0 ? e.Card.Price ?? 0m : 0m, ToGet = toGet,
                };
            }

            public void Add(Eligible e, string reason, bool locked = false, double score = 0, int copies = 1, int toGet = -1)
            {
                var pick = PickOf(e, reason, score, locked, copies, toGet);
                _spent += pick.Cost * pick.ToGet;
                _value += (e.Card.Price ?? 0) * pick.Copies;
                if (e.Card.IsGameChanger) _gameChangers++;
                if (ExtraTurn.IsMatch(e.Card.OracleText)) _extraTurns++;
                if (_constructed && !e.Card.IsLand) _curve[Bucket(e.Card)] += pick.Copies;
                _picked.Add(e.Card.Name);
                Deck.Picks.Add(pick);
            }

            /// <summary>"Removal (6 of 8)"; past the guide's target "Removal (over the guide)".</summary>
            public string RoleLead(Eligible e, Role role, int target, int already = -1)
            {
                int n = (already >= 0 ? RoleCount(role) + already : RoleCount(role)) + 1;
                return $"{RoleClassifier.Name(role)} ({(n > target ? "over the guide" : $"{n} of {target}")})" +
                       (e.RolesFromTags ? "" : " — read from its text");
            }

            public static string PlanLead(Eligible e) =>
                e.Roles.Count > 0 ? string.Join(", ", e.Roles.Select(RoleClassifier.Name)) : "For the deck's plan";

            public string Why(Eligible e, string lead, int copies = 1, int toGet = -1)
            {
                var parts = new List<string> { lead };
                var card = e.Card;
                if (_constructed && !card.IsBasicLand && toGet >= 0)
                {
                    int own = copies - toGet;
                    string each = card.Price is { } p ? $", ${p:0.00} each" : "";
                    parts.Add(toGet == 0 ? $"{copies} free in your collection" + (card.FreeCopies > copies ? $" (of {card.FreeCopies})" : "")
                              : own > 0 ? $"{own} yours + {toGet} to get{each}"
                              : $"{toGet} to get{each}");
                    if (card.Restricted) parts.Add("restricted: 1 copy");
                }
                else
                    parts.Add(card.IsBasicLand ? "basic land"
                        : card.FreeCopies > 0 ? $"free in your collection ({card.FreeCopies})"
                        : card.Price is { } p ? $"to get, ${p:0.00}" : "to get");
                if (card.EdhrecInclusion is { } inc && inc >= 1)
                    parts.Add($"EDHREC: {inc:0}% of decks" + (card.EdhrecSynergy is { } syn && Math.Abs(syn) >= 0.05 ? $", synergy {syn * 100:+0;-0}%" : ""));
                var hits = ThemeHits(card, _themes).Where(t => !IsGeneric(t)).Take(2).ToList();
                if (hits.Count > 0) parts.Add((_constructed ? "fits the plan (" : "fits the commander (") + string.Join(", ", hits) + ")");
                if (Completes(card.Name, out var combo))
                    parts.Add($"combo with {string.Join(" + ", combo!.Cards.Where(n => !n.Equals(card.Name, StringComparison.OrdinalIgnoreCase)))}");
                if (card.IsGameChanger && !_constructed) parts.Add("Game Changer");
                return string.Join(" · ", parts);
            }

            public int RoleCount(Role r) => Deck.Picks.Where(p => !p.IsCommander && p.Roles.Contains(r)).Sum(p => p.Copies);
            public int NonLandPicked() => Deck.Picks.Where(p => !p.IsCommander && !p.Card.IsLand).Sum(p => p.Copies);
            private int Ninety() => Deck.Picks.Where(p => !p.IsCommander).Sum(p => p.Copies);

            /// <summary>0. The commander(s) head the list (they're part of the deck, not of its 99).</summary>
            public void AddCommanders()
            {
                if (_commanders.Count == 0 || _constructed) return;
                foreach (var name in _c.Commanders)
                {
                    var card = _commanders.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                               ?? _byName.GetValueOrDefault(name)?.Card ?? new Candidate { Name = name };
                    var (roles, _) = RoleClassifier.Classify(card);
                    _value += card.Price ?? 0;
                    // Only a FREE copy is yours to use: one in another deck has to be got (Want List).
                    decimal cost = card.FreeCopies > 0 ? 0 : card.Price ?? 0;
                    _spent += cost;                                  // the commander comes out of the spend limit first
                    if (card.FreeCopies <= 0 && cost > _c.SpendLimit)
                        Deck.Notes.Add($"{card.Name} costs ${cost:0.00} to get — more than the spend limit (${_c.SpendLimit:0.00}).");
                    if (card.IsGameChanger) _gameChangers++;
                    Deck.Picks.Add(new Pick
                    {
                        Card = card, Roles = roles, Locked = true, IsCommander = true,
                        Reason = "Commander" + (card.FreeCopies > 0 ? $" · free in your collection ({card.FreeCopies})"
                                 : (card.OwnedCopies > 0 ? " · your copies are in use · to get" : " · to get") +
                                   (card.Price is { } p ? $", ${p:0.00}" : "")),
                        Source = card.FreeCopies > 0 ? CardSource.Collection : CardSource.Pool,
                        Cost = cost, ToGet = cost > 0 ? 1 : 0,
                    });
                    _picked.Add(card.Name);
                }
            }

            /// <summary>1. Locked cards (key cards, and the ones chosen in Guided Build), as they are.</summary>
            public void AddLocked()
            {
                foreach (var name in _c.Locked)
                {
                    if (!_byName.TryGetValue(name, out var e)) { Deck.Notes.Add($"Locked card \"{name}\" isn't available (not legal, {(_constructed ? "not in the deck's colors" : "off-color")}, or excluded)."); continue; }
                    if (_picked.Contains(e.Card.Name)) continue;
                    if (!_constructed) { Add(e, Why(e, "Locked by you"), locked: true); continue; }
                    // 60-card: the copies asked for (a key card: as many as allowed), yours first, the rest to get.
                    int want = _c.LockedCopies != null && _c.LockedCopies.TryGetValue(e.Card.Name, out int n) ? n : MaxCopies(e.Card);
                    want = Math.Clamp(want, 1, MaxCopies(e.Card));
                    Copies(e, want, mustHave: true, out int copies, out int toGet);
                    Add(e, Why(e, "Locked by you", copies, toGet), locked: true, copies: copies, toGet: toGet);
                }
            }

            /// <summary>4. Nonbasic lands that make the deck's colors (or do something), then basics.</summary>
            public void AddLands()
            {
                var identity = _c.ColorIdentity.ToUpperInvariant().Where(ch => "WUBRG".Contains(ch)).ToList();
                int landSlots = Slots - Ninety();
                int minBasics = identity.Count == 0 ? 0 : Math.Min(landSlots, Math.Max(identity.Count * 2, landSlots / 3));
                if (identity.Count >= 4) minBasics = Math.Min(landSlots, identity.Count * 2);
                if (_constructed) minBasics = identity.Count <= 1 ? Math.Max(0, landSlots - 4) : identity.Count == 2 ? landSlots / 2 : landSlots / 3;
                var lands = _eligible.Where(e => e.Card.IsLand && !e.Card.IsBasicLand && !_picked.Contains(e.Card.Name))
                                     .Select(e => (e, colors: LandColors(e.Card, identity), s: Score(e, Role.Land)))
                                     .Where(x => _constructed ? x.colors >= Math.Min(2, identity.Count) && (identity.Count >= 2 || x.e.Roles.Count > 1)
                                                              : x.colors > 0 || x.e.Roles.Count > 1)
                                     .OrderByDescending(x => x.colors).ThenByDescending(x => x.s).ToList();
                foreach (var (e, colors, s) in lands)
                {
                    int room = Slots - minBasics - Ninety();
                    if (room <= 0) break;
                    string lead = colors >= 3 ? $"Land — makes {colors} of your colors" : colors == 2 ? "Land — makes 2 of your colors"
                        : colors == 1 ? "Land" : "Utility land";
                    if (!_constructed)
                    {
                        if (!CanAdd(e)) continue;
                        Add(e, Why(e, lead), score: s);
                        continue;
                    }
                    if (_picked.Contains(e.Card.Name) || BreaksBracket(e)) continue;
                    int want = Math.Min(room, identity.Count >= 2 ? MaxCopies(e.Card) : Math.Min(2, MaxCopies(e.Card)));
                    if (!Copies(e, want, mustHave: false, out int copies, out int toGet)) continue;
                    Add(e, Why(e, lead, copies, toGet), score: s, copies: copies, toGet: toGet);
                }
                AddBasics(Deck, _eligible, identity, Slots - Ninety());
            }

            /// <summary>
            /// The runner-ups: taken in turns from each guide role's list (in the
            /// guide's order) and the plan's, so every job has a spare or two.
            /// <paramref name="count"/> counts cards (60-card: copies included).
            /// Inside the bracket; money isn't spent on them (they're suggestions).
            /// </summary>
            public void AddSwapOptions(int count)
            {
                if (count <= 0) return;
                var lists = new List<(Role? Role, List<(Eligible e, double s)> Ranked)>();
                if (_c.UseGuide)
                    foreach (var role in Order)
                        if (_c.Guide.For(role) is { Enabled: true, Count: > 0 })
                            lists.Add((role, Ranked(role)));
                lists.Add((null, Ranked(null)));
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var next = new int[lists.Count];
                int total = 0;
                bool any = true;
                while (total < count && any)
                {
                    any = false;
                    for (int i = 0; i < lists.Count && total < count; i++)
                    {
                        var (role, ranked) = lists[i];
                        while (next[i] < ranked.Count)
                        {
                            var (e, score) = ranked[next[i]++];
                            if (taken.Contains(e.Card.Name) || BreaksBracket(e)) continue;
                            int copies = _constructed ? Math.Max(1, Math.Min(Wanted(e), count - total)) : 1;
                            int own = e.Card.IsBasicLand ? copies : Math.Min(Math.Max(0, e.Card.FreeCopies), copies);
                            int toGet = copies - own;
                            string lead = "Swap option — " + (role is { } r ? RoleClassifier.Name(r) : PlanLead(e));
                            Deck.SwapOptions.Add(PickOf(e, Why(e, lead, copies, toGet), score, copies: copies, toGet: toGet));
                            taken.Add(e.Card.Name);
                            total += copies;
                            any = true;
                            break;
                        }
                    }
                }
            }

            /// <summary>5. How it went: card count, the guide's targets, combos in the deck, the curve (60-card).</summary>
            public void Finish()
            {
                Deck.CardCount = Deck.Picks.Where(p => !p.IsCommander).Sum(p => p.Copies) + (_constructed ? 0 : Math.Max(1, _c.Commanders.Count));
                foreach (var t in _c.Guide.Targets)
                    Deck.Roles.Add(new RoleResult
                    {
                        Role = t.Role, Target = t.Count, Enabled = _c.UseGuide && t.Enabled,
                        Got = t.Role == Role.Land ? Deck.Picks.Where(p => p.Card.IsLand && !p.IsCommander).Sum(p => p.Copies) : RoleCount(t.Role),
                    });
                foreach (var k in _combos)
                    if (k.Cards.All(n => _picked.Contains(n) || _c.Commanders.Contains(n, StringComparer.OrdinalIgnoreCase)))
                        Deck.Combos.Add(k);
                if (_constructed)
                {
                    string Label(int i) => i == 5 ? "6+" : (i + 1).ToString();
                    Deck.Notes.Add("Curve (spells by mana value): " + string.Join(" · ", Enumerable.Range(0, 6).Select(i =>
                        $"{Label(i)}: {_curve[i]}" + (_c.Curve is { } cv ? $"/{cv[i]}" : ""))));
                }
            }
        }

        /// <summary>Common words: a commander mentioning them says little about its plan.</summary>
        private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        {
            "spell", "token", "flying", "enters", "dies", "exile", "draw", "life", "attack", "land", "copy",
            "legendary", "sacrifice", "discard", "instant", "sorcery", "Human",
        };

        /// <summary>The commander's themes this card has (text, type line, or — for "multicolored" — its cost).</summary>
        private static IEnumerable<string> ThemeHits(Candidate card, List<string> themes)
        {
            foreach (var t in themes)
            {
                if (t == "multicolored")
                {
                    var colors = Symbol.Matches(card.ManaCost).SelectMany(m => m.Groups[1].Value.ToUpperInvariant())
                                       .Where(ch => "WUBRG".Contains(ch)).Distinct().Count();
                    if (colors >= 2 || card.OracleText.Contains("multicolored", StringComparison.OrdinalIgnoreCase)) yield return t;
                }
                else if (card.OracleText.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                         card.TypeLine.Contains(t, StringComparison.OrdinalIgnoreCase))
                    yield return t;
            }
        }

        /// <summary>The commanders' themes worth telling people about (not the common words).</summary>
        public static List<string> CommanderThemes(IEnumerable<Candidate> commanders) =>
            Themes(commanders).Where(t => !Generic.Contains(t)).ToList();

        /// <summary>Words in the commander's text worth matching in other cards (its themes).</summary>
        private static List<string> Themes(IEnumerable<Candidate> commanders)
        {
            var list = new List<string>();
            // Partners: both commanders' themes.
            string text = string.Join("\n", commanders.Select(x => x.OracleText));
            if (text.Length == 0) return list;
            string[] words =
            {
                "+1/+1 counter", "-1/-1 counter", "token", "sacrifice", "graveyard", "land", "artifact", "enchantment",
                "instant", "sorcery", "spell", "draw", "discard", "life", "attack", "flying", "equipment", "aura",
                "legendary", "multicolored", "dragon", "elf", "zombie", "vampire", "goblin", "wizard", "treasure",
                "proliferate", "energy", "experience", "mill", "exile", "copy", "dies", "enters",
            };
            foreach (var w in words)
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) list.Add(w);
            // Creature types its text cares about ("Spirits you control", "other Elves", "Dragon spells",
            // "the number of Clerics", "Clerics on the battlefield"). A type it only makes tokens of isn't a theme.
            foreach (Match m in Regex.Matches(text,
                @"\b(?<pl>[A-Z][a-z]+) you control\b|\b[Oo]ther (?<pl>[A-Z][a-z]+)\b|\bnumber of (?<pl>[A-Z][a-z]+)\b|" +
                @"\b(?<pl>[A-Z][a-z]+) on the battlefield\b|\b(?<type>[A-Z][a-z]+) spells?\b"))
            {
                string t = m.Groups["type"].Success ? m.Groups["type"].Value : Singular(m.Groups["pl"].Value);
                if (t.Length > 2 && !list.Contains(t, StringComparer.OrdinalIgnoreCase) && !NotATribe.Contains(t))
                    list.Add(t);
            }
            return list;
        }

        /// <summary>Capitalized words the patterns catch that aren't creature types.</summary>
        private static readonly HashSet<string> NotATribe = new(StringComparer.OrdinalIgnoreCase)
        {
            "You", "Whenever", "When", "Create", "Treasure", "Each", "Then", "Target", "Creature", "Permanent", "Land",
            "Artifact", "Enchantment", "Card", "Token", "Spell", "Player", "Opponent", "Planeswalker", "Instant", "Sorcery",
            "Legendary", "Nonland", "Noncreature", "Battle", "If", "All", "Other", "Untapped", "Tapped", "Attacking", "Blocking",
        };

        /// <summary>"Clerics" → "Cleric", "Elves" → "Elf", "Dwarves" → "Dwarf"; a word that isn't plural → "" (not a type list).</summary>
        private static string Singular(string word)
        {
            if (word.EndsWith("ves", StringComparison.Ordinal) && word.Length > 4) return word[..^3] + "f";
            if (word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal) && word.Length > 3) return word[..^1];
            return "";
        }

        /// <summary>How many of the deck's colors a land can make (from its text; "any color" = all).</summary>
        private static int LandColors(Candidate land, List<char> identity)
        {
            string text = land.OracleText;
            if (text.Contains("any color", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("mana of any type", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("commander's color identity", StringComparison.OrdinalIgnoreCase))
                return identity.Count;
            var colors = new HashSet<char>();
            foreach (Match m in Symbol.Matches(text))
                foreach (char ch in m.Groups[1].Value.ToUpperInvariant())
                    if (identity.Contains(ch)) colors.Add(ch);
            foreach (var (word, ch) in new[] { ("Plains", 'W'), ("Island", 'U'), ("Swamp", 'B'), ("Mountain", 'R'), ("Forest", 'G') })
                if ((land.TypeLine.Contains(word) || text.Contains(word)) && identity.Contains(ch)) colors.Add(ch);
            return colors.Count;
        }

        /// <summary>Basic lands for the remaining slots, split by how many of each color's symbols the spells need.</summary>
        private static void AddBasics(DraftDeck deck, IReadOnlyList<Eligible> eligible, List<char> identity, int slots)
        {
            if (slots <= 0) return;
            if (identity.Count == 0)
            {
                deck.Picks.Add(new Pick { Card = new Candidate { Name = "Wastes", TypeLine = "Basic Land" }, Copies = slots, Reason = "Basic land (colorless deck)" });
                return;
            }
            var pips = identity.ToDictionary(ch => ch, _ => 0);
            foreach (var p in deck.Picks.Where(p => !p.Card.IsLand))                 // the commander's symbols count too
                foreach (Match m in Symbol.Matches(p.Card.ManaCost))
                    foreach (char ch in m.Groups[1].Value.ToUpperInvariant())
                        if (pips.ContainsKey(ch)) pips[ch] += p.Copies;
            int total = Math.Max(1, pips.Values.Sum());
            var names = new Dictionary<char, string> { ['W'] = "Plains", ['U'] = "Island", ['B'] = "Swamp", ['R'] = "Mountain", ['G'] = "Forest" };
            var share = identity.ToDictionary(ch => ch, ch => pips.Values.Sum() == 0 ? slots / identity.Count : (int)Math.Floor(slots * (double)pips[ch] / total));
            // Every color gets at least one; the leftovers go to the most-needed colors.
            foreach (var ch in identity) if (share[ch] == 0) share[ch] = 1;
            while (share.Values.Sum() > slots) { var top = share.OrderByDescending(kv => kv.Value).First().Key; share[top]--; }
            foreach (var ch in identity.OrderByDescending(ch => pips[ch]))
                if (share.Values.Sum() < slots) share[ch]++;
            while (share.Values.Sum() < slots) share[identity.OrderByDescending(ch => pips[ch]).First()]++;
            foreach (var ch in identity.Where(ch => share[ch] > 0))
            {
                var basic = eligible.FirstOrDefault(e => e.Card.IsBasicLand && e.Card.Name == names[ch])?.Card
                            ?? new Candidate { Name = names[ch], TypeLine = "Basic Land — " + names[ch] };
                deck.Picks.Add(new Pick
                {
                    Card = basic, Copies = share[ch], Roles = new HashSet<Role> { Role.Land },
                    Reason = $"Basic land — your spells have {pips[ch]} {ColorName(ch)} mana symbol{(pips[ch] == 1 ? "" : "s")}",
                });
            }
        }

        private static string ColorName(char ch) => ch switch
        {
            'W' => "white", 'U' => "blue", 'B' => "black", 'R' => "red", 'G' => "green", _ => "colorless",
        };
    }
}
