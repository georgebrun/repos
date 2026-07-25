using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Timers;
using System.Windows.Data;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// CollectionViewModel — owns all logic for the Collection page.
    /// 
    /// Split-view: Pool grid (top) + Collection grid (bottom).
    /// Mode selector swaps which tables are shown.
    /// Filter panel applies to BOTH grids reactively.
    /// Search bar scrolls to match (NEVER filters/hides rows).
    /// </summary>
    public partial class CollectionViewModel : ObservableObject
    {
        // ── Debounce timer for filter changes ────────────────────────────
        private readonly System.Timers.Timer _filterDebounce;
        private bool _filterDirty = false;

        // ── Ownership cache for pool row coloring ────────────────────────
        private HashSet<string> _ownedIds = new(StringComparer.OrdinalIgnoreCase);

        // ══════════════════════════════════════════════════════════════════
        // OBSERVABLE PROPERTIES
        // ══════════════════════════════════════════════════════════════════

        // ── Mode ─────────────────────────────────────────────────────────
        [ObservableProperty]
        private string selectedMode = "Pool → Collection";

        public List<string> AvailableModes { get; } = new()
        {
            "Pool → Collection",
            "Pool → Tokens",
            "Pool → Planechase",
            "Pool → Archenemy",
            "Pool → Vanguard",
            "Pool → Conspiracy",
            "Pool → Art Series",
            "Collection → Trade Binder",
            "Pool → Want List"
        };

        // ── Pool Type (which pool table to browse) ───────────────────────
        // Derived from SelectedMode
        private string PoolTableType => SelectedMode switch
        {
            "Pool → Collection" => "pool",
            "Pool → Tokens" => "tokens",
            "Pool → Planechase" => "planar",
            "Pool → Archenemy" => "schemes",
            "Pool → Vanguard" => "vanguard",
            "Pool → Conspiracy" => "conspiracy",
            "Pool → Art Series" => "artseries",
            "Collection → Trade Binder" => "collection",
            "Pool → Want List" => "pool",
            _ => "pool"
        };

        // ── Grid Data ────────────────────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<CollectionDisplayRow> poolCards = new();

        [ObservableProperty]
        private ObservableCollection<CollectionDisplayRow> collectionCards = new();

        [ObservableProperty]
        private CollectionDisplayRow? selectedPoolCard;

        [ObservableProperty]
        private CollectionDisplayRow? selectedCollectionCard;

        // ── Card Detail Panel ────────────────────────────────────────────
        [ObservableProperty]
        private string selectedCardName = string.Empty;

        [ObservableProperty]
        private string selectedCardType = string.Empty;

        [ObservableProperty]
        private string selectedCardSet = string.Empty;

        [ObservableProperty]
        private string selectedCardRarity = string.Empty;

        [ObservableProperty]
        private string selectedCardCollectorNumber = string.Empty;

        [ObservableProperty]
        private string selectedCardArtist = string.Empty;

        [ObservableProperty]
        private string selectedCardManaCost = string.Empty;

        [ObservableProperty]
        private string selectedCardPowerToughness = string.Empty;

        [ObservableProperty]
        private string selectedCardOracleText = string.Empty;

        [ObservableProperty]
        private string selectedCardFlavorText = string.Empty;

        [ObservableProperty]
        private string selectedCardPriceUsd = string.Empty;

        [ObservableProperty]
        private string selectedCardPriceFoil = string.Empty;

        [ObservableProperty]
        private string selectedCardAvailableAs = string.Empty;

        [ObservableProperty]
        private string selectedCardImageUrl = string.Empty;

        [ObservableProperty]
        private string selectedCardLocalImagePath = string.Empty;

        [ObservableProperty]
        private string selectedCardKeywords = string.Empty;

        // ── Deck Usage (nested table) ────────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<DeckUsageRow> selectedCardDeckUsage = new();

        // ── Search ───────────────────────────────────────────────────────
        [ObservableProperty]
        private string searchText = string.Empty;

        // ── Filters ──────────────────────────────────────────────────────
        [ObservableProperty] private bool filterWhite;
        [ObservableProperty] private bool filterBlue;
        [ObservableProperty] private bool filterBlack;
        [ObservableProperty] private bool filterRed;
        [ObservableProperty] private bool filterGreen;
        [ObservableProperty] private bool filterColorless;

        [ObservableProperty] private string filterType = string.Empty;
        [ObservableProperty] private string filterSetCode = string.Empty;
        [ObservableProperty] private string filterRarity = string.Empty;
        [ObservableProperty] private string filterOracleText = string.Empty;
        [ObservableProperty] private string filterKeyword = string.Empty;
        [ObservableProperty] private string filterArtist = string.Empty;
        [ObservableProperty] private string filterName = string.Empty;

        [ObservableProperty] private double filterCmcMin = 0;
        [ObservableProperty] private double filterCmcMax = 20;

        [ObservableProperty] private decimal filterPriceMin = 0;
        [ObservableProperty] private decimal filterPriceMax = 99999;

        // ── Status Bar ───────────────────────────────────────────────────
        [ObservableProperty]
        private string poolStatusText = "0 cards in pool";

        [ObservableProperty]
        private string collectionStatusText = string.Empty;

        // ── Loading ──────────────────────────────────────────────────────
        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string loadingText = string.Empty;

        // ── Set list for filter dropdown ─────────────────────────────────
        [ObservableProperty]
        private ObservableCollection<string> availableSets = new();

        // ══════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════

        public CollectionViewModel()
        {
            // Debounce filter changes: wait 300ms after last change before querying
            _filterDebounce = new System.Timers.Timer(300);
            _filterDebounce.AutoReset = false;
            _filterDebounce.Elapsed += (s, e) =>
            {
                RunQueryInBackground();
            };

            // Load data on a background thread, then push to UI
            IsLoading = true;
            LoadingText = "Loading collection...";
            RunQueryInBackground();
        }

        /// <summary>
        /// Runs all database queries on a background thread,
        /// then pushes results to the UI thread.
        /// This prevents the UI from freezing.
        /// </summary>
        private void RunQueryInBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    // All DB work happens here — background thread
                    _ownedIds = CollectionService.GetOwnedScryfallIds();
                    var sets = LoadPoolSetsBackground();
                    var poolRows = BuildPoolRows();
                    var collRows = BuildCollectionRows();
                    var poolStatus = BuildPoolStatus();
                    var collStatus = BuildCollectionStatus(collRows);

                    // Push results to UI thread
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        AvailableSets = new ObservableCollection<string>(sets);
                        PoolCards = new ObservableCollection<CollectionDisplayRow>(poolRows);
                        CollectionCards = new ObservableCollection<CollectionDisplayRow>(collRows);
                        PoolStatusText = poolStatus;
                        CollectionStatusText = collStatus;
                        IsLoading = false;
                        LoadingText = string.Empty;
                        _filterDirty = false;
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        PoolStatusText = $"Error: {ex.Message}";
                        IsLoading = false;
                    });
                }
            });
        }

        // ══════════════════════════════════════════════════════════════════
        // MODE CHANGE
        // ══════════════════════════════════════════════════════════════════

        partial void OnSelectedModeChanged(string value)
        {
            RunQueryInBackground();
        }

        // ══════════════════════════════════════════════════════════════════
        // FILTER CHANGE HANDLERS — each triggers debounced re-query
        // ══════════════════════════════════════════════════════════════════

        partial void OnFilterWhiteChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterBlueChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterBlackChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterRedChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterGreenChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterColorlessChanged(bool value) => ScheduleFilterUpdate();
        partial void OnFilterTypeChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterSetCodeChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterRarityChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterOracleTextChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterKeywordChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterArtistChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterNameChanged(string value) => ScheduleFilterUpdate();
        partial void OnFilterCmcMinChanged(double value) => ScheduleFilterUpdate();
        partial void OnFilterCmcMaxChanged(double value) => ScheduleFilterUpdate();
        partial void OnFilterPriceMinChanged(decimal value) => ScheduleFilterUpdate();
        partial void OnFilterPriceMaxChanged(decimal value) => ScheduleFilterUpdate();

        private void ScheduleFilterUpdate()
        {
            _filterDirty = true;
            _filterDebounce.Stop();
            _filterDebounce.Start();
        }

        // ══════════════════════════════════════════════════════════════════
        // BACKGROUND QUERY BUILDERS — run off UI thread
        // ══════════════════════════════════════════════════════════════════

        private List<CollectionDisplayRow> BuildPoolRows()
        {
            try
            {
                using var db = new AppDbContext();

                if (SelectedMode == "Collection → Trade Binder")
                {
                    return BuildPoolFromCollectionRows();
                }

                IQueryable<PoolCard> query = db.PoolCards.AsNoTracking();
                query = ApplyPoolFilters(query);
                var results = query.Take(1000).ToList();

                int idx = 0;
                return results.Select(p => MapPoolToDisplayRow(p, idx++)).ToList();
            }
            catch
            {
                return new List<CollectionDisplayRow>();
            }
        }

        private List<CollectionDisplayRow> BuildPoolFromCollectionRows()
        {
            try
            {
                using var db = new CollectionDbContext();
                var entries = db.CollectionEntries
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .Take(1000)
                    .ToList();

                int idx = 0;
                return entries.Select(c => MapCollectionEntryToDisplayRow(c, idx++)).ToList();
            }
            catch
            {
                return new List<CollectionDisplayRow>();
            }
        }

        private string BuildPoolStatus()
        {
            try
            {
                using var db = new AppDbContext();
                return $"{db.PoolCards.Count():N0} cards in pool";
            }
            catch { return "Pool unavailable"; }
        }

        private List<CollectionDisplayRow> BuildCollectionRows()
        {
            try
            {
                using var db = new CollectionDbContext();

                if (SelectedMode == "Collection → Trade Binder" ||
                    SelectedMode.Contains("Trade Binder"))
                {
                    var entries = db.TradeBinderEntries
                        .AsNoTracking().OrderBy(t => t.Name).ToList();
                    int idx = 0;
                    return entries.Select(t => MapTradeBinderToDisplayRow(t, idx++)).ToList();
                }

                if (SelectedMode == "Pool → Want List")
                {
                    var entries = db.WantListEntries
                        .AsNoTracking().OrderBy(w => w.Name).ToList();
                    int idx = 0;
                    return entries.Select(w => MapWantListToDisplayRow(w, idx++)).ToList();
                }

                // Main collection
                var collEntries = db.CollectionEntries
                    .AsNoTracking().OrderBy(c => c.Name).ToList();
                int i = 0;
                return collEntries.Select(c => MapCollectionEntryToDisplayRow(c, i++)).ToList();
            }
            catch
            {
                return new List<CollectionDisplayRow>();
            }
        }

        private string BuildCollectionStatus(List<CollectionDisplayRow> rows)
        {
            if (SelectedMode == "Pool → Want List")
                return $"{rows.Count:N0} cards on want list";

            int totalQty = rows.Sum(r => r.Quantity + r.FoilQuantity);
            int totalFoils = rows.Sum(r => r.FoilQuantity);
            decimal totalValue = rows.Sum(r => r.TotalValue);
            int totalAvail = rows.Sum(r => r.AvailableCount);

            return $"{rows.Count:N0} | {totalQty:N0} | {totalFoils:N0} | {totalAvail:N0} | ${totalValue:F2}";
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD SELECTION — updates the detail panel
        // ══════════════════════════════════════════════════════════════════

        partial void OnSelectedPoolCardChanged(CollectionDisplayRow? value)
        {
            if (value != null) UpdateDetailPanel(value);
        }

        partial void OnSelectedCollectionCardChanged(CollectionDisplayRow? value)
        {
            if (value != null) UpdateDetailPanel(value);
        }

        private void UpdateDetailPanel(CollectionDisplayRow card)
        {
            SelectedCardName = card.Name;
            SelectedCardType = card.TypeLine;
            SelectedCardSet = $"{card.SetName} ({card.SetCode.ToUpper()})";
            SelectedCardRarity = card.Rarity;
            SelectedCardCollectorNumber = card.CollectorNumber;
            SelectedCardArtist = card.Artist;
            SelectedCardManaCost = card.ManaCost;
            SelectedCardOracleText = card.OracleText;
            SelectedCardFlavorText = card.FlavorText;
            SelectedCardKeywords = card.Keywords;

            SelectedCardPowerToughness = !string.IsNullOrWhiteSpace(card.Power)
                ? $"{card.Power}/{card.Toughness}" : string.Empty;

            SelectedCardPriceUsd = card.PriceUsd.HasValue
                ? $"USD: ${card.PriceUsd:F2}" : "USD: —";
            SelectedCardPriceFoil = card.PriceUsdFoil.HasValue
                ? $"USD Foil: ${card.PriceUsdFoil:F2}" : "USD Foil: —";

            var avail = new List<string>();
            if (card.IsNonFoil) avail.Add("Non-Foil");
            if (card.IsFoil) avail.Add("Foil");
            SelectedCardAvailableAs = string.Join(" · ", avail);

            // Image: prefer local, then URL
            SelectedCardLocalImagePath = card.LocalImagePath;
            SelectedCardImageUrl = card.ImageNormalUrl;
        }

        // ══════════════════════════════════════════════════════════════════
        // SEARCH — begins-with, scroll-to-match, NEVER filters rows
        // ══════════════════════════════════════════════════════════════════

        // Search is handled in the View (code-behind) via ScrollIntoView
        // because it needs direct access to the DataGrid control.
        // The ViewModel just exposes SearchText for binding.

        // ══════════════════════════════════════════════════════════════════
        // COMMANDS
        // ══════════════════════════════════════════════════════════════════

        [RelayCommand]
        private void AddToCollection()
        {
            if (SelectedPoolCard == null) return;

            // Look up the pool card by ScryfallId
            using var poolDb = new AppDbContext();
            var poolCard = poolDb.PoolCards
                .FirstOrDefault(p => p.ScryfallId == SelectedPoolCard.ScryfallId);
            if (poolCard == null) return;

            if (SelectedMode == "Pool → Want List")
            {
                CollectionService.AddToWantList(poolCard);
            }
            else if (SelectedMode == "Collection → Trade Binder")
            {
                CollectionService.AddToTradeBinder(poolCard);
            }
            else
            {
                CollectionService.AddToCollection(poolCard);
            }

            // Refresh ownership cache and collection grid
            _ownedIds = CollectionService.GetOwnedScryfallIds();
            LoadCollectionGrid();

            // Refresh pool to update ownership coloring
            RefreshPoolOwnershipColors();
        }

        [RelayCommand]
        private void RemoveFromCollection()
        {
            if (SelectedCollectionCard == null) return;

            CollectionService.RemoveFromCollection(
                SelectedCollectionCard.CollectionEntryId);

            _ownedIds = CollectionService.GetOwnedScryfallIds();
            LoadCollectionGrid();
            RefreshPoolOwnershipColors();
        }

        [RelayCommand]
        private void ExpandDeckUsage(CollectionDisplayRow? row)
        {
            if (row == null) return;

            row.IsExpanded = !row.IsExpanded;

            if (row.IsExpanded)
            {
                // Load deck usage (includes Trade Binder)
                row.DeckUsageRows = CollectionService.GetDeckUsage(
                    row.ScryfallId, row.Name);
                row.UsedCount = row.DeckUsageRows.Sum(u => u.Quantity);
            }

            // Update detail panel with usage
            if (row.IsExpanded)
            {
                SelectedCardDeckUsage = new ObservableCollection<DeckUsageRow>(
                    row.DeckUsageRows);
            }
            else
            {
                SelectedCardDeckUsage.Clear();
            }
        }

        [RelayCommand]
        private void ClearFilters()
        {
            FilterWhite = false;
            FilterBlue = false;
            FilterBlack = false;
            FilterRed = false;
            FilterGreen = false;
            FilterColorless = false;
            FilterType = string.Empty;
            FilterSetCode = string.Empty;
            FilterRarity = string.Empty;
            FilterOracleText = string.Empty;
            FilterKeyword = string.Empty;
            FilterArtist = string.Empty;
            FilterName = string.Empty;
            FilterCmcMin = 0;
            FilterCmcMax = 20;
            FilterPriceMin = 0;
            FilterPriceMax = 99999;

            ExecuteApplyFilters();
        }

        [RelayCommand]
        private void Refresh()
        {
            _ownedIds = CollectionService.GetOwnedScryfallIds();
            ExecuteApplyFilters();
        }

        // ══════════════════════════════════════════════════════════════════
        // LEGACY — kept for commands that need immediate refresh
        // ══════════════════════════════════════════════════════════════════

        private void ExecuteApplyFilters()
        {
            RunQueryInBackground();
        }

        private void LoadCollectionGrid()
        {
            RunQueryInBackground();
        }

        // ══════════════════════════════════════════════════════════════════
        // FILTER APPLICATION (server-side EF Core queries)
        // ══════════════════════════════════════════════════════════════════

        private IQueryable<PoolCard> ApplyPoolFilters(IQueryable<PoolCard> query)
        {
            // Color identity filters
            var colorFilters = new List<string>();
            if (FilterWhite) colorFilters.Add("W");
            if (FilterBlue) colorFilters.Add("U");
            if (FilterBlack) colorFilters.Add("B");
            if (FilterRed) colorFilters.Add("R");
            if (FilterGreen) colorFilters.Add("G");

            if (colorFilters.Count > 0)
            {
                // Cards must contain ALL selected colors
                foreach (var color in colorFilters)
                {
                    query = query.Where(c => c.ColorIdentity.Contains(color));
                }
            }

            if (FilterColorless)
            {
                query = query.Where(c =>
                    c.ColorIdentity == "" || c.ColorIdentity == "C");
            }

            // Name filter
            if (!string.IsNullOrWhiteSpace(FilterName))
            {
                var name = FilterName.Trim();
                query = query.Where(c => c.Name.Contains(name));
            }

            // Type filter
            if (!string.IsNullOrWhiteSpace(FilterType))
            {
                var type = FilterType.Trim();
                query = query.Where(c => c.TypeLine.Contains(type));
            }

            // Set filter
            if (!string.IsNullOrWhiteSpace(FilterSetCode))
            {
                var set = FilterSetCode.Trim();
                query = query.Where(c => c.SetCode == set);
            }

            // Rarity filter
            if (!string.IsNullOrWhiteSpace(FilterRarity))
            {
                var rarity = FilterRarity.Trim().ToLower();
                query = query.Where(c => c.Rarity == rarity);
            }

            // CMC range
            if (FilterCmcMin > 0)
                query = query.Where(c => c.ManaValue >= FilterCmcMin);
            if (FilterCmcMax < 20)
                query = query.Where(c => c.ManaValue <= FilterCmcMax);

            // Price range
            if (FilterPriceMin > 0)
                query = query.Where(c => c.PriceUsd >= FilterPriceMin);
            if (FilterPriceMax < 99999)
                query = query.Where(c => c.PriceUsd <= FilterPriceMax);

            // Oracle text
            if (!string.IsNullOrWhiteSpace(FilterOracleText))
            {
                var text = FilterOracleText.Trim();
                query = query.Where(c => c.OracleText.Contains(text));
            }

            // Keyword
            if (!string.IsNullOrWhiteSpace(FilterKeyword))
            {
                var kw = FilterKeyword.Trim();
                query = query.Where(c => c.Keywords.Contains(kw));
            }

            // Artist
            if (!string.IsNullOrWhiteSpace(FilterArtist))
            {
                var artist = FilterArtist.Trim();
                query = query.Where(c => c.Artist.Contains(artist));
            }

            // Default sort: by name
            query = query.OrderBy(c => c.Name);

            return query;
        }

        // ══════════════════════════════════════════════════════════════════
        // MAPPING — Pool/Collection → DisplayRow
        // ══════════════════════════════════════════════════════════════════

        private CollectionDisplayRow MapPoolToDisplayRow(PoolCard p, int idx)
        {
            bool isOwned = _ownedIds.Contains(p.ScryfallId);

            return new CollectionDisplayRow
            {
                RowIndex = idx,
                ScryfallId = p.ScryfallId,
                PoolId = p.PoolId,
                RowTableType = TableType.Pool,
                Name = p.Name,
                SetCode = p.SetCode,
                SetName = p.SetName,
                CollectorNumber = p.CollectorNumber,
                ColorIdentity = p.ColorIdentity,
                Colors = p.Colors,
                TypeLine = p.TypeLine,
                ManaCost = p.ManaCost,
                ManaValue = p.ManaValue,
                Power = p.Power,
                Toughness = p.Toughness,
                OracleText = p.OracleText,
                FlavorText = p.FlavorText,
                Artist = p.Artist,
                Rarity = p.Rarity,
                IsFoil = p.IsFoil,
                IsNonFoil = p.IsNonFoil,
                ImageNormalUrl = p.ImageNormalUrl,
                ImageBackUrl = p.ImageBackUrl ?? string.Empty,
                LocalImagePath = p.LocalImagePath,
                LocalImageBackPath = p.LocalImageBackPath ?? string.Empty,
                Keywords = p.Keywords ?? string.Empty,
                PriceUsd = p.PriceUsd,
                PriceUsdFoil = p.PriceUsdFoil,
                LegalitiesJson = p.LegalitiesJson ?? string.Empty,
            };
        }

        private CollectionDisplayRow MapCollectionEntryToDisplayRow(
            CollectionEntry c, int idx)
        {
            return new CollectionDisplayRow
            {
                RowIndex = idx,
                CollectionEntryId = c.CollectionEntryId,
                ScryfallId = c.ScryfallId,
                PoolId = c.PoolId,
                RowTableType = TableType.Collection,
                Name = c.Name,
                SetCode = c.SetCode,
                SetName = c.SetName,
                CollectorNumber = c.CollectorNumber,
                ColorIdentity = c.ColorIdentity,
                Colors = c.Colors,
                TypeLine = c.TypeLine,
                ManaCost = c.ManaCost,
                ManaValue = c.ManaValue,
                Power = c.Power,
                Toughness = c.Toughness,
                OracleText = c.OracleText,
                FlavorText = c.FlavorText,
                Artist = c.Artist,
                Rarity = c.Rarity,
                IsFoil = c.IsFoilAvailable,
                IsNonFoil = c.IsNonFoilAvailable,
                ImageNormalUrl = c.ImageNormalUrl,
                ImageBackUrl = c.ImageBackUrl,
                LocalImagePath = c.LocalImagePath,
                LocalImageBackPath = c.LocalImageBackPath,
                Keywords = c.Keywords,
                Quantity = c.Quantity,
                FoilQuantity = c.FoilQuantity,
                Condition = c.Condition,
                Language = c.Language,
                StorageLocation = c.StorageLocation,
                Notes = c.Notes,
                IsFavorite = c.IsFavorite,
                PriceUsd = c.PriceUsd,
                PriceUsdFoil = c.PriceUsdFoil,
                BuyAt = c.BuyAt,
                SellAt = c.SellAt,
                SellAtValue = c.SellAtValue,
                PriceHigh = c.PriceHigh,
                MarketValue = c.MarketValue,
                PriceLow = c.PriceLow,
                Needed = c.Needed,
                Excess = c.Excess,
                Target = c.Target,
                Desired = c.Desired,
                CardGroup = c.CardGroup,
                PrintType = c.PrintType,
                BuyStatus = c.BuyStatus,
                SellStatus = c.SellStatus,
                DateAdded = c.DateAdded,
                DateModified = c.DateModified,
                UsedCount = c.UsedCount,
                LegalitiesJson = c.LegalitiesJson,
            };
        }

        private CollectionDisplayRow MapTradeBinderToDisplayRow(
            TradeBinderEntry t, int idx)
        {
            return new CollectionDisplayRow
            {
                RowIndex = idx,
                CollectionEntryId = t.TradeBinderEntryId,
                ScryfallId = t.ScryfallId,
                PoolId = t.PoolId,
                RowTableType = TableType.TradeBinder,
                Name = t.Name,
                SetCode = t.SetCode,
                SetName = t.SetName,
                CollectorNumber = t.CollectorNumber,
                ColorIdentity = t.ColorIdentity,
                Colors = t.Colors,
                TypeLine = t.TypeLine,
                ManaCost = t.ManaCost,
                ManaValue = t.ManaValue,
                Power = t.Power,
                Toughness = t.Toughness,
                OracleText = t.OracleText,
                FlavorText = t.FlavorText,
                Artist = t.Artist,
                Rarity = t.Rarity,
                IsFoil = t.IsFoilAvailable,
                IsNonFoil = t.IsNonFoilAvailable,
                ImageNormalUrl = t.ImageNormalUrl,
                LocalImagePath = t.LocalImagePath,
                Quantity = t.Quantity,
                Condition = t.Condition,
                PriceUsd = t.PriceUsd,
                PriceUsdFoil = t.PriceUsdFoil,
                DateAdded = t.DateAdded,
            };
        }

        private CollectionDisplayRow MapWantListToDisplayRow(
            WantListEntry w, int idx)
        {
            return new CollectionDisplayRow
            {
                RowIndex = idx,
                CollectionEntryId = w.WantListEntryId,
                ScryfallId = w.ScryfallId,
                PoolId = w.PoolId,
                RowTableType = TableType.WantList,
                Name = w.Name,
                SetCode = w.SetCode,
                SetName = w.SetName,
                CollectorNumber = w.CollectorNumber,
                ColorIdentity = w.ColorIdentity,
                Colors = w.Colors,
                TypeLine = w.TypeLine,
                ManaCost = w.ManaCost,
                ManaValue = w.ManaValue,
                Power = w.Power,
                Toughness = w.Toughness,
                OracleText = w.OracleText,
                FlavorText = w.FlavorText,
                Artist = w.Artist,
                Rarity = w.Rarity,
                IsFoil = w.IsFoilAvailable,
                IsNonFoil = w.IsNonFoilAvailable,
                ImageNormalUrl = w.ImageNormalUrl,
                LocalImagePath = w.LocalImagePath,
                Quantity = w.Quantity,
                PriceUsd = w.PriceUsd,
                PriceUsdFoil = w.PriceUsdFoil,
                DateAdded = w.DateAdded,
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════

        private void RefreshPoolOwnershipColors()
        {
            // Update row coloring without full reload
            foreach (var row in PoolCards)
            {
                // RowTableType color logic in the view will check _ownedIds
                // For now, trigger a visual refresh via property change
            }
        }

        private List<string> LoadPoolSetsBackground()
        {
            try
            {
                using var db = new AppDbContext();
                var sets = db.PoolCards
                    .Select(c => c.SetCode)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                return new List<string>(new[] { "" }.Concat(sets));
            }
            catch
            {
                return new List<string> { "" };
            }
        }
    }
}