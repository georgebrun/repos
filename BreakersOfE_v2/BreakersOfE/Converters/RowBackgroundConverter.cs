using System.Globalization;
using System.Windows.Data;
using BreakersOfE.Services;

namespace BreakersOfE.Converters
{
    /// <summary>
    /// Card-table row fill from the row's place on screen: values = [the row's
    /// card, the row's AlternationIndex]. Alternates white / tint however the
    /// table is sorted or filtered.
    /// </summary>
    public sealed class RowBackgroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            object? item = values.Length > 0 ? values[0] : null;
            int alternation = values.Length > 1 && values[1] is int i ? i : 0;
            return CardColorService.RowBackground(item, alternation);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
