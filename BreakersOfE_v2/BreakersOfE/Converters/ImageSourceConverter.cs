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
    public class ImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is not string path ||
                string.IsNullOrEmpty(path)) return null;

            try
            {
                // Check if SVG by content
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);

                string h = System.Text.Encoding.UTF8
                    .GetString(header);

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
                    if (converter.Drawing != null)
                        return new DrawingImage(converter.Drawing);
                    return null;
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

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}