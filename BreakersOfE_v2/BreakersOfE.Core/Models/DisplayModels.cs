namespace BreakersOfE.Models
{
    // ── Deck usage row (nested "Used in Decks" table) ─────────────────────────
    // The rest of v1's display layer (CollectionDisplayRow and friends) was
    // removed in v2: grids bind the models directly.
    public class DeckUsageRow
    {
        public string DeckName { get; set; } = string.Empty;
        public string DeckType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Category { get; set; } = string.Empty;
        public string IsFoil { get; set; } = string.Empty;
    }
}
