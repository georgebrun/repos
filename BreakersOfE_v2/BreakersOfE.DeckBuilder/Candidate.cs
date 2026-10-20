namespace BreakersOfE.DeckBuilder
{
    /// <summary>Where a candidate card would come from.</summary>
    public enum CardSource
    {
        /// <summary>Free copies in your collection (no deck or the Trade Binder claims them).</summary>
        Collection,
        /// <summary>The card pool: you'd have to get it (it goes on the Want List).</summary>
        Pool,
    }

    /// <summary>
    /// One card the builder may pick, by card name (any printing). The app
    /// fills these from the card pool, your collection, card tags, EDHREC and
    /// the combo list; anything it doesn't know stays at its default.
    /// </summary>
    public sealed class Candidate
    {
        public string Name { get; init; } = "";
        public string OracleId { get; init; } = "";
        public string TypeLine { get; init; } = "";
        public string OracleText { get; init; } = "";
        public string ManaCost { get; init; } = "";
        public double ManaValue { get; init; }
        /// <summary>Color identity letters ("WUBRG" subset; "" = colorless).</summary>
        public string ColorIdentity { get; init; } = "";
        /// <summary>Scryfall keywords ("Flying", "Ward" …).</summary>
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
        /// <summary>Scryfall card tags (slugs: "ramp", "mana-rock", "removal" …), when downloaded.</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        /// <summary>Legal in the deck's format.</summary>
        public bool Legal { get; init; } = true;
        /// <summary>On the Commander Game Changers list.</summary>
        public bool IsGameChanger { get; init; }

        /// <summary>Copies you own (any printing) and how many of them are free.</summary>
        public int OwnedCopies { get; init; }
        public int FreeCopies { get; init; }
        /// <summary>Cheapest market price you could buy it at (null = unknown).</summary>
        public decimal? Price { get; init; }

        /// <summary>EDHREC for the deck's commander: share of decks that run it (0–100) and synergy (−1…1); null when unknown.</summary>
        public double? EdhrecInclusion { get; init; }
        public double? EdhrecSynergy { get; init; }
        /// <summary>EDHREC salt (0–4); null when not on the list.</summary>
        public double? Salt { get; init; }

        // ── Worked out from the type line ──────────────────────────────
        public bool IsLand => TypeLine.Contains("Land", StringComparison.Ordinal);
        public bool IsBasicLand => TypeLine.StartsWith("Basic", StringComparison.Ordinal) ||
                                   TypeLine.StartsWith("Snow Basic", StringComparison.Ordinal);
        public bool IsCreature => TypeLine.Contains("Creature", StringComparison.Ordinal);
        public bool IsLegendaryCreature => TypeLine.Contains("Legendary", StringComparison.Ordinal) && IsCreature;

        /// <summary>Free in your collection, owned but all in use, or not owned.</summary>
        public CardSource Source => FreeCopies > 0 ? CardSource.Collection : CardSource.Pool;

        public override string ToString() => Name;
    }
}
