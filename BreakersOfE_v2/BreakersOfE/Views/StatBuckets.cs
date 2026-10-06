namespace BreakersOfE.Views
{
    /// <summary>
    /// How Deck Statistics and Collection Statistics group cards into bars:
    /// one set of rules for both windows (they had drifted apart before —
    /// Bonus rarity was only counted in one of them).
    /// </summary>
    internal static class StatBuckets
    {
        public static readonly string[] ColorOrder =
            { "White", "Blue", "Black", "Red", "Green", "Multicolor", "Colorless", "Land" };

        public static readonly string[] TypeOrder =
            { "Creature", "Planeswalker", "Battle", "Instant", "Sorcery", "Artifact", "Enchantment", "Land", "Other" };

        public static readonly string[] RarityOrder =
            { "Common", "Uncommon", "Rare", "Mythic", "Special", "Bonus", "Other" };

        /// <summary>Lands on their own; else the card's color ("M" = several).</summary>
        public static string Color(string? typeLine, string? colorDisplay)
        {
            if ((typeLine ?? "").Contains("Land", StringComparison.OrdinalIgnoreCase)) return "Land";
            return colorDisplay switch
            {
                "W" => "White",
                "U" => "Blue",
                "B" => "Black",
                "R" => "Red",
                "G" => "Green",
                "M" => "Multicolor",
                _ => "Colorless",
            };
        }

        public static string Rarity(string? rarity) => (rarity ?? "").ToLowerInvariant() switch
        {
            "common" => "Common",
            "uncommon" => "Uncommon",
            "rare" => "Rare",
            "mythic" => "Mythic",
            "special" => "Special",
            "bonus" => "Bonus",
            _ => "Other",
        };

        /// <summary>First matching type, so each card counts once.</summary>
        public static string Type(string? typeLine)
        {
            string t = typeLine ?? "";
            foreach (var type in TypeOrder)
                if (type != "Other" && t.Contains(type, StringComparison.OrdinalIgnoreCase)) return type;
            return "Other";
        }
    }
}
