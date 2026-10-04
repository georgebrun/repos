using System.Windows.Media;

namespace BreakersOfE.Services
{
    public enum TableType { Pool, Collection, Deck, TradeBinder, WantList }

    /// <summary>
    /// Card-table colours (v1's light rows). The tables keep this look in
    /// every app theme (Light, Dark, Follow Windows), so nothing here depends
    /// on the theme.
    /// • Text: the card's colour — lands brown, colourless grey, two or more
    ///   colours gold, else white / blue / black / red / green.
    /// • Rows: white, alternating with a light tint per table (Pool red,
    ///   decks green, the rest blue). Alternation follows the row's place on
    ///   screen (see <see cref="RowBackground"/>), so it survives sorting.
    /// </summary>
    public static class CardColorService
    {
        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // ── Text colours (unchanged from v1's light theme) ───────────────
        private static readonly Brush LandText = Frozen(0x8B, 0x45, 0x13);       // saddle brown
        private static readonly Brush ColorlessText = Frozen(0x55, 0x55, 0x55);  // dark grey
        private static readonly Brush MultiText = Frozen(0xB8, 0x86, 0x0B);      // dark goldenrod
        private static readonly Brush WhiteText = Frozen(0x8B, 0x7D, 0x00);      // dark gold
        private static readonly Brush BlueText = Frozen(0x00, 0x50, 0xAA);
        private static readonly Brush BlackText = Frozen(0x66, 0x00, 0xAA);      // purple
        private static readonly Brush RedText = Frozen(0xCC, 0x00, 0x00);
        private static readonly Brush GreenText = Frozen(0x00, 0x64, 0x00);

        // ── Row colours ──────────────────────────────────────────────────
        private static readonly Brush RowWhite = Frozen(0xFF, 0xFF, 0xFF);
        private static readonly Brush PoolTint = Frozen(0xFF, 0xED, 0xED);       // faded red
        private static readonly Brush DeckTint = Frozen(0xED, 0xFF, 0xED);       // faded green
        private static readonly Brush CollectionTint = Frozen(0xEE, 0xF2, 0xF7); // faded blue
        private static readonly Brush CellBorder = Frozen(0x00, 0x00, 0x00);

        // ════════════════════════════════════════════════════════════════════
        // TEXT — the card's colour
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Row text for a card whose colours are known (Scryfall's "colors").
        /// Two-faced cards don't carry colours for the whole card, so those
        /// use the colour identity instead.
        /// </summary>
        public static Brush GetForeground(string? colors, string? colorIdentity, string? typeLine)
        {
            if (IsLand(typeLine)) return LandText;
            string c = colors ?? "";
            if (c.Length == 0 && (typeLine ?? "").Contains("//"))
                c = colorIdentity ?? "";
            return ByColors(c);
        }

        /// <summary>
        /// Row text for a card known only by mana cost and colour identity
        /// (deck lines): the coloured mana symbols of the front face; no
        /// mana cost at all (a two-faced card, or a card with a colour
        /// dot instead) → the colour identity.
        /// </summary>
        public static Brush GetForegroundFromCost(string? manaCost, string? colorIdentity, string? typeLine)
        {
            if (IsLand(typeLine)) return LandText;
            string cost = FrontFace(manaCost);
            if (cost.Length == 0) return ByColors(colorIdentity ?? "");
            var colours = new System.Text.StringBuilder();
            bool inSymbol = false;
            foreach (char ch in cost)
            {
                if (ch == '{') inSymbol = true;
                else if (ch == '}') inSymbol = false;
                else if (inSymbol && "WUBRG".IndexOf(char.ToUpperInvariant(ch)) >= 0) colours.Append(char.ToUpperInvariant(ch));
            }
            return ByColors(colours.ToString());
        }

        /// <summary>Lands are brown (judged by the front face: a spell // land card is the spell's colour).</summary>
        private static bool IsLand(string? typeLine) => FrontFace(typeLine).Contains("Land");

        private static string FrontFace(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOf("//", System.StringComparison.Ordinal);
            return cut < 0 ? text : text[..cut];
        }

        private static Brush ByColors(string colors)
        {
            char single = '\0';
            int count = 0;
            foreach (char ch in "WUBRG")
                if (colors.IndexOf(ch) >= 0 || colors.IndexOf(char.ToLowerInvariant(ch)) >= 0)
                {
                    single = ch;
                    count++;
                }
            if (count == 0) return ColorlessText;
            if (count > 1) return MultiText;
            return single switch
            {
                'W' => WhiteText,
                'U' => BlueText,
                'B' => BlackText,
                'R' => RedText,
                _ => GreenText,
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // ROWS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A row's fill from its place on screen (0 = white, 1 = tinted), so
        /// sorting never puts two tinted rows together. Deck footers and the
        /// commander keep their own colours.
        /// </summary>
        public static Brush RowBackground(object? item, int alternation)
        {
            bool tinted = alternation % 2 == 1;
            switch (item)
            {
                case Models.DeckCard d when d.IsFooter || d.IsCommander:
                    return d.RowBackgroundBrush;
                case Models.DeckCard:
                    return tinted ? DeckTint : RowWhite;
                case Models.IOwnedCard:                                   // the Pool's card kinds
                    return tinted ? PoolTint : RowWhite;
                default:
                    return tinted ? CollectionTint : RowWhite;
            }
        }

        /// <summary>A row's fill by its number (older bindings; the tables use <see cref="RowBackground"/>).</summary>
        public static Brush GetBackground(bool isFoil, int rowIndex,
            TableType tableType = TableType.Collection)
        {
            if (rowIndex % 2 == 0) return RowWhite;
            return tableType switch
            {
                TableType.Pool => PoolTint,
                TableType.Deck => DeckTint,
                _ => CollectionTint,
            };
        }

        public static Brush GetCellBorderBrush() => CellBorder;
    }
}