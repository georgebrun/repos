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
            // Finish pill: every table (collection = row's finish, pool = foil-only)
            ["Finish"] = "FinishPill",
            // Collection-only columns (per-finish rows)
            ["Qty"] = "Quantity",
            ["Used"] = "UsedCount",
            ["Available"] = "AvailableCount",
            ["Price"] = "PriceDisplay",
            ["Value"] = "RowValueDisplay",
            ["Buy At"] = "BuyAtDisplay",
            ["Sell At"] = "SellAtDisplay",
            ["Sell At Value"] = "SellAtValueDisplay",
            ["Needed"] = "Needed",
            ["Excess"] = "Excess",
            ["Target"] = "Target",
            ["Condition"] = "Condition",
            ["Notes"] = "Notes",
            ["Storage"] = "StorageLocation",
            ["Desired"] = "Desired",
            ["Group"] = "CardGroup",
            ["Print Type"] = "PrintType",
            ["Buy"] = "BuyStatus",
            ["Sell"] = "SellStatus",
            ["Added"] = "DateAddedDisplay",
            ["Color"] = "ColorDisplay",
            ["Flavor"] = "FlavorText",
            ["Power"] = "Power",
            ["Toughness"] = "Toughness",
            ["CMC"] = "ManaValue",
            ["Row"] = "RowIndex",
            // Deck-only columns
            ["SB"] = "SideboardDisplay",
            ["Non-Foil"] = "Quantity",
            ["Foil"] = "FoilQuantity",
            ["Total"] = "TotalQuantity",
            ["Owned"] = "CollectionOwned",
            ["Free"] = "CollectionFree",
            ["Missing"] = "CollectionMissing",
            ["Wanted"] = "WantedCount",
            // Trade Binder / Want List
            ["Asking"] = "AskingPriceDisplay",
            ["Offer"] = "OfferPriceDisplay",
        };

        // ── Table kinds ─────────────────────────────────────────────────
        // Pool (Cards, Tokens, …), the collections, and Deck share one grid;
        // each kind shows its own columns. Tags: "Collection" (main
        // collection), "Coll…" (tokens, planes, … collections), "TradeBinder",
        // "WantList", "Deck", else a pool.
        private enum TableKind { Pool, Collection, SpecialCollection, TradeBinder, WantList, Deck }

        private static TableKind KindOf(string tag) => tag switch
        {
            "Collection" => TableKind.Collection,
            "TradeBinder" => TableKind.TradeBinder,
            "WantList" => TableKind.WantList,
            DeckTableTag => TableKind.Deck,
            _ when tag.StartsWith("Coll", StringComparison.Ordinal) => TableKind.SpecialCollection,
            _ => TableKind.Pool,
        };

        /// <summary>Any collection table (main, special, binder, want list).</summary>
        private static bool IsCollectionKind(TableKind k) =>
            k is TableKind.Collection or TableKind.SpecialCollection or TableKind.TradeBinder or TableKind.WantList;

        /// <summary>All open decks share one table (and one saved layout).</summary>
        private const string DeckTableTag = "Deck";

        // Columns that only some kinds have. Columns not listed are shared.
        private static readonly Dictionary<string, TableKind[]> ColumnKinds = BuildColumnKinds();

        private static Dictionary<string, TableKind[]> BuildColumnKinds()
        {
            var C = TableKind.Collection; var S = TableKind.SpecialCollection;
            var B = TableKind.TradeBinder; var W = TableKind.WantList; var D = TableKind.Deck;
            var cs = new[] { C, S };                  // main + special collections
            var csb = new[] { C, S, B };              // + trade binder
            var all = new[] { C, S, B, W };           // every collection table
            var allD = new[] { C, S, B, W, D };       // + decks
            var pd = new[] { TableKind.Pool, D };
            var d = new[] { D };
            var map = new Dictionary<string, TableKind[]>();
            foreach (var h in new[] { "Used", "Available",
                                      "Buy At", "Sell At", "Sell At Value", "Needed", "Excess", "Target",
                                      "Storage", "Desired", "Group", "Print Type", "Buy", "Sell" })
                map[h] = cs;
            foreach (var h in new[] { "Qty", "Price", "Notes", "Added" })
                map[h] = all;
            map["Condition"] = csb;
            foreach (var h in new[] { "Value", "Color", "Flavor", "Power", "Toughness", "CMC", "Row" })
                map[h] = allD;
            map["Asking"] = new[] { B };
            map["Offer"] = new[] { W };
            foreach (var h in new[] { "USD", "Foil $" })
                map[h] = pd;
            foreach (var h in new[] { "SB", "Non-Foil", "Foil", "Total", "Owned", "Free", "Missing", "Wanted" })
                map[h] = d;
            return map;
        }

        // Legality columns (one per format, collection only). Built in code
        // from LegalityInfo.Formats; listed by the Legality button, not Columns.
        private static readonly HashSet<string> LegalityHeaders = new();

        // Columns that start hidden (still available to switch on): the
        // legality formats v1 hides by default.
        private static readonly HashSet<string> DefaultHiddenColumns = new();

        static PoolPage()
        {
            foreach (var fmt in Models.LegalityInfo.Formats)
            {
                LegalityHeaders.Add(fmt.Header);
                ColumnKinds[fmt.Header] = new[] { TableKind.Collection };
                ColumnToProperty[fmt.Header] = PoolColumnFilters.LegalityPrefix + fmt.Key;
                if (!fmt.DefaultVisible) DefaultHiddenColumns.Add(fmt.Header);
            }

            // Decks: one "Legal" column = legality in the deck's own format
            // (Commander decks → commander, Standard decks → standard).
            ColumnKinds[DeckLegalHeader] = new[] { TableKind.Deck };
            ColumnToProperty[DeckLegalHeader] =
                PoolColumnFilters.LegalityPrefix + Models.LegalityAccessor.DeckFormatKey;
        }

        private const string DeckLegalHeader = "Legal";

        /// <summary>Hide the columns this kind of table doesn't have.</summary>
        private void ApplyColumnSet(TableKind kind)
        {
            foreach (var col in PoolGrid.Columns)
            {
                if (!ColumnApplies(col.Header?.ToString() ?? "", kind))
                    col.Visibility = Visibility.Collapsed;
            }
        }

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

            // Legality columns are built in code (22 formats), then every
            // column's default is remembered for the per-table layouts.
            AddLegalityColumns();

            // Per-table column layout: remember the XAML defaults, then save
            // whenever the user reorders or resizes a column.
            InitColumnLayout();

            // Collection totals row under the grid (mirrors the grid's columns).
            BuildTotalsColumns();
        }

        private string _currentTag = "";
        private bool _inSetsContext;          // true while the "Sets" nav item is active
        private bool _inDecksContext;         // true while the "Decks" nav item is active

        /// <summary>Card Pool nav items (Cards, Tokens, …): always a card view.</summary>
        public void LoadPool(string tag)
        {
            _inSetsContext = false;
            _inDecksContext = false;
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
            _inDecksContext = false;

            // The set view has its own filters (separate from Cards). Coming
            // back to the browser drops the set's Edition filter.
            _vm.UseFilters(SetsFilterKey);
            _vm.Filters.ClearAll();

            if (_currentTag != "Cards" || _vm.AllRows.Count == 0)
            {
                LoadPoolCore("Cards");          // tiles build when the data arrives
            }
            else
            {
                _vm.ApplyFilters();
                RefreshFunnelIcons();
            }
            ResetSearch();
            SetViewMode(PoolViewMode.Sets);
        }

        /// <summary>"◀ All Sets" / "◀ All Decks": back to whichever browser we came from.</summary>
        private void BtnBackToSets_Click(object sender, RoutedEventArgs e)
        {
            if (_inDecksContext) ShowDecks();
            else ShowSets();
        }

        private const string SetsFilterKey = "Sets";

        private void LoadPoolCore(string tag)
        {
            // Each table keeps its own filters while the app is open.
            _vm.UseFilters(_inSetsContext ? SetsFilterKey : tag);
            PrepareTable(tag);
            _vm.LoadPool(tag);
            // Sort is applied in OnItemsChanged when the binding propagates.
        }

        /// <summary>
        /// Switch the grid to another table: save the one we're leaving, show
        /// this table's own layout, and clear per-table state.
        /// </summary>
        private void PrepareTable(string tag)
        {
            if (KindOf(_currentTag) != KindOf(tag))
            {
                // A sort on a column the other kind doesn't have (e.g. Qty)
                // falls back to Name.
                _lastSortProp = "Name";
                _lastSortAsc = true;
            }
            SaveColumnLayoutNow();
            ApplyColumnLayout(tag);

            _currentTag = tag;
            ResetSearch();
            ClearDetail();
            Gallery.Reset();         // new card objects for this table
            _setTiles = null;        // set tiles come from the Cards pool
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

            // Totals row follows the rows on screen (filters applied)
            UpdateTotals();

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

        // ══════════════════════════════════════════════════════════════════
        // COLUMN LAYOUT — per table (Cards, Tokens, …, Collection). Order,
        // visibility, and width are saved separately for each table in
        // GridLayouts.json. Drag a header to reorder, drag an edge to resize,
        // the Columns button to show/hide. Name can't be hidden.
        // ══════════════════════════════════════════════════════════════════
        private readonly Dictionary<string, (DataGridLength width, int index)> _defaultColumns = new();
        private string _layoutTable = "";     // table whose layout the grid shows now
        private bool _applyingLayout;         // true while code (not the user) moves columns
        private System.Windows.Threading.DispatcherTimer? _layoutSaveTimer;

        private void InitColumnLayout()
        {
            var widthDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.WidthProperty, typeof(DataGridColumn));

            foreach (var col in PoolGrid.Columns)
            {
                // Default position = the column's order in the XAML. (DisplayIndex
                // is still -1 here: the grid hasn't assigned positions yet.)
                _defaultColumns[ColumnHeader(col)] = (col.Width, PoolGrid.Columns.IndexOf(col));
                widthDescriptor?.AddValueChanged(col, (_, _) => RequestColumnLayoutSave());
            }

            PoolGrid.ColumnReordered += (_, _) => RequestColumnLayoutSave();

            // Dragging a header toward the grid's left/right edge scrolls the table.
            PoolGrid.ColumnHeaderDragStarted += (_, _) => StartDragAutoScroll();
            PoolGrid.ColumnHeaderDragCompleted += (_, _) => StopDragAutoScroll();

            // Column virtualization discards off-screen headers, and discarding
            // the header being dragged cancels the drag. So while the mouse is
            // down on a header, build all columns; restore after the drop.
            PoolGrid.PreviewMouseLeftButtonDown += PoolGrid_HeaderPressCheck;
            PoolGrid.PreviewMouseLeftButtonUp += (_, _) => RestoreColumnVirtualization();

            // Don't lose a pending change when leaving the page or closing the app.
            Unloaded += (_, _) => SaveColumnLayoutNow();
            if (Application.Current != null)
                Application.Current.Exit += (_, _) => SaveColumnLayoutNow();
        }

        // ── Auto-scroll while dragging a column header ──────────────────
        private System.Windows.Threading.DispatcherTimer? _dragScrollTimer;
        private const double DragScrollEdge = 60;     // px from the edge where scrolling starts

        private void StartDragAutoScroll()
        {
            _dragScrollTimer ??= CreateDragScrollTimer();
            _dragScrollTimer.Start();
        }

        private void StopDragAutoScroll() => _dragScrollTimer?.Stop();

        private void PoolGrid_HeaderPressCheck(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d) return;
            // A header press that could start a drag — not the funnel button
            // and not the resize grip.
            if (FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(d) == null) return;
            if (FindAncestor<Button>(d) != null) return;
            if (FindAncestor<System.Windows.Controls.Primitives.Thumb>(d) != null) return;

            PoolGrid.EnableColumnVirtualization = false;
        }

        private void RestoreColumnVirtualization()
        {
            if (PoolGrid.EnableColumnVirtualization) return;
            // After the drop has been processed (mouse-up reaches the header
            // after this preview event), switch back to on-screen-only columns.
            Dispatcher.BeginInvoke(new Action(() => PoolGrid.EnableColumnVirtualization = true),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Nearest ancestor of type T (works from text runs too).</summary>
        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private System.Windows.Threading.DispatcherTimer CreateDragScrollTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            timer.Tick += (_, _) =>
            {
                // Safety: the drag ended somewhere we didn't hear about.
                if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                {
                    timer.Stop();
                    RestoreColumnVirtualization();
                    return;
                }

                var sv = FindVisualChild<ScrollViewer>(PoolGrid);
                if (sv == null) return;

                double x = System.Windows.Input.Mouse.GetPosition(PoolGrid).X;
                double width = PoolGrid.ActualWidth;

                // Faster the closer to (or further past) the edge.
                double step = 0;
                if (x < DragScrollEdge)
                    step = -(4 + (DragScrollEdge - x) * 0.6);
                else if (x > width - DragScrollEdge)
                    step = 4 + (x - (width - DragScrollEdge)) * 0.6;

                if (step != 0)
                    sv.ScrollToHorizontalOffset(sv.HorizontalOffset + Math.Clamp(step, -80, 80));
            };
            return timer;
        }

        private static string ColumnHeader(DataGridColumn col) => col.Header?.ToString() ?? "";

        private DataGridColumn? FindColumn(string header) =>
            PoolGrid.Columns.FirstOrDefault(c => ColumnHeader(c) == header);

        /// <summary>Does this column exist for this kind of table?</summary>
        private static bool ColumnApplies(string header, TableKind kind) =>
            !ColumnKinds.TryGetValue(header, out var kinds) || Array.IndexOf(kinds, kind) >= 0;

        /// <summary>
        /// Show a table's layout: defaults first (XAML widths and order, plus the
        /// table's column set), then that table's saved order/visibility/width.
        /// </summary>
        private void ApplyColumnLayout(string table)
        {
            _applyingLayout = true;
            try
            {
                var kind = KindOf(table);

                // 1. Defaults. Set positions in ascending order so each
                //    assignment lands where intended.
                foreach (var kv in _defaultColumns.OrderBy(k => k.Value.index))
                {
                    var col = FindColumn(kv.Key);
                    if (col == null) continue;
                    col.Width = kv.Value.width;
                    col.DisplayIndex = kv.Value.index;
                    col.Visibility = DefaultHiddenColumns.Contains(kv.Key)   // defaults show every
                        ? Visibility.Collapsed : Visibility.Visible;          // column but the hidden-by-default ones
                }
                ApplyColumnSet(kind);                      // then hide what this table doesn't have

                // 2. This table's saved layout, if any.
                var saved = Services.GridLayoutService.Get(table);
                if (saved != null)
                {
                    foreach (var cl in saved.OrderBy(c => c.DisplayIndex))
                    {
                        var col = FindColumn(cl.Header);
                        if (col == null) continue;       // column no longer exists

                        if (cl.Width > 0 && col.CanUserResize)
                            col.Width = new DataGridLength(cl.Width);
                        if (cl.DisplayIndex >= 0 && cl.DisplayIndex < PoolGrid.Columns.Count)
                            col.DisplayIndex = cl.DisplayIndex;

                        // Only user choices: a column this table doesn't have
                        // stays hidden, and Name always shows.
                        if (ColumnApplies(cl.Header, kind) && cl.Header != "Name")
                            col.Visibility = cl.Visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                _layoutTable = table;
            }
            finally
            {
                _applyingLayout = false;
            }

            // Headers may be rebuilt: funnel colors must match the filters.
            Dispatcher.BeginInvoke(new Action(RefreshFunnelIcons),
                System.Windows.Threading.DispatcherPriority.Loaded);
            QueueTotalsSync();
        }

        /// <summary>Save shortly after the user stops dragging (one write, not hundreds).</summary>
        private void RequestColumnLayoutSave()
        {
            if (_applyingLayout || string.IsNullOrEmpty(_layoutTable)) return;
            _layoutSaveTimer ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
            _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void LayoutSaveTimer_Tick(object? sender, EventArgs e) => SaveColumnLayoutNow();

        /// <summary>Write the current table's layout (all columns) right now.</summary>
        private void SaveColumnLayoutNow()
        {
            _layoutSaveTimer?.Stop();
            if (_applyingLayout || string.IsNullOrEmpty(_layoutTable)) return;

            var layout = PoolGrid.Columns.Select(c => new Services.ColumnLayout
            {
                Header = ColumnHeader(c),
                DisplayIndex = c.DisplayIndex,
                Visible = c.Visibility == Visibility.Visible,
                Width = c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth,
            }).ToList();

            Services.GridLayoutService.Set(_layoutTable, layout);
        }

        /// <summary>Columns button: checklist of this table's regular columns.</summary>
        private void BtnColumns_Click(object sender, RoutedEventArgs e) =>
            ShowColumnChecklist((FrameworkElement)sender, legality: false);

        /// <summary>Legality button: checklist of the format legality columns.</summary>
        private void BtnLegality_Click(object sender, RoutedEventArgs e) =>
            ShowColumnChecklist((FrameworkElement)sender, legality: true);

        /// <summary>
        /// A scrolling checklist under the button. Columns lists the regular
        /// columns (in their current order); Legality lists the formats (in
        /// LegalityInfo order) with Show all / Hide all. Both have Reset.
        /// Either way they're ordinary grid columns: drag to move them anywhere.
        /// </summary>
        private void ShowColumnChecklist(FrameworkElement anchor, bool legality)
        {
            if (string.IsNullOrEmpty(_layoutTable)) return;
            var kind = KindOf(_layoutTable);

            // Which columns this list covers
            IEnumerable<DataGridColumn> columns = legality
                ? Models.LegalityInfo.Formats
                    .Select(f => FindColumn(f.Header))
                    .Where(c => c != null)!
                : PoolGrid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Where(c => !LegalityHeaders.Contains(ColumnHeader(c)));
            columns = columns.Where(c => ColumnApplies(ColumnHeader(c!), kind)).ToList();

            var list = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            var boxes = new List<(CheckBox box, DataGridColumn col)>();

            foreach (var col in columns)
            {
                string header = ColumnHeader(col);
                var box = new CheckBox
                {
                    Content = header,
                    IsChecked = col.Visibility == Visibility.Visible,
                    IsEnabled = header != "Name",      // Name is always shown
                    Margin = new Thickness(0, 2, 0, 2),
                };
                var column = col;
                box.Click += (_, _) =>
                {
                    column.Visibility = box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                    AfterChecklistChange();
                };
                list.Children.Add(box);
                boxes.Add((box, col));
            }

            var scroll = new ScrollViewer
            {
                Content = list,
                MaxHeight = 460,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };

            var footer = new WrapPanel { Margin = new Thickness(10, 4, 10, 10) };
            if (legality)
            {
                footer.Children.Add(ChecklistButton("Show all", () => SetAll(true)));
                footer.Children.Add(ChecklistButton("Hide all", () => SetAll(false)));
            }
            footer.Children.Add(ChecklistButton("Reset to default", () =>
            {
                Services.GridLayoutService.Remove(_layoutTable);
                ApplyColumnLayout(_layoutTable);
                popup.IsOpen = false;
            }));

            var root = new StackPanel();
            root.Children.Add(scroll);
            root.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 2) });
            root.Children.Add(footer);

            popup.Child = new Border
            {
                Child = root,
                MinWidth = 230,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
                             ?? TryFindResource("ApplicationBackgroundBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                BorderBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush
                              ?? Brushes.Gray,
            };
            popup.IsOpen = true;

            void SetAll(bool visible)
            {
                foreach (var (box, col) in boxes)
                {
                    if (!box.IsEnabled) continue;
                    box.IsChecked = visible;
                    col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
                AfterChecklistChange();
            }
        }

        private static Button ChecklistButton(string text, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0)
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        private void AfterChecklistChange()
        {
            SaveColumnLayoutNow();
            Dispatcher.BeginInvoke(new Action(RefreshFunnelIcons),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ── Legality columns ────────────────────────────────────────────
        /// <summary>
        /// One colored-chip column per format, inserted right after Rarity (v1's
        /// spot). Hidden until the Collection shows them; filterable (Legal /
        /// Ban / Res / No) and sortable by severity from the funnel popup.
        /// </summary>
        private void AddLegalityColumns()
        {
            var rarity = FindColumn("Rarity");
            int insertAt = rarity != null ? PoolGrid.Columns.IndexOf(rarity) + 1 : PoolGrid.Columns.Count;
            var headerTemplate = (DataTemplate)FindResource("FilterableHeader");

            foreach (var fmt in Models.LegalityInfo.Formats)
            {
                var col = new DataGridTemplateColumn
                {
                    Header = fmt.Header,
                    HeaderTemplate = headerTemplate,
                    Width = new DataGridLength(Math.Max(74, fmt.Header.Length * 8 + 50)),
                    CellTemplate = CreateLegalityCellTemplate(fmt.Key),
                    CanUserSort = false,               // sort from the funnel popup
                    Visibility = Visibility.Collapsed,
                };
                PoolGrid.Columns.Insert(insertAt++, col);
            }

            // Deck "Legal" column right after Name (v1's spot).
            var name = FindColumn("Name");
            int legalAt = name != null ? PoolGrid.Columns.IndexOf(name) + 1 : 0;
            PoolGrid.Columns.Insert(legalAt, new DataGridTemplateColumn
            {
                Header = DeckLegalHeader,
                HeaderTemplate = headerTemplate,
                Width = new DataGridLength(90),
                CellTemplate = CreateLegalityCellTemplate(Models.LegalityAccessor.DeckFormatKey),
                CanUserSort = false,               // sort from the funnel popup
                Visibility = Visibility.Collapsed,
            });
        }

        /// <summary>The colored status chip for one format (same look as v1).</summary>
        private static DataTemplate CreateLegalityCellTemplate(string formatKey)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.MarginProperty, new Thickness(3, 1, 3, 1));
            border.SetValue(Border.PaddingProperty, new Thickness(6, 1, 6, 1));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.SetBinding(Border.BackgroundProperty,
                new Binding($"Legality[{formatKey}].Background"));

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty,
                new Binding($"Legality[{formatKey}].Text"));
            text.SetBinding(TextBlock.ForegroundProperty,
                new Binding($"Legality[{formatKey}].Foreground"));
            text.SetValue(TextBlock.FontSizeProperty, 11.0);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            border.AppendChild(text);
            return new DataTemplate { VisualTree = border };
        }

        /// <summary>
        /// Open a popup window just under <paramref name="anchor"/>, shifted
        /// left/up as needed so it stays inside the main window (works when
        /// maximized, on any monitor, at any display scaling).
        /// </summary>
        private void PlaceInsideWindow(Window popup, FrameworkElement anchor)
        {
            var src = PresentationSource.FromVisual(anchor);
            Point ToDip(Point p) =>
                src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(p) : p;

            var pt = ToDip(anchor.PointToScreen(new Point(0, anchor.ActualHeight)));
            double left = pt.X, top = pt.Y;

            if (Window.GetWindow(this) is Window owner && owner.Content is FrameworkElement content)
            {
                var topLeft = ToDip(content.PointToScreen(new Point(0, 0)));
                double right = topLeft.X + content.ActualWidth;
                double bottom = topLeft.Y + content.ActualHeight;

                if (left + popup.Width > right) left = right - popup.Width;
                if (top + popup.Height > bottom) top = bottom - popup.Height;
                left = Math.Max(left, topLeft.X);
                top = Math.Max(top, topLeft.Y);
            }

            popup.Left = left;
            popup.Top = top;
        }

        // ══════════════════════════════════════════════════════════════════
        // TOTALS ROW (Collection) — one green row under the grid, like v1.
        // Each grid column has a twin here: same width, position, and hidden
        // state, and the row scrolls sideways with the grid. Totals are for the
        // rows on screen, so they follow the filters.
        // ══════════════════════════════════════════════════════════════════
        private readonly List<(DataGridColumn src, DataGridColumn sum)> _totalsColumns = new();
        private bool _totalsSyncQueued;
        private ScrollViewer? _gridScroll;

        // Grid column header → CollectionTotalsRow property.
        private static readonly Dictionary<string, string> TotalsBindings = new()
        {
            ["Name"] = nameof(CollectionTotalsRow.Label),
            ["Qty"] = nameof(CollectionTotalsRow.Qty),
            ["Used"] = nameof(CollectionTotalsRow.Used),
            ["Available"] = nameof(CollectionTotalsRow.Available),
            ["Value"] = nameof(CollectionTotalsRow.Value),
            ["Buy At"] = nameof(CollectionTotalsRow.BuyAt),
            ["Sell At"] = nameof(CollectionTotalsRow.SellAt),
            ["Sell At Value"] = nameof(CollectionTotalsRow.SellAtValue),
            ["Needed"] = nameof(CollectionTotalsRow.Needed),
            ["Excess"] = nameof(CollectionTotalsRow.Excess),
            ["Target"] = nameof(CollectionTotalsRow.Target),
            // Deck totals
            ["Legal"] = nameof(CollectionTotalsRow.Legal),
            ["Non-Foil"] = nameof(CollectionTotalsRow.NonFoil),
            ["Foil"] = nameof(CollectionTotalsRow.Foil),
            ["Total"] = nameof(CollectionTotalsRow.Total),
            ["Owned"] = nameof(CollectionTotalsRow.Owned),
            ["Missing"] = nameof(CollectionTotalsRow.Missing),
            ["Wanted"] = nameof(CollectionTotalsRow.Wanted),
            ["Asking"] = nameof(CollectionTotalsRow.Asking),
            ["Offer"] = nameof(CollectionTotalsRow.Offer),
        };

        private void BuildTotalsColumns()
        {
            var widthDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
            var visDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.VisibilityProperty, typeof(DataGridColumn));

            foreach (var src in PoolGrid.Columns)
            {
                var sum = new DataGridTextColumn { IsReadOnly = true, Width = src.Width };
                if (TotalsBindings.TryGetValue(ColumnHeader(src), out var prop))
                    sum.Binding = new Binding(prop);
                TotalsGrid.Columns.Add(sum);
                _totalsColumns.Add((src, sum));

                widthDescriptor?.AddValueChanged(src, (_, _) => QueueTotalsSync());
                visDescriptor?.AddValueChanged(src, (_, _) => QueueTotalsSync());
            }
            PoolGrid.ColumnReordered += (_, _) => QueueTotalsSync();
        }

        /// <summary>Coalesce many column changes into one sync after layout.</summary>
        private void QueueTotalsSync()
        {
            if (_totalsSyncQueued) return;
            _totalsSyncQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _totalsSyncQueued = false;
                SyncTotalsColumns();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Totals columns take the grid's widths, hidden state, and order.</summary>
        private void SyncTotalsColumns()
        {
            if (TotalsGrid.Visibility != Visibility.Visible) return;

            foreach (var (src, sum) in _totalsColumns)
            {
                sum.Visibility = src.Visibility;
                double w = src.ActualWidth > 0 ? src.ActualWidth
                         : src.Width.IsAbsolute ? src.Width.Value : 0;
                if (w > 0) sum.Width = new DataGridLength(w);
            }
            // Positions in ascending order so each lands where intended.
            foreach (var (src, sum) in _totalsColumns.OrderBy(p => p.src.DisplayIndex))
            {
                if (src.DisplayIndex >= 0 && src.DisplayIndex < TotalsGrid.Columns.Count)
                    sum.DisplayIndex = src.DisplayIndex;
            }
            MatchTotalsScroll();
        }

        /// <summary>Scroll the totals row sideways with the grid.</summary>
        private void HookTotalsScroll()
        {
            if (_gridScroll != null) return;
            PoolGrid.ApplyTemplate();
            _gridScroll = FindVisualChild<ScrollViewer>(PoolGrid);
            if (_gridScroll == null) return;
            _gridScroll.ScrollChanged += (_, e) =>
            {
                if (e.HorizontalChange != 0 || e.ViewportWidthChange != 0 || e.ExtentWidthChange != 0)
                    MatchTotalsScroll();
            };
        }

        private void MatchTotalsScroll()
        {
            if (_gridScroll == null) return;
            // Same visible width as the grid (the grid loses some to its
            // vertical scroll bar), so both scroll the same distance.
            double gap = Math.Max(0, _gridScroll.ActualWidth - _gridScroll.ViewportWidth);
            TotalsGrid.Margin = new Thickness(0, 0, gap, 0);
            FindVisualChild<ScrollViewer>(TotalsGrid)?
                .ScrollToHorizontalOffset(_gridScroll.HorizontalOffset);
        }

        /// <summary>Recompute totals from the rows currently shown.</summary>
        private void UpdateTotals()
        {
            static string Money(decimal v) => v > 0 ? $"${v:N2}" : "";
            static string Count(int v) => v > 0 ? v.ToString("N0") : "";

            if (_currentTag == DeckTableTag)
            {
                var cards = _vm.Items.OfType<Models.DeckCard>().ToList();
                int missing = cards.Sum(c => c.CollectionMissing);
                // Cost to finish: missing copies at the card's price (non-foil, else foil).
                decimal missingCost = cards.Sum(c => c.CollectionMissing * (c.PriceUsd ?? c.PriceUsdFoil ?? 0m));
                int illegal = cards.Count(c =>
                    c.Legality[Models.LegalityAccessor.DeckFormatKey].Status is "banned" or "not_legal");
                TotalsGrid.ItemsSource = new[]
                {
                    new CollectionTotalsRow
                    {
                        Label = missing == 0
                            ? $"Totals ({cards.Count:N0} lines) · buildable from collection"
                            : $"Totals ({cards.Count:N0} lines) · {missing:N0} missing",
                        Owned = cards.Sum(c => c.CollectionOwned).ToString("N0"),
                        Missing = missing == 0 ? "0" : $"{missing:N0} (${missingCost:N2})",
                        Wanted = Count(cards.Sum(c => c.WantedCount)),
                        Legal = illegal == 0 ? "All legal" : $"{illegal} illegal",
                        NonFoil = cards.Sum(c => c.Quantity).ToString("N0"),
                        Foil = cards.Sum(c => c.FoilQuantity).ToString("N0"),
                        Total = cards.Sum(c => c.TotalQuantity).ToString("N0"),
                        Value = $"${cards.Sum(c => c.RowValue):N2}",
                    }
                };
                QueueTotalsSync();
                return;
            }

            if (!IsCollectionKind(KindOf(_currentTag)))
            {
                TotalsGrid.ItemsSource = null;
                return;
            }

            // Every collection table (main, tokens, …, Trade Binder, Want List):
            // the tables differ, so read the fields by name; a field a table
            // doesn't have totals as blank.
            var rows = _vm.Items.Cast<object>().Where(r => r != null).ToList();
            int foil = rows.Where(r => (Str(r, "Finish") ?? "nonfoil") != Models.CardFinish.NonFoil)
                           .Sum(r => Int(r, "Quantity"));

            TotalsGrid.ItemsSource = new[]
            {
                new CollectionTotalsRow
                {
                    Label = $"Totals ({rows.Count:N0} rows, {foil:N0} foil)",
                    Qty = rows.Sum(r => Int(r, "Quantity")).ToString("N0"),
                    Used = Count(rows.Sum(r => Int(r, "UsedCount"))),
                    Available = Count(rows.Sum(r => Int(r, "AvailableCount"))),
                    Value = $"${rows.Sum(r => Dec(r, "RowValue")):N2}",
                    BuyAt = Money(rows.Sum(r => Dec(r, "BuyAt"))),
                    SellAt = Money(rows.Sum(r => Dec(r, "SellAt"))),
                    SellAtValue = Money(rows.Sum(r => Dec(r, "SellAtValue"))),
                    Needed = Count(rows.Sum(r => Int(r, "Needed"))),
                    Excess = Count(rows.Sum(r => Int(r, "Excess"))),
                    Target = Count(rows.Sum(r => Int(r, "Target"))),
                    // Asking / offer prices are per copy.
                    Asking = Money(rows.Sum(r => Dec(r, "AskingPrice") * Int(r, "Quantity"))),
                    Offer = Money(rows.Sum(r => Dec(r, "OfferPrice") * Int(r, "Quantity"))),
                }
            };
            QueueTotalsSync();
        }

        // ── Field readers for the totals (cached per type + property) ──
        private static readonly Dictionary<(Type, string), System.Reflection.PropertyInfo?> _totalsProps = new();

        private static object? Val(object row, string prop)
        {
            var key = (row.GetType(), prop);
            if (!_totalsProps.TryGetValue(key, out var pi))
                _totalsProps[key] = pi = row.GetType().GetProperty(prop);
            return pi?.GetValue(row);
        }
        private static int Int(object row, string prop) => Val(row, prop) is int i ? i : 0;
        private static decimal Dec(object row, string prop) => Val(row, prop) switch
        {
            decimal d => d,
            _ => 0m,
        };
        private static string? Str(object row, string prop) => Val(row, prop) as string;

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
            // Deck browser: jump to a deck by name (never hides anything)
            if (_viewMode == PoolViewMode.Decks)
            {
                if (!string.IsNullOrEmpty(text)) SearchDecks(text);
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

        // ── View modes: Grid, Gallery, Sets, Decks ──────────────────────
        private enum PoolViewMode { Grid, Gallery, Sets, Decks }

        /// <summary>A browser (set or deck tiles), not a card view.</summary>
        private static bool IsBrowser(PoolViewMode m) => m == PoolViewMode.Sets || m == PoolViewMode.Decks;
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
            if (!IsBrowser(_viewMode)) SetViewMode(SwitchMode);
        }

        private void SetViewMode(PoolViewMode mode)
        {
            var previous = _viewMode;
            var selected = PoolGrid.SelectedItem;

            _viewMode = mode;

            PoolGrid.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            Gallery.Visibility = mode == PoolViewMode.Gallery ? Visibility.Visible : Visibility.Collapsed;
            SetList.Visibility = mode == PoolViewMode.Sets ? Visibility.Visible : Visibility.Collapsed;
            DeckList.Visibility = mode == PoolViewMode.Decks ? Visibility.Visible : Visibility.Collapsed;

            // Header buttons: Edition sort only matters in the grid; the
            // browsers use none of the card-view buttons.
            bool cardView = !IsBrowser(mode);
            BtnClearFilters.Visibility = cardView ? Visibility.Visible : Visibility.Collapsed;
            BtnEditionOrder.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            BtnColumns.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            BtnSetCompletion.Visibility = mode == PoolViewMode.Sets ? Visibility.Visible : Visibility.Collapsed;
            BtnStatistics.Visibility = cardView && (_currentTag == "Collection" || _currentTag == DeckTableTag)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnLegality.Visibility = mode == PoolViewMode.Grid && _currentTag == "Collection"
                ? Visibility.Visible : Visibility.Collapsed;
            bool showTotals = mode == PoolViewMode.Grid &&
                              (IsCollectionKind(KindOf(_currentTag)) || _currentTag == DeckTableTag);
            TotalsGrid.Visibility = showTotals ? Visibility.Visible : Visibility.Collapsed;
            if (showTotals)
            {
                HookTotalsScroll();
                QueueTotalsSync();
            }
            BtnBackToSets.Visibility = cardView && (_inSetsContext || _inDecksContext)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnBackToSets.Content = _inDecksContext ? "◀  All Decks" : "◀  All Sets";
            BtnBackToSets.ToolTip = _inDecksContext ? "Back to the deck browser" : "Back to the set browser";

            if (mode == PoolViewMode.Sets)
            {
                _vm.Title = "Sets";
                if (_setTiles != null) _vm.StatusText = $"{_setTiles.Count:N0} sets";
            }
            else if (mode == PoolViewMode.Decks)
            {
                _vm.Title = "Decks";
                _vm.StatusText = _deckTiles.Count == 1 ? "1 deck" : $"{_deckTiles.Count:N0} decks";
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
                    case PoolViewMode.Decks:
                        RebuildDeckRows();
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════════════════════
        // DECK BROWSER — every .deck file under Documents\BoE_V2\Decks
        // (subfolders too), grouped Commander / Standard, A→Z. Click a tile →
        // the deck opens read-only in the grid or gallery (per the switch).
        // ══════════════════════════════════════════════════════════════════
        private List<DeckTile> _deckTiles = new();
        private List<(string section, List<DeckTile> tiles)> _deckGroups = new();
        private List<DeckBrowserRow> _deckRows = new();
        private int _decksPerRow = 1;
        private DeckTile? _deckHighlighted;
        private string _openDeckFormat = "standard";
        private Models.Deck? _openDeck;            // the deck on screen (for Statistics)

        /// <summary>"Decks" nav item: the deck browser (re-reads the folder each time).</summary>
        public void ShowDecks()
        {
            _inSetsContext = false;
            _inDecksContext = true;
            _vm.IsEmpty = false;             // the grid's empty message doesn't apply here
            BuildDeckTiles();
            ResetSearch();
            SetViewMode(PoolViewMode.Decks);
        }

        private void BuildDeckTiles()
        {
            var tiles = new List<DeckTile>();
            string root = Services.AppFolderService.DecksFolder;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.deck", SearchOption.AllDirectories).ToList(); }
            catch { files = Array.Empty<string>(); }

            foreach (var path in files)
            {
                try
                {
                    // Read the file directly — no pool lookups — so the browser is fast.
                    var deck = System.Text.Json.JsonSerializer.Deserialize<Models.Deck>(File.ReadAllText(path));
                    if (deck == null) continue;

                    string folder = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root);

                    // Color identity: the commander's (Commander decks), else the main deck's.
                    var main = deck.Cards.Where(c => c.Category != Models.DeckCardCategory.Sideboard).ToList();
                    var commanders = main.Where(c => c.IsCommander || c.Category == Models.DeckCardCategory.Commander).ToList();
                    var identitySource = deck.DeckType == Models.DeckType.Commander && commanders.Count > 0
                        ? commanders : main;
                    var identity = new HashSet<char>(identitySource.SelectMany(c => c.ColorIdentity));
                    string symbols = string.Concat("WUBRG".Where(identity.Contains).Select(c => $"{{{c}}}"));
                    if (symbols.Length == 0) symbols = "{C}";
                    tiles.Add(new DeckTile
                    {
                        Name = string.IsNullOrWhiteSpace(deck.Name)
                            ? Path.GetFileNameWithoutExtension(path) : deck.Name,
                        FilePath = path,
                        IsCommander = deck.DeckType == Models.DeckType.Commander,
                        CommanderName = string.Join(" & ",
                            deck.Cards.Where(c => c.IsCommander).Select(c => c.Name)),
                        Folder = folder == "." ? "" : folder,
                        IdentitySymbols = symbols,
                        CardCount = deck.Cards.Sum(c => c.TotalQuantity),
                        TotalValue = deck.Cards.Sum(c => c.RowValue),
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Deck browser skipped {path}: {ex.Message}");
                }
            }

            _deckTiles = tiles;
            _deckGroups = tiles
                .GroupBy(t => t.IsCommander ? "Commander" : "Standard")
                .OrderBy(g => g.Key == "Commander" ? 0 : 1)
                .Select(g => (section: g.Key,
                              tiles: g.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();
        }

        /// <summary>Flatten sections into header rows + rows of N tiles.</summary>
        private void RebuildDeckRows()
        {
            double avail = DeckList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            _decksPerRow = perRow;

            var rows = new List<DeckBrowserRow>();
            if (_deckTiles.Count == 0)
            {
                rows.Add(new DeckBrowserRow
                {
                    IsHeader = true,
                    HeaderText = $"No decks yet — copy .deck files into {Services.AppFolderService.DecksFolder}",
                    HeaderCount = "0",
                });
            }
            foreach (var (section, tiles) in _deckGroups)
            {
                rows.Add(new DeckBrowserRow
                {
                    IsHeader = true,
                    HeaderText = section,
                    HeaderCount = tiles.Count.ToString("N0"),
                });
                for (int i = 0; i < tiles.Count; i += perRow)
                    rows.Add(new DeckBrowserRow
                    {
                        Tiles = tiles.GetRange(i, Math.Min(perRow, tiles.Count - i))
                    });
            }
            _deckRows = rows;
            DeckList.ItemsSource = rows;
        }

        private void DeckList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode != PoolViewMode.Decks || !e.WidthChanged) return;
            double avail = DeckList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            if (perRow != _decksPerRow) RebuildDeckRows();
        }

        private void DeckTile_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DeckTile tile)
                OpenDeck(tile);
        }

        /// <summary>Open a deck read-only in the shared grid (Deck columns + layout).</summary>
        private void OpenDeck(DeckTile tile)
        {
            Models.Deck? deck;
            try
            {
                deck = Services.DeckService.Load(tile.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"Could not open this deck.\n\n{ex.Message}", "Open Deck",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (deck == null) return;

            // Legal column checks each card against this deck's format.
            string format = deck.DeckType == Models.DeckType.Commander ? "commander" : "standard";
            foreach (var card in deck.Cards) card.DeckFormat = format;
            _openDeckFormat = format;
            _openDeck = deck;

            // Deck vs. collection: Owned / Free / Missing / Wanted per card (read-only).
            Services.DeckCollectionService.Annotate(deck);

            _vm.UseFilters("Deck:" + tile.FilePath);     // each deck keeps its own filters
            PrepareTable(DeckTableTag);
            PoolGrid.SelectedItem = null;
            _vm.LoadRows($"Decks — {deck.Name}",
                         deck.Cards.Cast<object>().ToList(),
                         deck.Cards.Count == 1 ? "line" : "lines");
            SetViewMode(SwitchMode);          // grid or gallery, per the switch
        }

        /// <summary>Search in the deck browser: jump to the first deck name that begins with the text.</summary>
        private void SearchDecks(string text)
        {
            for (int r = 0; r < _deckRows.Count; r++)
            {
                var row = _deckRows[r];
                if (row.IsHeader) continue;
                var match = row.Tiles.FirstOrDefault(t =>
                    t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;

                if (_deckHighlighted != null) _deckHighlighted.IsHighlighted = false;
                match.IsHighlighted = true;
                _deckHighlighted = match;

                DeckList.UpdateLayout();
                int jump = Math.Min(r + 10, _deckRows.Count - 1);
                DeckList.ScrollIntoView(_deckRows[jump]);
                DeckList.UpdateLayout();
                DeckList.ScrollIntoView(row);
                return;
            }
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
            var owned = LoadOwnedScryfallIds();
            System.Reflection.PropertyInfo? pCode = null, pName = null, pType = null,
                                            pDate = null, pUsd = null, pFoil = null, pSid = null;
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
                    pFoil = t.GetProperty("PriceUsdFoil");
                    pSid = t.GetProperty("ScryfallId");
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
                decimal? usd = pUsd?.GetValue(card) as decimal?;
                if (usd is decimal u) tile.TotalValue += u;

                // Completion: one pool row = one printing of this set.
                string sid = pSid?.GetValue(card) as string ?? "";
                if (sid.Length > 0 && owned.Contains(sid))
                    tile.OwnedCount++;
                else
                    tile.MissingValue += usd ?? (pFoil?.GetValue(card) as decimal?) ?? 0m;
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

        /// <summary>
        /// ScryfallIds of every printing in the collection with at least one
        /// copy (any finish). Read-only; an empty set if the collection can't
        /// be read.
        /// </summary>
        private static HashSet<string> LoadOwnedScryfallIds()
        {
            try
            {
                using var cdb = new Data.CollectionDbContext();
                return cdb.CollectionEntries
                    .Where(e => e.Quantity > 0 && e.ScryfallId != "")
                    .Select(e => e.ScryfallId)
                    .ToList()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Set completion: collection not read: {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Statistics button: overview of the rows on screen (Collection).</summary>
        private void BtnStatistics_Click(object sender, RoutedEventArgs e)
        {
            // Deck: statistics for the whole open deck.
            if (_currentTag == DeckTableTag)
            {
                if (_openDeck != null)
                    new DeckStatsWindow(_openDeck, Window.GetWindow(this)).Show();
                return;
            }
            if (_currentTag != "Collection") return;
            var rows = _vm.Items.OfType<Models.CollectionEntry>().ToList();
            new CollectionStatsWindow(rows, _vm.Filters.HasActiveFilters, _vm.AllRows.Count,
                                      Window.GetWindow(this)).Show();
        }

        /// <summary>Set Completion button (set browser): the sortable completion table.</summary>
        private void BtnSetCompletion_Click(object sender, RoutedEventArgs e)
        {
            if (_setTiles == null) return;
            var win = new SetCompletionWindow(_setTiles, Window.GetWindow(this));
            win.SetOpened += tile =>
            {
                win.Close();
                // Left the set browser meanwhile? Go back to it (Cards pool) first.
                if (!_inSetsContext || _currentTag != "Cards") ShowSets();
                OpenSet(tile);
            };
            win.Show();
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
            int cmp;
            if (_primaryProp.StartsWith(PoolColumnFilters.LegalityPrefix, StringComparison.Ordinal))
            {
                // Legality column: by severity (Ban → Res → No → Legal), then name.
                string key = _primaryProp.Substring(PoolColumnFilters.LegalityPrefix.Length);
                int ra = (x as Models.ILegalityRow)?.Legality[key].SortRank ?? 4;
                int rb = (y as Models.ILegalityRow)?.Legality[key].SortRank ?? 4;
                cmp = ra.CompareTo(rb);
                if (!_primaryAsc) cmp = -cmp;
                if (cmp != 0) return cmp;
                _namePi ??= type.GetProperty("Name", flags);
                cmp = string.Compare(_namePi?.GetValue(x) as string, _namePi?.GetValue(y) as string,
                                     StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            else
            {
                string a = _primaryPi?.GetValue(x)?.ToString() ?? "";
                string b = _primaryPi?.GetValue(y)?.ToString() ?? "";
                cmp = ColumnFilterState.CompareNatural(a, b);
                if (!_primaryAsc) cmp = -cmp;
                if (cmp != 0) return cmp;
            }

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

            // Sub-sort 3: Finish (collection rows) — the non-foil row sits just
            // above the foil row of the same printing. Pool rows have no Finish.
            _finishPi ??= type.GetProperty("Finish", flags);
            if (_finishPi != null && _primaryProp != "Finish" && _primaryProp != "FinishPill")
            {
                cmp = FinishRank(_finishPi.GetValue(x) as string)
                          .CompareTo(FinishRank(_finishPi.GetValue(y) as string));
                if (cmp != 0) return cmp;
            }

            return 0;
        }

        private System.Reflection.PropertyInfo? _finishPi;
        private System.Reflection.PropertyInfo? _namePi;

        private static int FinishRank(string? finish) => finish switch
        {
            Models.CardFinish.Foil => 1,
            Models.CardFinish.Etched => 2,
            _ => 0      // non-foil first
        };
    }

    /// <summary>The Collection totals row's values (display strings).</summary>
    public sealed class CollectionTotalsRow
    {
        public string Label { get; init; } = "";
        public string Qty { get; init; } = "";
        public string Used { get; init; } = "";
        public string Available { get; init; } = "";
        public string Value { get; init; } = "";
        public string BuyAt { get; init; } = "";
        public string SellAt { get; init; } = "";
        public string SellAtValue { get; init; } = "";
        public string Needed { get; init; } = "";
        public string Excess { get; init; } = "";
        public string Target { get; init; } = "";
        // Deck totals
        public string Legal { get; init; } = "";
        public string NonFoil { get; init; } = "";
        public string Foil { get; init; } = "";
        public string Total { get; init; } = "";
        public string Owned { get; init; } = "";
        public string Missing { get; init; } = "";
        public string Wanted { get; init; } = "";
        public string Asking { get; init; } = "";
        public string Offer { get; init; } = "";
    }
}