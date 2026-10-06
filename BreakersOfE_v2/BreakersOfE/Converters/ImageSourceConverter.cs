using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Converts a file path to an ImageSource. SVG files are rendered via
    /// SharpVectors; bitmaps are loaded directly. Supports both single-value
    /// binding (path only) and multi-value binding (path + rarity for
    /// rarity-tinted set symbols).
    /// </summary>
    public class ImageSourceConverter : IValueConverter, IMultiValueConverter
    {
        // ── IValueConverter (path only, no tinting) ──────────────────────
        public object? Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path))
                return null;
            return LoadImage(path, null);
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        // ── IMultiValueConverter (path + rarity for tinted set symbols) ──
        public object? Convert(object[] values, Type targetType,
            object parameter, CultureInfo culture)
        {
            string? path = values.Length > 0 ? values[0] as string : null;
            string? rarity = values.Length > 1 ? values[1] as string : null;
            if (string.IsNullOrEmpty(path)) return null;
            return LoadImage(path!, rarity);
        }

        public object[] ConvertBack(object value, Type[] targetTypes,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        // ── Cache: each symbol (file + rarity tint + theme) is drawn once and
        //    shared by every row. Misses aren't kept (a later download shows). ──
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _drawn =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Forget drawn symbols (after Update Database downloads new ones).</summary>
        public static void ClearCache() => _drawn.Clear();

        private static ImageSource? LoadImage(string path, string? rarity)
        {
            string key = $"{path}|{rarity}|{ThemeService.CurrentTheme}";
            if (_drawn.TryGetValue(key, out var hit)) return hit;
            var img = Render(path, rarity);
            if (img != null) _drawn[key] = img;
            return img;
        }

        // ── Core rendering ──────────────────────────────────────────────
        private static ImageSource? Render(string path, string? rarity)
        {
            try
            {
                if (!File.Exists(path)) return null;

                // Sniff content: SVG starts with '<'
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);
                string h = System.Text.Encoding.UTF8.GetString(header);

                if (h.TrimStart().StartsWith("<"))
                {
                    // Render SVG
                    var settings = new WpfDrawingSettings
                    {
                        IncludeRuntime = true,
                        TextAsGeometry = false
                    };
                    var converter = new FileSvgConverter(settings);
                    converter.Convert(path);
                    if (converter.Drawing == null) return null;

                    // Apply rarity tint
                    var tint = GetRarityBrush(rarity);
                    if (tint != null)
                        TintDrawing(converter.Drawing, tint);

                    var img = new DrawingImage(converter.Drawing);
                    img.Freeze();
                    return img;
                }

                // Regular bitmap
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ── Rarity → brush mapping (standard MTG set symbol colors) ─────
        private static SolidColorBrush? GetRarityBrush(string? rarity)
        {
            if (string.IsNullOrEmpty(rarity)) return null;

            bool dark = ThemeService.CurrentTheme == AppTheme.Dark;

            return rarity.ToLower() switch
            {
                // Neutral symbol on the app's own surfaces (set browser tiles,
                // Set Completion): light grey on Dark, dark grey on Light.
                "ondark" => dark
                    ? new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8))
                    : new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                // Card tables keep their light rows in every theme: black.
                "common" => new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                "uncommon" => new SolidColorBrush(Color.FromRgb(0x84, 0x93, 0xA0)), // silver
                "rare" => new SolidColorBrush(Color.FromRgb(0xC8, 0xA2, 0x00)), // gold
                "mythic" => new SolidColorBrush(Color.FromRgb(0xD4, 0x50, 0x20)), // orange-red
                "special" => new SolidColorBrush(Color.FromRgb(0x90, 0x50, 0xC0)), // purple
                "bonus" => new SolidColorBrush(Color.FromRgb(0x90, 0x50, 0xC0)), // purple
                _ => null
            };
        }

        /// <summary>
        /// Recursively tint all solid fills in a Drawing to the given brush.
        /// This replaces the SVG's black fills with the rarity color.
        /// </summary>
        private static void TintDrawing(Drawing drawing, SolidColorBrush tint)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (var child in group.Children)
                        TintDrawing(child, tint);
                    break;

                case GeometryDrawing geo:
                    if (geo.Brush is SolidColorBrush)
                        geo.Brush = tint;
                    if (geo.Pen?.Brush is SolidColorBrush)
                        geo.Pen.Brush = tint;
                    break;

                case GlyphRunDrawing glyph:
                    if (glyph.ForegroundBrush is SolidColorBrush)
                        glyph.ForegroundBrush = tint;
                    break;
            }
        }
    }
}