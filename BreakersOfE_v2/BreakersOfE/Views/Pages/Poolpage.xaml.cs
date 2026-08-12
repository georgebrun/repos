using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BreakersOfE.Filtering;
using BreakersOfE.ViewModels;

namespace BreakersOfE.Views.Pages
{
    public partial class PoolPage : Page
    {
        private readonly PoolViewModel _vm;
        private Views.ColumnFilterPopup? _activePopup;

        // Map column display name → bound property name (for reflection filtering)
        private static readonly Dictionary<string, string> ColumnToProperty = new()
        {
            ["Name"] = "Name",
            ["Edition"] = "SetCode",
            ["Edition Name"] = "SetName",
            ["Type"] = "TypeLine",
            ["Rarity"] = "RarityCode",
            ["P/T"] = "PowerToughness",
            ["USD"] = "PriceUsdDisplay",
            ["Foil $"] = "PriceUsdFoilDisplay",
            ["Text"] = "OracleText",
            ["Artist"] = "Artist",
            ["No."] = "CollectorNumber",
        };

        // ── Search state ────────────────────────────────────────────────
        private string _lastSearchTerm = string.Empty;
        private int _searchMatchIndex = -1;
        private readonly List<int> _searchMatchIndices = new();

        public PoolPage()
        {
            InitializeComponent();
            _vm = (PoolViewModel)DataContext;
            Loaded += PoolPage_Loaded;
        }

        public void LoadPool(string tag)
        {
            _vm.LoadPool(tag);
            ResetSearch();
        }

        private void PoolPage_Loaded(object sender, RoutedEventArgs e)
            => DisableParentScrollViewers();

        // NavigationView wraps pages in a ScrollViewer → infinite height →
        // virtualization defeated → freeze. Kill it.
        private void DisableParentScrollViewers()
        {
            DependencyObject current = this;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer sv)
                {
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    break;
                }
            }
        }

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

            // Position under the funnel
            var pt = btn.PointToScreen(new Point(0, btn.ActualHeight));
            popup.Left = pt.X;
            popup.Top = pt.Y;

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

        private void SortColumn(string propName, bool ascending)
        {
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view == null) return;

            view.CustomSort = new NaturalComparer(propName, ascending);
        }

        private void UpdateFunnelIcon(Button funnelButton, bool active)
        {
            if (funnelButton.Content is TextBlock icon)
            {
                icon.Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA0, 0xFF)) // accent
                    : new SolidColorBrush(Color.FromRgb(0x9F, 0x9F, 0x9F));
                icon.Text = active ? "\uE71C" : "\uE71C"; // same glyph, color signals state
            }
        }

        private void ClearAllFilters_Click(object sender, RoutedEventArgs e)
        {
            _vm.Filters.ClearAll();
            _vm.ApplyFilters();

            // Reset any custom sort
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view != null) view.CustomSort = null;

            // Reset all funnel icons to inactive
            foreach (var btn in FindVisualChildren<Button>(PoolGrid)
                     .Where(b => b.Name == "FunnelButton"))
            {
                UpdateFunnelIcon(btn, false);
            }

            ResetSearch();
        }

        // ══════════════════════════════════════════════════════════════════
        // SEARCH — begins-with, scroll into view, cycle. Never hides rows.
        // ══════════════════════════════════════════════════════════════════
        private void ResetSearch()
        {
            _lastSearchTerm = string.Empty;
            _searchMatchIndex = -1;
            _searchMatchIndices.Clear();
            if (SearchBox != null) SearchBox.Text = string.Empty;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(text) || PoolGrid.Items.Count == 0)
            {
                _lastSearchTerm = string.Empty;
                _searchMatchIndex = -1;
                _searchMatchIndices.Clear();
                return;
            }

            if (!text.Equals(_lastSearchTerm, StringComparison.OrdinalIgnoreCase))
            {
                _lastSearchTerm = text;
                _searchMatchIndices.Clear();
                _searchMatchIndex = -1;

                for (int i = 0; i < PoolGrid.Items.Count; i++)
                {
                    var nameProp = PoolGrid.Items[i]?.GetType().GetProperty("Name");
                    var name = nameProp?.GetValue(PoolGrid.Items[i]) as string;
                    if (!string.IsNullOrEmpty(name) &&
                        name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _searchMatchIndices.Add(i);
                }
            }

            if (_searchMatchIndices.Count == 0) return;

            _searchMatchIndex++;
            if (_searchMatchIndex >= _searchMatchIndices.Count) _searchMatchIndex = 0;

            var matchItem = PoolGrid.Items[_searchMatchIndices[_searchMatchIndex]];
            PoolGrid.ScrollIntoView(matchItem);
            PoolGrid.SelectedItem = matchItem;
        }

        // ── Visual tree helper ──────────────────────────────────────────
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var d in FindVisualChildren<T>(child)) yield return d;
            }
        }
    }

    /// <summary>Numeric-aware comparer for grid sorting (1,2,10 not 1,10,2).</summary>
    public class NaturalComparer : System.Collections.IComparer
    {
        private readonly string _prop;
        private readonly bool _asc;
        private System.Reflection.PropertyInfo? _pi;

        public NaturalComparer(string prop, bool asc) { _prop = prop; _asc = asc; }

        public int Compare(object? x, object? y)
        {
            if (x == null || y == null) return 0;
            _pi ??= x.GetType().GetProperty(_prop,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);
            string a = _pi?.GetValue(x)?.ToString() ?? string.Empty;
            string b = _pi?.GetValue(y)?.ToString() ?? string.Empty;
            int r = ColumnFilterState.CompareNatural(a, b);
            return _asc ? r : -r;
        }
    }
}