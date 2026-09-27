using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BreakersOfE.Filtering;
using BreakersOfE.ViewModels;


namespace BreakersOfE.Views.Pages
{
    // Column filters: funnel buttons, the filter popup and funnel icons.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // FUNNEL → open the filter popup for that column
        // ══════════════════════════════════════════════════════════════════
        private void Funnel_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn) return;

            string columnName = btn.Tag?.ToString() ?? string.Empty;
            if (!ColumnToProperty.TryGetValue(columnName, out var propName))
                return;

            var state = _vm.Filters.GetOrCreate(columnName, propName);
            var values = _vm.DistinctValuesFor(columnName, propName);

            _activePopup?.Close();

            var popup = new Views.ColumnFilterPopup(columnName, propName, values, state)
            {
                Owner = Window.GetWindow(this)
            };

            // Position under the funnel, kept inside the app window so the
            // right-hand columns' filters aren't cut off at the edge.
            PlaceInsideWindow(popup, btn);

            popup.SortRequested += (_, ascending) => SortColumn(propName, ascending);

            popup.FilterChanged += (_, __) =>
            {
                _vm.ApplyFilters();
                UpdateFunnelIcon(btn, state.IsActive);
                ResetSearch();
            };

            _activePopup = popup;
            popup.Show();
        }

        /// <summary>Funnel icons reflect the actual filter state of each column.</summary>
        private void RefreshFunnelIcons()
        {
            foreach (var btn in FindVisualChildren<Button>(PoolGrid)
                     .Where(b => b.Name == "FunnelButton"))
            {
                string col = btn.Tag?.ToString() ?? "";
                UpdateFunnelIcon(btn, _vm.Filters.Get(col)?.IsActive == true);
            }
        }
    }
}
