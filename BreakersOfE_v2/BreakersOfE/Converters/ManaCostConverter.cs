using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    public class ManaCostConverter : IValueConverter
    {
        private static string ManaFolder => AppFolderService.ManaSymbolsFolder;

        /// <summary>
        /// Card detail views: put a mana cost's symbols into an ItemsControl
        /// (the converter builds one panel; its pieces move into the list).
        /// </summary>
        public static void Fill(ItemsControl target, string? manaCost)
        {
            target.Items.Clear();
            if (string.IsNullOrEmpty(manaCost)) return;
            if (new ManaCostConverter().Convert(manaCost, typeof(object), null!, CultureInfo.CurrentCulture) is not Panel panel)
                return;
            var pieces = new List<UIElement>();
            foreach (UIElement child in panel.Children) pieces.Add(child);
            panel.Children.Clear();                                // a piece can only have one parent
            foreach (var piece in pieces) target.Items.Add(piece);
        }

        public object? Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is not string manaCost ||
                string.IsNullOrEmpty(manaCost))
                return null;

            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var token in ParseManaSymbols(manaCost))
            {
                string key = token
                    .Replace("{", "").Replace("}", "")
                    .Replace("/", "-");
                string path = Path.Combine(ManaFolder, $"{key}.png");

                // No symbol file (or it can't be drawn): the symbol as text, e.g. "{W/P}".
                var source = File.Exists(path) ? CachedSymbol(path) : null;
                if (source != null)
                {
                    panel.Children.Add(new Image
                    {
                        Source = source,
                        Width = 14,
                        Height = 14,
                        Margin = new Thickness(1, 0, 1, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = token
                    });
                }
                else
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = token,
                        FontSize = 10,
                        Margin = new Thickness(1, 0, 1, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.Gray
                    });
                }
            }

            return panel;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();

        private static List<string> ParseManaSymbols(string cost)
        {
            var list = new List<string>();
            int i = 0;
            while (i < cost.Length)
            {
                if (cost[i] == '{')
                {
                    int end = cost.IndexOf('}', i);
                    if (end > i)
                    {
                        list.Add(cost.Substring(i, end - i + 1));
                        i = end + 1;
                        continue;
                    }
                }
                i++;
            }
            return list;
        }

        // Each symbol is read and drawn once, then shared by every row (the grid
        // asks for every visible row on each scroll). Misses aren't kept, so a
        // symbol downloaded later shows up.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _symbols =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Forget the drawn symbols (after Update Database downloads new ones).</summary>
        public static void ClearCache() => _symbols.Clear();

        private static ImageSource? CachedSymbol(string path)
        {
            if (_symbols.TryGetValue(path, out var hit)) return hit;
            var source = LoadImageSource(path);
            if (source == null) return null;
            if (source.CanFreeze && !source.IsFrozen) source.Freeze();   // shareable between rows and threads
            _symbols[path] = source;
            return source;
        }

        private static ImageSource? LoadImageSource(string path)
        {
            try
            {
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);

                string h = System.Text.Encoding.UTF8.GetString(header);

                if (h.TrimStart().StartsWith("<"))
                {
                    // SVG
                    var settings = new WpfDrawingSettings
                    {
                        IncludeRuntime = true,
                        TextAsGeometry = false
                    };
                    var converter = new FileSvgConverter(settings);
                    converter.Convert(path);
                    if (converter.Drawing != null)
                        return new DrawingImage(converter.Drawing);
                    return null;
                }

                // PNG
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
    }
}