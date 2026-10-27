using System;
using System.Globalization;
using System.Windows.Data;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// The Name column: a two-sided token row (or a deck's token line) shows
    /// its other side — "Treasure // Angel"; every other row its own name.
    /// The row's Name itself never changes (sorting, filters and exports use it).
    /// </summary>
    public sealed class NameShownConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try { return CollectionEditService.NameShown(value); }
            catch { return value?.GetType().GetProperty("Name")?.GetValue(value) as string ?? ""; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
