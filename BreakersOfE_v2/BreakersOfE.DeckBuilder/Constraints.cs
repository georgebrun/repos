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

    /// <summary>What you tell the builder (the Build panel). Everything has a sensible default.</summary>
    public sealed class BuildConstraints
    {
        /// <summary>The commander(s) (one, or two partners). Their color identity limits the deck.</summary>
        public IReadOnlyList<string> Commanders { get; init; } = Array.Empty<string>();
        /// <summary>The commanders' combined color identity ("WUBRG" subset; "" = colorless).</summary>
        public string ColorIdentity { get; init; } = "";
        /// <summary>Total cards, commanders included (Commander: 100).</summary>
        public int DeckSize { get; init; } = 100;

        /// <summary>Commander Bracket aimed for (1–5, default 3).</summary>
        public int Bracket { get; init; } = 3;

        public SourceMode Sources { get; init; } = SourceMode.CollectionOnly;
        /// <summary>Most to spend on cards you don't own ($0 = nothing to buy).</summary>
        public decimal SpendLimit { get; init; }
        /// <summary>Optional: the most the finished deck may be worth (all cards, owned too). Null = no cap.</summary>
        public decimal? DeckValueCap { get; init; }

        /// <summary>Cards that must be in the deck (by name).</summary>
        public IReadOnlyCollection<string> Locked { get; init; } = Array.Empty<string>();
        /// <summary>Cards the builder must not pick (by name).</summary>
        public IReadOnlyCollection<string> Excluded { get; init; } = Array.Empty<string>();

        /// <summary>Use the role guide at all (off = just the best cards).</summary>
        public bool UseGuide { get; init; } = true;
        public RoleGuide Guide { get; init; } = RoleGuide.CommanderDefault();

        /// <summary>Game Changers the bracket allows (Brackets 1–2: none; 3: up to 3; 4–5: any).</summary>
        public int GameChangerLimit => Bracket <= 2 ? 0 : Bracket == 3 ? 3 : int.MaxValue;
        /// <summary>Two-card infinite combos allowed (Bracket 4–5 only; 3 allows late-game ones, which cards can't tell).</summary>
        public bool AllowTwoCardCombos => Bracket >= 4;
        /// <summary>Mass land denial and chained extra turns (Bracket 4–5 only).</summary>
        public bool AllowLandDenialAndExtraTurns => Bracket >= 4;

        /// <summary>The same constraints with other locked cards (Guided Build: the cards chosen so far).</summary>
        public BuildConstraints WithLocked(IReadOnlyCollection<string> locked) => new()
        {
            Commanders = Commanders, ColorIdentity = ColorIdentity, DeckSize = DeckSize, Bracket = Bracket,
            Sources = Sources, SpendLimit = SpendLimit, DeckValueCap = DeckValueCap,
            Locked = locked, Excluded = Excluded, UseGuide = UseGuide, Guide = Guide,
        };

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
