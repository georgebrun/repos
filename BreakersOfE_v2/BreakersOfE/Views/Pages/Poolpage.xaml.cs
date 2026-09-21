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
            PoolGrid.SelectionChanged += PoolGrid_SelectionChanged;
        }

        public void LoadPool(string tag)
        {
            _vm.LoadPool(tag);
            ResetSearch();
            ClearDetail();

            // Apply default multi-level sort: Name → Edition → Collector Number
            _lastSortProp = "Name";
            _lastSortAsc = true;
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view != null)
                view.CustomSort = new PoolSortComparer(
                    "Name", true, _editionChronological);
        }

        // ══════════════════════════════════════════════════════════════════
        // LEFT DETAIL PANEL — populated on row selection
        // ══════════════════════════════════════════════════════════════════
        private bool _showingBack = false;

        private void PoolGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _showingBack = false;
            if (PoolGrid.SelectedItem == null) { ClearDetail(); return; }

            // Use reflection so this works for PoolCard, TokenCard, etc.
            var item = PoolGrid.SelectedItem;
            string Get(string prop) =>
                item.GetType().GetProperty(prop)?.GetValue(item)?.ToString() ?? "";

            DetailName.Text = Get("Name");
            DetailType.Text = Get("TypeLine");
            DetailSet.Text = $"{Get("SetName")} ({Get("SetCode")})";
            DetailCollectorNumber.Text = Get("CollectorNumber");
            DetailRarity.Text = Get("Rarity");
            DetailArtist.Text = Get("Artist");

            // Oracle + flavor
            DetailOracle.Text = Get("OracleText");
            DetailFlavor.Text = Get("FlavorText");

            // P/T or Loyalty
            string p = Get("Power"), t = Get("Toughness"), loy = Get("LoyaltyOrDefense");
            if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(t))
            {
                DetailPTLabel.Text = "POWER / TOUGHNESS";
                DetailPT.Text = $"{p}/{t}";
                DetailPTLabel.Visibility = Visibility.Visible;
                DetailPT.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(loy))
            {
                DetailPTLabel.Text = "LOYALTY / DEFENSE";
                DetailPT.Text = loy;
                DetailPTLabel.Visibility = Visibility.Visible;
                DetailPT.Visibility = Visibility.Visible;
            }
            else
            {
                DetailPTLabel.Visibility = Visibility.Collapsed;
                DetailPT.Visibility = Visibility.Collapsed;
            }

            // Finishes
            bool isFoil = bool.TryParse(Get("IsFoil"), out var f) && f;
            bool isNonFoil = bool.TryParse(Get("IsNonFoil"), out var nf) && nf;
            var finishes = new List<string>();
            if (isFoil) finishes.Add("Foil");
            if (isNonFoil) finishes.Add("Non-Foil");
            DetailFinishes.Text = finishes.Count > 0
                ? string.Join(" · ", finishes) : "Unknown";

            // Prices
            string usd = Get("PriceUsd") is string pu && !string.IsNullOrEmpty(pu) ? $"USD:  ${pu}" : "";
            string foilP = Get("PriceUsdFoil") is string pf && !string.IsNullOrEmpty(pf) ? $"USD Foil: ${pf}" : "";
            DetailPrices.Text = string.Join("\n",
                new[] { usd, foilP }.Where(s => !string.IsNullOrEmpty(s)));

            // Mana cost symbols
            DetailManaCost.Items.Clear();
            string manaCost = Get("ManaCost");
            if (!string.IsNullOrEmpty(manaCost))
            {
                var converter = new Services.ManaCostConverter();
                var symbols = converter.Convert(manaCost, typeof(object), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                if (symbols is System.Collections.IEnumerable items)
                    foreach (var sym in items)
                        DetailManaCost.Items.Add(sym);
            }

            // Set symbol (rarity-tinted)
            string setSymbolPath = Get("SetSymbolPath");
            string rarity = Get("Rarity");
            if (!string.IsNullOrEmpty(setSymbolPath))
            {
                var converter = new Services.ImageSourceConverter();
                var img = converter.Convert(
                    new object[] { setSymbolPath, rarity },
                    typeof(ImageSource), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                DetailSetSymbol.Source = img as ImageSource;
            }
            else
            {
                DetailSetSymbol.Source = null;
            }

            // Card image
            LoadCardImage(Get("ImageNormalUrl"), Get("LocalImagePath"));

            // Back face button
            string backUrl = Get("ImageBackUrl");
            BtnShowBackFace.Visibility = !string.IsNullOrEmpty(backUrl)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadCardImage(string url, string localPath)
        {
            try
            {
                // Try local first
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    DetailCardImage.Source = bmp;
                    return;
                }

                // Fall back to URL
                if (!string.IsNullOrEmpty(url))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(url, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    DetailCardImage.Source = bmp;
                }
                else
                {
                    DetailCardImage.Source = null;
                }
            }
            catch { DetailCardImage.Source = null; }
        }

        private void BtnShowBackFace_Click(object sender, RoutedEventArgs e)
        {
            if (PoolGrid.SelectedItem == null) return;
            var item = PoolGrid.SelectedItem;
            string Get(string prop) =>
                item.GetType().GetProperty(prop)?.GetValue(item)?.ToString() ?? "";

            _showingBack = !_showingBack;
            if (_showingBack)
            {
                LoadCardImage(Get("ImageBackUrl"), Get("LocalImageBackPath"));
                BtnShowBackFace.Content = "🔄 Show Front Face";
            }
            else
            {
                LoadCardImage(Get("ImageNormalUrl"), Get("LocalImagePath"));
                BtnShowBackFace.Content = "🔄 Show Back Face";
            }
        }

        private void ClearDetail()
        {
            DetailCardImage.Source = null;
            DetailName.Text = "";
            DetailType.Text = "";
            DetailSet.Text = "";
            DetailSetSymbol.Source = null;
            DetailCollectorNumber.Text = "";
            DetailRarity.Text = "";
            DetailPT.Text = "";
            DetailPTLabel.Visibility = Visibility.Collapsed;
            DetailPT.Visibility = Visibility.Collapsed;
            DetailOracle.Text = "";
            DetailFlavor.Text = "";
            DetailArtist.Text = "";
            DetailFinishes.Text = "";
            DetailPrices.Text = "";
            DetailManaCost.Items.Clear();
            BtnShowBackFace.Visibility = Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD DETAIL POPUP — double-click grid row or detail image
        // ══════════════════════════════════════════════════════════════════
        private CardDetailWindow? _cardDetailWindow;

        private void PoolGrid_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PoolGrid.SelectedItem == null) return;
            OpenCardDetailPopup(PoolGrid.SelectedItem);
        }

        private void DetailImage_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            if (PoolGrid.SelectedItem == null) return;
            OpenCardDetailPopup(PoolGrid.SelectedItem);
        }

        private void OpenCardDetailPopup(object card)
        {
            if (_cardDetailWindow != null && _cardDetailWindow.IsVisible)
            {
                _cardDetailWindow.Close();
                _cardDetailWindow = null;
                return;
            }

            _cardDetailWindow = new CardDetailWindow(card, Window.GetWindow(this));
            _cardDetailWindow.Closed += (s, ev) => _cardDetailWindow = null;
            _cardDetailWindow.Show();
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

        // ── Edition sort order toggle ──────────────────────────────────
        private bool _editionChronological = false;

        private void BtnEditionOrder_Click(object sender, RoutedEventArgs e)
        {
            _editionChronological = !_editionChronological;
            if (sender is Button btn)
                btn.Content = _editionChronological ? "Edition: Chrono" : "Edition: A→Z";

            // Re-sort with current settings
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view?.CustomSort is PoolSortComparer existing)
            {
                // Re-apply with toggled edition order
                view.CustomSort = new PoolSortComparer(
                    _lastSortProp ?? "Name", _lastSortAsc, _editionChronological);
            }
            else
            {
                // Default sort with new edition order
                if (view != null)
                    view.CustomSort = new PoolSortComparer(
                        "Name", true, _editionChronological);
            }
        }

        private string? _lastSortProp;
        private bool _lastSortAsc = true;

        private void SortColumn(string propName, bool ascending)
        {
            _lastSortProp = propName;
            _lastSortAsc = ascending;

            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view == null) return;

            view.CustomSort = new PoolSortComparer(
                propName, ascending, _editionChronological);
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

            // Reset to default multi-level sort (Name → Edition → Collector Number)
            _lastSortProp = "Name";
            _lastSortAsc = true;
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view != null)
                view.CustomSort = new PoolSortComparer(
                    "Name", true, _editionChronological);

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

    /// <summary>
    /// Multi-level sort for the pool grid. Primary sort = whatever column the
    /// user clicked. Sub-sorts: Edition (alpha or chrono) → Collector Number.
    /// When no user sort is active, default is Name → Edition → Collector Number.
    /// </summary>
    public class PoolSortComparer : System.Collections.IComparer
    {
        private readonly string _primaryProp;
        private readonly bool _primaryAsc;
        private readonly bool _editionChronological;

        // Cached reflection
        private System.Reflection.PropertyInfo? _primaryPi;
        private System.Reflection.PropertyInfo? _setCodePi;
        private System.Reflection.PropertyInfo? _releasedAtPi;
        private System.Reflection.PropertyInfo? _collNumSortPi;

        public PoolSortComparer(string primaryProp, bool ascending,
            bool editionChronological = false)
        {
            _primaryProp = primaryProp;
            _primaryAsc = ascending;
            _editionChronological = editionChronological;
        }

        public int Compare(object? x, object? y)
        {
            if (x == null || y == null) return 0;

            // Cache property info on first call
            var type = x.GetType();
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase;
            _primaryPi ??= type.GetProperty(_primaryProp, flags);
            _setCodePi ??= type.GetProperty("SetCode", flags);
            _releasedAtPi ??= type.GetProperty("ReleasedAt", flags);
            _collNumSortPi ??= type.GetProperty("CollectorNumberSort", flags);

            // Primary sort
            string a = _primaryPi?.GetValue(x)?.ToString() ?? "";
            string b = _primaryPi?.GetValue(y)?.ToString() ?? "";
            int cmp = ColumnFilterState.CompareNatural(a, b);
            if (!_primaryAsc) cmp = -cmp;
            if (cmp != 0) return cmp;

            // Sub-sort 1: Edition
            if (_primaryProp != "SetCode" && _primaryProp != "ReleasedAt")
            {
                if (_editionChronological)
                {
                    string ra = _releasedAtPi?.GetValue(x)?.ToString() ?? "";
                    string rb = _releasedAtPi?.GetValue(y)?.ToString() ?? "";
                    cmp = string.Compare(ra, rb, StringComparison.Ordinal); // ISO dates sort naturally
                    if (cmp != 0) return cmp;
                }
                else
                {
                    string sa = _setCodePi?.GetValue(x)?.ToString() ?? "";
                    string sb = _setCodePi?.GetValue(y)?.ToString() ?? "";
                    cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }

            // Sub-sort 2: Collector Number (numeric)
            if (_primaryProp != "CollectorNumber" && _primaryProp != "CollectorNumberSort")
            {
                double na = (_collNumSortPi?.GetValue(x) as double?) ?? 9999;
                double nb = (_collNumSortPi?.GetValue(y) as double?) ?? 9999;
                cmp = na.CompareTo(nb);
                if (cmp != 0) return cmp;
            }

            return 0;
        }
    }
}