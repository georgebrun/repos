using System.Windows.Media;

namespace BreakersOfE.Services
{
    public enum TableType { Pool, Collection, Deck }

    public static class CardColorService
    {
        // ════════════════════════════════════════════════════════════════════
        // FOREGROUND — theme-aware text color by color identity
        // ════════════════════════════════════════════════════════════════════
        public static Brush GetForeground(string colorIdentity,
            string typeLine, bool isFoil)
        {
            bool dark = ThemeService.CurrentTheme == AppTheme.Dark;

            // Land
            if (!string.IsNullOrWhiteSpace(typeLine) &&
                typeLine.Contains("Land"))
                return dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xDE, 0xB8, 0x87)) // burlywood
                    : new SolidColorBrush(
                        Color.FromRgb(0x8B, 0x45, 0x13)); // saddle brown

            // Artifact / colorless
            if (string.IsNullOrWhiteSpace(colorIdentity) ||
                (!string.IsNullOrWhiteSpace(typeLine) &&
                 typeLine.Contains("Artifact")))
                return dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xBB, 0xBB, 0xBB)) // light gray
                    : new SolidColorBrush(
                        Color.FromRgb(0x55, 0x55, 0x55)); // dark gray

            // Multicolor
            if (colorIdentity.Length > 1)
                return dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xFF, 0xD7, 0x00)) // bright gold
                    : new SolidColorBrush(
                        Color.FromRgb(0xB8, 0x86, 0x0B)); // dark goldenrod

            return colorIdentity switch
            {
                "W" => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xFF, 0xD7, 0x00)) // bright gold
                    : new SolidColorBrush(
                        Color.FromRgb(0x8B, 0x7D, 0x00)), // dark gold

                "U" => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0x4D, 0xA6, 0xFF)) // light blue
                    : new SolidColorBrush(
                        Color.FromRgb(0x00, 0x50, 0xAA)), // dark blue

                "B" => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xCC, 0x99, 0xFF)) // light purple
                    : new SolidColorBrush(
                        Color.FromRgb(0x66, 0x00, 0xAA)), // purple

                "R" => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xFF, 0x66, 0x66)) // light red
                    : new SolidColorBrush(
                        Color.FromRgb(0xCC, 0x00, 0x00)), // dark red

                "G" => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0x66, 0xCC, 0x66)) // light green
                    : new SolidColorBrush(
                        Color.FromRgb(0x00, 0x64, 0x00)), // dark green

                _ => dark
                    ? new SolidColorBrush(
                        Color.FromRgb(0xFF, 0xD7, 0x00))
                    : new SolidColorBrush(
                        Color.FromRgb(0xB8, 0x86, 0x0B))
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // BACKGROUND — alternating rows + foil shimmer
        // ════════════════════════════════════════════════════════════════════
        public static Brush GetBackground(bool isFoil, int rowIndex,
            TableType tableType = TableType.Collection)
        {
            bool dark = ThemeService.CurrentTheme == AppTheme.Dark;
            bool isEven = rowIndex % 2 == 0;

            if (dark)
            {
                return tableType switch
                {
                    TableType.Pool => isEven
                        ? new SolidColorBrush(Color.FromRgb(0x2E, 0x1A, 0x1A))  // red-tinted dark
                        : new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x20)),
                    TableType.Deck => isEven
                        ? new SolidColorBrush(Color.FromRgb(0x18, 0x2A, 0x1A))  // green-tinted dark
                        : new SolidColorBrush(Color.FromRgb(0x1E, 0x36, 0x22)),
                    _ => isEven
                        ? new SolidColorBrush(Color.FromRgb(0x18, 0x22, 0x30))  // blue-tinted dark
                        : new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x3C)),
                };
            }
            else
            {
                return tableType switch
                {
                    TableType.Pool => isEven
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xED)),  // faded red light
                    TableType.Deck => isEven
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0xED, 0xFF, 0xED)),  // faded green light
                    _ => isEven
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
                };
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CELL BORDER BRUSH
        // ════════════════════════════════════════════════════════════════════
        public static Brush GetCellBorderBrush()
        {
            bool dark = ThemeService.CurrentTheme == AppTheme.Dark;
            return dark
                ? new SolidColorBrush(
                    Color.FromRgb(0xE0, 0xE0, 0xE0)) // off-white
                : new SolidColorBrush(
                    Color.FromRgb(0x00, 0x00, 0x00)); // black
        }

        // ════════════════════════════════════════════════════════════════════
        // FOIL BRUSH BUILDER
        // ════════════════════════════════════════════════════════════════════
        private static LinearGradientBrush BuildFoilBrush(bool dark)
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0.5, 0),
                EndPoint = new System.Windows.Point(0.5, 1)
            };

            if (dark)
            {
                // Dark mode — gold shimmer
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0x3D, 0x30, 0x00), 0.0));
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0x7A, 0x60, 0x00), 0.5));
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0x3D, 0x30, 0x00), 1.0));
            }
            else
            {
                // Light mode — silver shimmer
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0xB5, 0xB5, 0xB5), 0.0));
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0xF2, 0xF2, 0xF2), 0.5));
                g.GradientStops.Add(new GradientStop(
                    Color.FromRgb(0xB5, 0xB5, 0xB5), 1.0));
            }

            return g;
        }
    }
}