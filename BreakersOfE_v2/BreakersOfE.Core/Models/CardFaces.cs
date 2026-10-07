namespace BreakersOfE.Models
{
    /// <summary>
    /// Double-faced, adventure and split cards keep one value per side, joined
    /// with " // " the way the type line is ("Creature // Land"). A side that
    /// doesn't have the value shows "—" so the two sides stay lined up
    /// (Power "6 // —" on a creature // land card).
    /// </summary>
    public static class CardFaces
    {
        public const string Separator = " // ";
        public const string None = "—";

        /// <summary>Between the sides' rules and flavor text: a line with "//".</summary>
        public const string TextSeparator = "\n//\n";

        /// <summary>The sides of a value ("1 // 5" → "1", "5"; a one-sided value → itself).</summary>
        public static string[] Sides(string? value) =>
            (value ?? "").Split("//", System.StringSplitOptions.TrimEntries);

        /// <summary>The sides of rules or flavor text (split on the "//" line only, not a "//" inside the text).</summary>
        public static string[] TextSides(string? text) =>
            (text ?? "").Replace("\r\n", "\n").Split(TextSeparator);

        /// <summary>
        /// One side of a value (0 = front, 1 = back). A value with fewer sides
        /// than asked for belongs to the front (a mana cost: the back of a
        /// transforming card or a land back has none), so the back gets "".
        /// </summary>
        public static string Side(string[] sides, int side) =>
            side < sides.Length ? (sides[side] == None ? "" : sides[side]) : "";

        /// <summary>
        /// "2/3", or per side for two-sided cards: "1/2 // 5/5", "6/6 // —".
        /// Empty when no side has power and toughness.
        /// </summary>
        public static string PowerToughness(string? power, string? toughness)
        {
            if (string.IsNullOrWhiteSpace(power) || string.IsNullOrWhiteSpace(toughness)) return "";
            var p = Sides(power);
            var t = Sides(toughness);
            if (p.Length == 1 && t.Length == 1) return $"{power}/{toughness}";

            int n = System.Math.Max(p.Length, t.Length);
            var parts = new string[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                string a = i < p.Length ? p[i] : "", b = i < t.Length ? t[i] : "";
                bool has = a.Length > 0 && b.Length > 0 && a != None && b != None;
                parts[i] = has ? $"{a}/{b}" : None;
                any |= has;
            }
            return any ? string.Join(Separator, parts) : "";
        }
    }
}
