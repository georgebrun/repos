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
            ["Tix"] = "PriceTixDisplay",        // MTGO price (online pool)
            ["Foil $"] = "PriceUsdFoilDisplay",
            ["Etched $"] = "PriceUsdEtchedDisplay",
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
            ["Condition"] = "Condition",
            ["Language"] = "Language",
            ["Notes"] = "Notes",
            ["Fav"] = "FavoriteDisplay",
            ["Storage"] = "StorageLocation",
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
            ["Etched"] = "EtchedQuantity",
            ["Total"] = "TotalQuantity",
            ["Owned"] = "OwnedTotal",           // Pool + Deck: copies of this printing you own
            ["Free"] = "CollectionFree",
            ["Claimed"] = "ClaimedCount",        // copies this deck claims from the collection
            ["Missing"] = "CollectionMissing",
            ["Wanted"] = "WantedCount",
            ["Other Decks"] = "OtherDecksCount",   // cards shared between decks
            // Trade Binder / Want List
            ["Asking"] = "AskingPriceDisplay",
            ["Offer"] = "OfferPriceDisplay",
        };

        // ── Table kinds ─────────────────────────────────────────────────
        // Pool (Cards, Tokens, …), the collections, and Deck share one grid;
        // each kind shows its own columns. Tags: "Collection" (main
        // collection), "Coll…" (tokens, planes, … collections), "TradeBinder",
        // "WantList", "Deck", else a pool.
        // Online: MTGO / Arena pools and collections ("MtgoCards", "ArenaCards",
        // "MtgoCollection", "ArenaCollection") — own tables, own columns.
        private enum TableKind
        {
            Pool, Collection, SpecialCollection, TradeBinder, WantList, Deck,
            MtgoPool, ArenaPool, MtgoCollection, ArenaCollection,
        }

        private static TableKind KindOf(string tag) => tag switch
        {
            "MtgoCards" => TableKind.MtgoPool,
            "ArenaCards" => TableKind.ArenaPool,
            "MtgoCollection" => TableKind.MtgoCollection,
            "ArenaCollection" => TableKind.ArenaCollection,
            "Collection" => TableKind.Collection,
            "TradeBinder" => TableKind.TradeBinder,
            "WantList" => TableKind.WantList,
            DeckTableTag => TableKind.Deck,
            _ when tag.StartsWith("Coll", StringComparison.Ordinal) => TableKind.SpecialCollection,
            _ => TableKind.Pool,
        };

        /// <summary>Any collection table (main, special, binder, want list).</summary>
        private static bool IsCollectionKind(TableKind k) =>
            k is TableKind.Collection or TableKind.SpecialCollection or TableKind.TradeBinder or TableKind.WantList
              or TableKind.MtgoCollection or TableKind.ArenaCollection;

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
            var pd = new[] { TableKind.Pool, D };
            var d = new[] { D };
            var MP = TableKind.MtgoPool; var AP = TableKind.ArenaPool;
            var MC = TableKind.MtgoCollection; var AC = TableKind.ArenaCollection;
            var map = new Dictionary<string, TableKind[]>();
            map["Decks"] = cs;                        // ▸ "Used in" (decks claiming the copies)
            foreach (var h in new[] { "Used", "Available", "Language", "Storage" })
                map[h] = cs;
            map["Fav"] = new[] { C, S, MC, AC };
            // Online collections: Qty / Notes / Added like the others; Price and
            // Value (in tickets) on MTGO only — Arena has no prices.
            foreach (var h in new[] { "Qty", "Notes", "Added" })
                map[h] = new[] { C, S, B, W, MC, AC };
            map["Price"] = new[] { C, S, B, W, MC };
            map["Condition"] = csb;
            map["Value"] = new[] { C, S, B, W, D, MC };
            foreach (var h in new[] { "Color", "Flavor", "Power", "Toughness", "CMC", "Row" })
                map[h] = new[] { C, S, B, W, D, MC, AC };
            // MTGO pool: its price in tickets. Finish: the paper finishes mean
            // nothing online, so the online pools and Arena don't show it.
            map["Tix"] = new[] { MP };
            map["Finish"] = new[] { TableKind.Pool, C, S, B, W, D, MC };
            map["Asking"] = new[] { B };
            map["Offer"] = new[] { W };
            foreach (var h in new[] { "USD", "Foil $", "Etched $" })
                map[h] = pd;
            foreach (var h in new[] { "SB", "Non-Foil", "Foil", "Etched", "Total", "Claimed", "Free", "Missing", "Wanted", "Other Decks" })
                map[h] = d;
            map["Owned"] = new[] { TableKind.Pool, D, MP, AP };
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
            PoolGrid.Sorting += PoolGrid_Sorting;   // header click → our multi-level sort

            // Gallery view: selection goes through the grid (one selection,
            // two views); double-click opens the detail window.
            Gallery.CardClicked += GalleryCardClicked;
            Gallery.CardOpened += OpenCardDetailPopup;
            Gallery.CardRightClicked += GalleryCardRightClicked;
            Gallery.CardAdd += (card, keys) => { SelectForTileButton(card); GalleryAdd?.Invoke(card, keys); };
            Gallery.CardRemove += (card, keys) => { SelectForTileButton(card); GalleryRemove?.Invoke(card, keys); };

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

            // Grid zoom (Ctrl + wheel / Ctrl + = - 0), saved per table.
            InitZoom();

            // Filters panel + Sort list (grid and gallery).
            InitFilterPanel();

            // Collection rows: the "Used in" table under a row, and the Used tooltip.
            InitUsedIn();
        }

        private string _currentTag = "";
        private bool _inSetsContext;          // true while the "Sets" nav item is active
        private bool _inDecksContext;         // true while the "Decks" nav item is active

        /// <summary>Card Pool nav items (Cards, Tokens, …): always a card view.</summary>
        public void LoadPool(string tag)
        {
            _inSetsContext = false;
            _inDecksContext = false;
            _pendingSelect = null;             // a new table: no re-select left over from an edit
            _pendingFallback = null;
            LoadPoolCore(tag);
            // Edit tables: this table's own Grid / Gallery (saved, else the page's default).
            if (_embeddedGalleryDefault is bool d)
                _embeddedGallery = Services.GridLayoutService.GetEditGallery(LayoutKey(tag)) ?? d;
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
            FilterPanelView.CancelPending();     // a filter still waiting for typing belongs to the old table
            _usedInOpen.Clear();                 // "Used in" tables open belong to the old rows

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
            ShowSortArrow();

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

            // Filters panel: this table's lists and settings; Sort list in step.
            SyncFilterPanel();

            // Edit page: re-select the row an edit just touched.
            ApplyPendingSelection();
            ItemsReloaded?.Invoke();
        }

        /// <summary>Edit pages: the rows were (re)loaded and any re-selection is done.</summary>
        public event Action? ItemsReloaded;

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
}