namespace BreakersOfE.DeckBuilder
{
    /// <summary>Which cards the builder may use.</summary>
    public enum SourceMode
    {
        /// <summary>Only free copies in your collection.</summary>
        CollectionOnly,
        /// <summary>Your free copies first, plus cards you'd have to get (up to the spend limit).</summary>
        CollectionAndPool,
    }

    /// <summary>What kind of deck: Commander (100, singleton, a commander) or 60-card Constructed.</summary>
    public enum DeckShape
    {
        Commander,
        /// <summary>60 cards, up to 4 copies of each, colors you choose, one format's legal cards.</summary>
        Constructed,
    }

    /// <summary>What you tell the builder (the Build panel). Everything has a sensible default.</summary>
    public sealed class BuildConstraints
    {
        public DeckShape Shape { get; init; } = DeckShape.Commander;
        public bool IsConstructed => Shape == DeckShape.Constructed;
        /// <summary>Copies of one card a deck may have (basic lands aside): Commander 1, Constructed 4.</summary>
        public int CopyLimit => IsConstructed ? 4 : 1;

        /// <summary>The commander(s) (one, or two partners). Their color identity limits the deck.</summary>
        public IReadOnlyList<string> Commanders { get; init; } = Array.Empty<string>();
        /// <summary>The deck's colors ("WUBRG" subset; "" = colorless): the commanders' identity, or the colors chosen (Constructed).</summary>
        public string ColorIdentity { get; init; } = "";
        /// <summary>Total cards, commanders included (Commander: 100).</summary>
        public int DeckSize { get; init; } = 100;

        /// <summary>Commander Bracket aimed for (1–5, default 3). Constructed: 3 = casual, 4 = competitive.</summary>
        public int Bracket { get; init; } = 3;

        public SourceMode Sources { get; init; } = SourceMode.CollectionOnly;
        /// <summary>Most to spend on cards you don't own ($0 = nothing to buy).</summary>
        public decimal SpendLimit { get; init; }
        /// <summary>Optional: the most the finished deck may be worth (all cards, owned too). Null = no cap.</summary>
        public decimal? DeckValueCap { get; init; }

        /// <summary>Cards that must be in the deck (by name).</summary>
        public IReadOnlyCollection<string> Locked { get; init; } = Array.Empty<string>();
        /// <summary>Copies of a locked card (Constructed; a key card or a lock from the draft). Missing: as many as allowed.</summary>
        public IReadOnlyDictionary<string, int>? LockedCopies { get; init; }
        /// <summary>Words for the deck's plan ("tokens", "+1/+1 counter", "Elf"): cards with them fit the deck.</summary>
        public IReadOnlyList<string> PlanWords { get; init; } = Array.Empty<string>();
        /// <summary>Swap options: how many runner-up cards to suggest (they go to the deck's sideboard on Accept; 0 = none).</summary>
        public int SwapOptions { get; init; }

        /// <summary>Constructed: multicolored spells (two or more colors) may be used.</summary>
        public bool AllowMulticolor { get; init; } = true;
        /// <summary>Constructed: colorless spells (artifacts, Eldrazi …) may be used.</summary>
        public bool AllowColorless { get; init; } = true;
        /// <summary>Constructed: spells wanted at mana value 1, 2, 3, 4, 5, 6+ (null = no curve target).</summary>
        public IReadOnlyList<int>? Curve { get; init; }

        /// <summary>Cards the builder must not pick (by name).</summary>
        public IReadOnlyCollection<string> Excluded { get; init; } = Array.Empty<string>();

        /// <summary>Use the role guide at all (off = just the best cards).</summary>
        public bool UseGuide { get; init; } = true;
        public RoleGuide Guide { get; init; } = RoleGuide.CommanderDefault();

        /// <summary>Game Changers the bracket allows (Brackets 1–2: none; 3: up to 3; 4–5: any).</summary>
        public int GameChangerLimit => IsConstructed ? int.MaxValue : Bracket <= 2 ? 0 : Bracket == 3 ? 3 : int.MaxValue;
        /// <summary>Two-card infinite combos allowed (Bracket 4–5 only; 3 allows late-game ones, which cards can't tell). Constructed: always.</summary>
        public bool AllowTwoCardCombos => IsConstructed || Bracket >= 4;
        /// <summary>Mass land denial and chained extra turns (Bracket 4–5 only). Constructed: the format decides.</summary>
        public bool AllowLandDenialAndExtraTurns => IsConstructed || Bracket >= 4;

        /// <summary>The same constraints with other locked cards (Guided Build: the cards chosen so far).</summary>
        public BuildConstraints WithLocked(IReadOnlyCollection<string> locked, IReadOnlyDictionary<string, int>? lockedCopies = null) => new()
        {
            Shape = Shape, Commanders = Commanders, ColorIdentity = ColorIdentity, DeckSize = DeckSize, Bracket = Bracket,
            Sources = Sources, SpendLimit = SpendLimit, DeckValueCap = DeckValueCap,
            Locked = locked, LockedCopies = lockedCopies ?? LockedCopies, Excluded = Excluded, UseGuide = UseGuide, Guide = Guide,
            PlanWords = PlanWords, Curve = Curve, AllowMulticolor = AllowMulticolor, AllowColorless = AllowColorless,
            SwapOptions = SwapOptions,
        };

