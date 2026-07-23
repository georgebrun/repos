using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BreakersOfE.Converters
{
    /// <summary>
    /// Converts a bool to Visibility.
    /// true → Visible, false → Collapsed
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts a string to Visibility.
    /// Non-empty string → Visible, empty/null → Collapsed
    /// Used for conditionally showing warning text.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
