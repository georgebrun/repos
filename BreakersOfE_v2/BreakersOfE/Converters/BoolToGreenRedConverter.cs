using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Converts bool to green (legal) or red (not legal) brush for legality pills.
    /// </summary>
    public class BoolToGreenRedConverter : IValueConverter
    {
        private static readonly SolidColorBrush Green =
            new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // dark green
        private static readonly SolidColorBrush Red =
            new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); // dark red
        private static readonly SolidColorBrush Grey =
            new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)); // grey for N/A

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Green : Red;
            return Grey; // null / non-bool = grey (no legality data)
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}