        /// <summary>The same constraints with the deck's colors and commanders filled in (the service does this).</summary>
        public BuildConstraints With(IReadOnlyList<string> commanders, string colorIdentity) => new()
        {
            Shape = Shape, Commanders = commanders, ColorIdentity = colorIdentity, DeckSize = DeckSize, Bracket = Bracket,
            Sources = Sources, SpendLimit = SpendLimit, DeckValueCap = DeckValueCap,
            Locked = Locked, LockedCopies = LockedCopies, Excluded = Excluded, UseGuide = UseGuide, Guide = Guide,
            PlanWords = PlanWords, Curve = Curve, AllowMulticolor = AllowMulticolor, AllowColorless = AllowColorless,
            SwapOptions = SwapOptions,
        };

        /// <summary>Spell curve presets for 36 spells (mana value 1, 2, 3, 4, 5, 6+), scaled to the deck's spell slots.</summary>
        public static IReadOnlyList<int> CurvePreset(string name, int spells)
        {
            int[] baseCurve = name switch
            {
                "Low" => new[] { 8, 12, 10, 4, 2, 0 },
                "High" => new[] { 2, 8, 8, 8, 5, 5 },
                _ => new[] { 4, 10, 10, 7, 3, 2 },
            };
            double k = spells / 36.0;
            var scaled = baseCurve.Select(n => (int)Math.Round(n * k)).ToArray();
            // Rounding: even it out on mana values 2–4 (adding), or the biggest number (taking away).
            for (int i = 0; scaled.Sum() < spells; i++) scaled[1 + i % 3]++;
            while (scaled.Sum() > spells) scaled[Array.IndexOf(scaled, scaled.Max())]--;
            return scaled;
        }

        /// <summary>"3 — Upgraded".</summary>
        public static string BracketName(int bracket) => bracket switch
        {
            1 => "1 — Exhibition", 2 => "2 — Core", 3 => "3 — Upgraded", 4 => "4 — Optimized", 5 => "5 — cEDH",
            _ => bracket.ToString(),
        };
    }

    /// <summary>One guide target: how many cards of a role, and whether it's on.</summary>
    public sealed class RoleTarget
    {
        public Role Role { get; init; }
        public int Count { get; set; }
        public bool Enabled { get; set; } = true;
        public override string ToString() => $"{RoleClassifier.Name(Role)}: {Count}{(Enabled ? "" : " (off)")}";
    }

    /// <summary>
    /// The deck-building guide: about how many of each role. Only a guide —
    /// every target can be changed or switched off, and the builder never
    /// adds a bad card just to reach a number.
    /// </summary>
    public sealed class RoleGuide
    {
        public List<RoleTarget> Targets { get; init; } = new();

        public RoleTarget? For(Role r) => Targets.FirstOrDefault(t => t.Role == r);

        /// <summary>
        /// A common Commander starting point (99 cards + commander): 37 lands,
        /// 10 ramp, 10 card draw, 8 removal, 3 board wipes, the rest for the
        /// deck's plan. Counterspells, protection, tutors and recursion start
        /// at a few each; finishers at 3.
        /// </summary>
        /// <summary>
        /// A common 60-card starting point (counted in cards, copies included):
        /// 24 lands, 8 removal, 6 card draw, 4 finishers, a little ramp and a
        /// couple of sweepers; the rest for the deck's plan. Counterspells,
        /// protection, tutors and recursion start off (turn them on for decks that want them).
        /// </summary>
        public static RoleGuide ConstructedDefault() => new()
        {
            Targets =
            {
                new() { Role = Role.Land, Count = 24 },
                new() { Role = Role.Ramp, Count = 4 },
                new() { Role = Role.Draw, Count = 6 },
                new() { Role = Role.Removal, Count = 8 },
                new() { Role = Role.BoardWipe, Count = 2 },
                new() { Role = Role.Protection, Count = 0, Enabled = false },
                new() { Role = Role.Counterspell, Count = 4, Enabled = false },
                new() { Role = Role.Tutor, Count = 0, Enabled = false },
                new() { Role = Role.Recursion, Count = 0, Enabled = false },
                new() { Role = Role.Finisher, Count = 4 },
            },
        };

        public static RoleGuide CommanderDefault() => new()
        {
            Targets =
            {
                new() { Role = Role.Land, Count = 37 },
                new() { Role = Role.Ramp, Count = 10 },
                new() { Role = Role.Draw, Count = 10 },
                new() { Role = Role.Removal, Count = 8 },
                new() { Role = Role.BoardWipe, Count = 3 },
                new() { Role = Role.Protection, Count = 3 },
                new() { Role = Role.Counterspell, Count = 2 },
                new() { Role = Role.Tutor, Count = 2 },
                new() { Role = Role.Recursion, Count = 2 },
                new() { Role = Role.Finisher, Count = 3 },
            },
        };
    }
}
