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
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Items")
                    Dispatcher.BeginInvoke(new Action(OnItemsChanged),
                        System.Windows.Threading.DispatcherPriority.Loaded);
            };
            Loaded += PoolPage_Loaded;
            PoolGrid.SelectionChanged += PoolGrid_SelectionChanged;

            // Gallery view: selection goes through the grid (one selection,
            // two views); double-click opens the detail window.
            Gallery.CardClicked += card => PoolGrid.SelectedItem = card;
            Gallery.CardOpened += OpenCardDetailPopup;

            // Grid / Gallery switch above the left navigation (app-wide).
            // Listen only while this page is on screen.
            Loaded += (_, _) => Services.CardViewModeService.ModeChanged += OnCardViewModeChanged;
            Unloaded += (_, _) => Services.CardViewModeService.ModeChanged -= OnCardViewModeChanged;
        }

        private string _currentTag = "";
        private bool _inSetsContext;          // true while the "Sets" nav item is active

        /// <summary>Card Pool nav items (Cards, Tokens, …): always a card view.</summary>
        public void LoadPool(string tag)
        {
            _inSetsContext = false;
            LoadPoolCore(tag);
            SetViewMode(SwitchMode);
        }

        /// <summary>
        /// "Sets" nav item: the set browser over the main Cards pool. Reuses
        /// the Cards pool if it's already loaded (no 100K-card reload).
        /// </summary>
        public void ShowSets()
        {
            _inSetsContext = true;
            if (_currentTag != "Cards" || _vm.AllRows.Count == 0)
            {
                LoadPoolCore("Cards");          // tiles build when the data arrives
            }
            else if (_vm.Filters.HasActiveFilters)
            {
                // Coming back from a set: drop its filter so card views start whole.
                _vm.Filters.ClearAll();
                _vm.ApplyFilters();
                RefreshFunnelIcons();
            }
            ResetSearch();
            SetViewMode(PoolViewMode.Sets);
        }

        private void BtnBackToSets_Click(object sender, RoutedEventArgs e) => ShowSets();

        private void LoadPoolCore(string tag)
        {
            _currentTag = tag;
            _vm.LoadPool(tag);
            ResetSearch();
            ClearDetail();
            Gallery.Reset();         // new card objects for this pool type
            _setTiles = null;        // set tiles come from the new pool too
            // Sort is applied in OnItemsChanged when the binding propagates.
        }

        // Called when the ViewModel's Items property changes (data loaded).
        private void OnItemsChanged()
        {
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view == null) return;

            _lastSortProp = _lastSortProp ?? "Name";
            view.CustomSort = new PoolSortComparer(
                _lastSortProp, _lastSortAsc, _editionChronological);

            // Show sort indicator on the active column header
            foreach (var col in PoolGrid.Columns)
            {
                if (col.SortMemberPath == _lastSortProp ||
                    col.Header?.ToString() == _lastSortProp)
                    col.SortDirection = _lastSortAsc
                        ? ListSortDirection.Ascending
                        : ListSortDirection.Descending;
                else
                    col.SortDirection = null;
            }

            // Pre-build name cache for fast search
            RebuildSearchIndex();

            // Gallery follows the same data + order
            RebuildGalleryIfVisible();

            // Set browser: first time data is available for this pool
            if (_viewMode == PoolViewMode.Sets && _setTiles == null)
            {
                BuildSetTilesIfNeeded();
                RebuildSetRows();
                _vm.Title = "Sets";
                _vm.StatusText = $"{_setTiles!.Count:N0} sets";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // LEFT DETAIL PANEL — shared CardDetailPanel control
        // ══════════════════════════════════════════════════════════════════
        private void PoolGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Gallery.SelectCard(PoolGrid.SelectedItem);
            Detail.ShowCard(PoolGrid.SelectedItem);
        }

        private void ClearDetail() => Detail.ShowCard(null);

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

        private void Detail_ImageDoubleClicked(object? sender, EventArgs e)
        {
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

            // Build items list and find index for prev/next navigation
            var items = new List<object>();
            int idx = -1;
            for (int i = 0; i < PoolGrid.Items.Count; i++)
            {
                var item = PoolGrid.Items[i];
                if (item != null) items.Add(item);
                if (item == card) idx = items.Count - 1;
            }

            _cardDetailWindow = new CardDetailWindow(
                card, Window.GetWindow(this), items, idx);
            _cardDetailWindow.CardChanged += newCard =>
            {
                PoolGrid.SelectedItem = newCard;
                if (_galleryMode) Gallery.ScrollToCard(newCard, toTop: false);
                else PoolGrid.ScrollIntoView(newCard);
            };
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
        private bool _editionChronological = true;

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
            OnSortChanged();
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
            OnSortChanged();
        }

        /// <summary>
        /// After any re-sort: search index and gallery must follow the new order.
        /// </summary>
        private void OnSortChanged()
        {
            RebuildSearchIndex();
            RebuildGalleryIfVisible();
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
            OnSortChanged();

            // Viewing a set from the set browser: the set filter is gone now.
            if (_inSetsContext) _vm.Title = "Sets — All Cards";
        }

        // ══════════════════════════════════════════════════════════════════
        // SEARCH — begins-with, scroll to top, debounced. Never hides rows.
        // ══════════════════════════════════════════════════════════════════
        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private List<string> _searchNames = new();

        private void RebuildSearchIndex()
        {
            _searchNames.Clear();
            if (PoolGrid.Items.Count == 0) return;
            System.Reflection.PropertyInfo? namePi = null;
            for (int i = 0; i < PoolGrid.Items.Count; i++)
            {
                var item = PoolGrid.Items[i];
                if (item == null) { _searchNames.Add(""); continue; }
                namePi ??= item.GetType().GetProperty("Name");
                _searchNames.Add(namePi?.GetValue(item) as string ?? "");
            }
        }

        private void ResetSearch()
        {
            _lastSearchTerm = string.Empty;
            _searchMatchIndex = -1;
            _searchMatchIndices.Clear();
            if (SearchBox != null) SearchBox.Text = string.Empty;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce — wait 150ms after last keystroke
            _searchTimer?.Stop();
            _searchTimer ??= new System.Windows.Threading.DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchTimer.Tick -= SearchTimer_Tick;
            _searchTimer.Tick += SearchTimer_Tick;
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer?.Stop();
            ExecuteSearch();
        }

        private void ExecuteSearch()
        {
            var text = SearchBox.Text?.Trim();

            // Set browser: jump to a set by name (never hides anything)
            if (_viewMode == PoolViewMode.Sets)
            {
                if (!string.IsNullOrEmpty(text)) SearchSets(text);
                return;
            }
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

                // Use cached names — no reflection per keystroke
                for (int i = 0; i < _searchNames.Count; i++)
                {
                    if (_searchNames[i].StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _searchMatchIndices.Add(i);
                }
            }

            if (_searchMatchIndices.Count == 0) return;

            _searchMatchIndex++;
            if (_searchMatchIndex >= _searchMatchIndices.Count) _searchMatchIndex = 0;

            var matchItem = PoolGrid.Items[_searchMatchIndices[_searchMatchIndex]];
            PoolGrid.SelectedItem = matchItem;

            // Gallery mode: put the match's row at the top of the gallery
            if (_galleryMode)
            {
                Gallery.ScrollToCard(matchItem, toTop: true);
                return;
            }

            // Scroll the match to the TOP of the visible area.
            // Two-step trick: first scroll far past the target so it's above
            // the viewport, then ScrollIntoView scrolls UP to put it at top.
            PoolGrid.UpdateLayout();
            int matchRow = _searchMatchIndices[_searchMatchIndex];
            int jumpTarget = Math.Min(matchRow + 50, PoolGrid.Items.Count - 1);
            PoolGrid.ScrollIntoView(PoolGrid.Items[jumpTarget]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(matchItem);
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD GALLERY — its own view (Views/Controls/GalleryView). This page
        // feeds it the grid's sorted/filtered cards and shares the selection.
        // ══════════════════════════════════════════════════════════════════
        private bool _galleryMode => _viewMode == PoolViewMode.Gallery;

        // ── View modes: Grid, Gallery, Sets ─────────────────────────────
        private enum PoolViewMode { Grid, Gallery, Sets }
        private PoolViewMode _viewMode = PoolViewMode.Grid;

        /// <summary>The card view the switch above the navigation is set to.</summary>
        private static PoolViewMode SwitchMode =>
            Services.CardViewModeService.Mode == Services.CardViewMode.Gallery
                ? PoolViewMode.Gallery : PoolViewMode.Grid;

        /// <summary>
        /// Switch flipped. In the set browser nothing changes on screen; the
        /// switch just decides what opening a set shows.
        /// </summary>
        private void OnCardViewModeChanged(Services.CardViewMode _)
        {
            if (_viewMode != PoolViewMode.Sets) SetViewMode(SwitchMode);
        }

        private void SetViewMode(PoolViewMode mode)
        {
            var previous = _viewMode;
            var selected = PoolGrid.SelectedItem;

            _viewMode = mode;

            PoolGrid.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            Gallery.Visibility = mode == PoolViewMode.Gallery ? Visibility.Visible : Visibility.Collapsed;
            SetList.Visibility = mode == PoolViewMode.Sets ? Visibility.Visible : Visibility.Collapsed;

            // Header buttons: Edition sort only matters in the grid; the set
            // browser uses none of the card-view buttons.
            bool cardView = mode != PoolViewMode.Sets;
            BtnClearFilters.Visibility = cardView ? Visibility.Visible : Visibility.Collapsed;
            BtnEditionOrder.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            BtnBackToSets.Visibility = cardView && _inSetsContext ? Visibility.Visible : Visibility.Collapsed;

            if (mode == PoolViewMode.Sets)
            {
                _vm.Title = "Sets";
                if (_setTiles != null) _vm.StatusText = $"{_setTiles.Count:N0} sets";
            }

            // Wait for layout so the new view has a real width to size rows.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (mode)
                {
                    case PoolViewMode.Gallery:
                        Gallery.Show(GridOrder(), selected);
                        if (selected != null) Gallery.ScrollToCard(selected, toTop: true);
                        break;
                    case PoolViewMode.Grid:
                        if (selected != null && previous != PoolViewMode.Grid)
                            FocusGridRow(selected);
                        break;
                    case PoolViewMode.Sets:
                        if (_vm.AllRows.Count == 0) break;   // still loading; OnItemsChanged builds
                        BuildSetTilesIfNeeded();
                        RebuildSetRows();
                        _vm.StatusText = $"{_setTiles!.Count:N0} sets";
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════════════════════
        // SET BROWSER — tiles built from the in-memory pool, grouped by set
        // type, newest first. Click a tile → Edition filter + gallery.
        // ══════════════════════════════════════════════════════════════════
        private List<SetTile>? _setTiles;                            // per pool load
        private List<(string section, List<SetTile> tiles)> _setGroups = new();
        private List<SetBrowserRow> _setRows = new();
        private int _setsPerRow = 1;
        private SetTile? _setHighlighted;

        private const double SetTileWidth = 300;   // matches XAML
        private const double SetTileMargin = 8;

        private void BuildSetTilesIfNeeded()
        {
            if (_setTiles != null) return;

            var bySet = new Dictionary<string, SetTile>(StringComparer.OrdinalIgnoreCase);
            System.Reflection.PropertyInfo? pCode = null, pName = null, pType = null,
                                            pDate = null, pUsd = null;
            Type? lastType = null;

            foreach (var card in _vm.AllRows)
            {
                if (card == null) continue;
                var t = card.GetType();
                if (t != lastType)
                {
                    lastType = t;
                    pCode = t.GetProperty("SetCode");
                    pName = t.GetProperty("SetName");
                    pType = t.GetProperty("SetType");
                    pDate = t.GetProperty("ReleasedAt");
                    pUsd = t.GetProperty("PriceUsd");
                }

                string code = pCode?.GetValue(card) as string ?? "";
                if (string.IsNullOrWhiteSpace(code)) continue;
                string date = pDate?.GetValue(card) as string ?? "";

                if (!bySet.TryGetValue(code, out var tile))
                {
                    string symbol = System.IO.Path.Combine(
                        Services.AppFolderService.SetSymbolsFolder, $"{code.ToLower()}.png");
                    tile = new SetTile
                    {
                        Code = code,
                        Name = pName?.GetValue(card) as string ?? code,
                        SetType = pType?.GetValue(card) as string ?? "",
                        SymbolPath = File.Exists(symbol) ? symbol : "",
                        ReleasedAt = date,
                    };
                    bySet[code] = tile;
                }

                tile.CardCount++;
                if (pUsd?.GetValue(card) is decimal usd) tile.TotalValue += usd;
                // A set's date = its earliest card (ISO dates compare as text).
                if (!string.IsNullOrEmpty(date) &&
                    (string.IsNullOrEmpty(tile.ReleasedAt) ||
                     string.CompareOrdinal(date, tile.ReleasedAt) < 0))
                    tile.ReleasedAt = date;
            }

            _setTiles = bySet.Values.ToList();
            _setGroups = _setTiles
                .GroupBy(t => SetGrouping.SectionFor(t.SetType))
                .OrderBy(g => SetGrouping.OrderOf(g.Key))
                .Select(g => (section: g.Key, tiles: g.OrderByDescending(t => t.ReleasedAt, StringComparer.Ordinal)
                                      .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                      .ToList()))
                .ToList();
        }

        /// <summary>Flatten sections into header rows + rows of N tiles.</summary>
        private void RebuildSetRows()
        {
            if (SetList == null) return;

            double avail = SetList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            _setsPerRow = perRow;

            var rows = new List<SetBrowserRow>();
            foreach (var (section, tiles) in _setGroups)
            {
                rows.Add(new SetBrowserRow
                {
                    IsHeader = true,
                    HeaderText = section,
                    HeaderCount = tiles.Count.ToString("N0"),
                });
                for (int i = 0; i < tiles.Count; i += perRow)
                    rows.Add(new SetBrowserRow
                    {
                        Tiles = tiles.GetRange(i, Math.Min(perRow, tiles.Count - i))
                    });
            }
            _setRows = rows;
            SetList.ItemsSource = rows;
        }

        private void SetList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode != PoolViewMode.Sets || !e.WidthChanged || _setGroups.Count == 0) return;
            double avail = SetList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            if (perRow != _setsPerRow) RebuildSetRows();
        }

        private void SetTile_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SetTile tile)
                OpenSet(tile);
        }

        /// <summary>
        /// Show one whole set: clear other filters, filter Edition to this set
        /// (a normal filter — Clear All Filters removes it), open the gallery.
        /// </summary>
        private void OpenSet(SetTile tile)
        {
            _vm.Filters.ClearAll();
            var f = _vm.Filters.GetOrCreate("Edition", ColumnToProperty["Edition"]);
            f.SelectedValues.Clear();
            f.SelectedValues.Add(tile.Code);
            f.AllSelected = false;

            PoolGrid.SelectedItem = null;
            ResetSearch();
            _vm.ApplyFilters();
            RefreshFunnelIcons();
            SetViewMode(SwitchMode);          // grid or gallery, per the switch
            _vm.Title = $"Sets — {tile.Name}";
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

        /// <summary>Search in the set browser: jump to the first set name that begins with the text.</summary>
        private void SearchSets(string text)
        {
            for (int r = 0; r < _setRows.Count; r++)
            {
                var row = _setRows[r];
                if (row.IsHeader) continue;
                var match = row.Tiles.FirstOrDefault(t =>
                    t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;

                if (_setHighlighted != null) _setHighlighted.IsHighlighted = false;
                match.IsHighlighted = true;
                _setHighlighted = match;

                // Put the row at the top (same two-step as the grid and gallery).
                SetList.UpdateLayout();
                int jump = Math.Min(r + 10, _setRows.Count - 1);
                SetList.ScrollIntoView(_setRows[jump]);
                SetList.UpdateLayout();
                SetList.ScrollIntoView(row);
                return;
            }
        }

        /// <summary>
        /// Select a card in the grid, put its row at the top (same two-step as
        /// search), and give the row keyboard focus. Without focus the DataGrid
        /// draws selection with its faint "inactive" color, which is nearly
        /// invisible on the dark theme, and arrow keys don't start from it.
        /// </summary>
        private void FocusGridRow(object card)
        {
            PoolGrid.SelectedItem = card;

            int idx = PoolGrid.Items.IndexOf(card);
            if (idx < 0) return;

            PoolGrid.UpdateLayout();
            int jump = Math.Min(idx + 50, PoolGrid.Items.Count - 1);
            PoolGrid.ScrollIntoView(PoolGrid.Items[jump]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(card);
            PoolGrid.UpdateLayout();

            if (PoolGrid.Columns.Count > 0)
                PoolGrid.CurrentCell = new DataGridCellInfo(card, PoolGrid.Columns[0]);

            if (PoolGrid.ItemContainerGenerator.ContainerFromItem(card) is DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
            else
            {
                PoolGrid.Focus();
            }
        }

        /// <summary>Gallery follows the grid's data and order.</summary>
        private void RebuildGalleryIfVisible()
        {
            if (_galleryMode) Gallery.Show(GridOrder(), PoolGrid.SelectedItem);
        }

        /// <summary>The grid's cards in display order (sort + filters applied).</summary>
        private System.Collections.IEnumerable GridOrder() =>
            CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                ?? (System.Collections.IEnumerable)Array.Empty<object>();

        // ── Visual tree helpers ─────────────────────────────────────────
        private static T? FindVisualChild<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

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
            bool editionChronological = true)
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