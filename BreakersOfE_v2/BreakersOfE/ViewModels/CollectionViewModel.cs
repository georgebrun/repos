using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// CollectionViewModel — simplified like v1.
    /// 
    /// LoadCaches() pulls ALL data into memory once.
    /// PoolCard IS the grid row — no mapping layer.
    /// FilterDataGrid handles column filtering in the view.
    /// Search bar scrolls to match (NEVER filters rows) — handled in code-behind.
    /// </summary>
    public partial class CollectionViewModel : ObservableObject
    {
        // ── In-memory caches (loaded once, exactly like v1) ────────────
        private List<PoolCard> _poolCache = new();
        private List<TokenCard> _tokenCache = new();
        private List<PlanarCard> _planarCache = new();
        private List<SchemeCard> _schemeCache = new();
        private List<VanguardCard> _vanguardCache = new();
        private List<ArtSeriesCard> _artSeriesCache = new();
        private List<ConspiracyCard> _conspiracyCache = new();

        // ── Ownership cache for pool row coloring ──────────────────────
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

        // ── Pool Grid — binds directly to PoolCard ─────────────────────
        [ObservableProperty]
        private List<PoolCard> poolCards = new();

        [ObservableProperty]
        private PoolCard? selectedPoolCard;

        // ── Search ─────────────────────────────────────────────────────
        [ObservableProperty]
        private string searchText = string.Empty;

        // ── Status Bar ─────────────────────────────────────────────────
        [ObservableProperty]
        private string poolStatusText = "Loading...";

        // ── Loading ────────────────────────────────────────────────────
        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string loadingText = string.Empty;

        // ══════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════

        public CollectionViewModel()
        {
            // Don't load data at design time — VS designer instantiates this
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(
                    new System.Windows.DependencyObject()))
                return;

            IsLoading = true;
            LoadingText = "Loading card pool...";

            Task.Run(() =>
            {
                LoadCaches();

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    PoolCards = _poolCache;
                    PoolStatusText = $"{_poolCache.Count:N0} cards in pool";
                    IsLoading = false;
                    LoadingText = string.Empty;

                    System.Diagnostics.Debug.WriteLine(
                        $"CollectionVM: Pool loaded — {_poolCache.Count:N0} cards");
                });
            });
        }

        // ══════════════════════════════════════════════════════════════════
        // LOAD CACHES — one-time bulk load, exactly like v1
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads ALL card data into memory. Runs on a background thread.
        /// Each table is loaded once with AsNoTracking() — no change tracking overhead.
        /// PoolCard IS the grid row — zero mapping, zero copying.
        /// </summary>
        private void LoadCaches()
        {
            try
            {
                using var db = new AppDbContext();

                _poolCache = db.PoolCards.AsNoTracking()
                    .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetCode)
                    .ToList();

                // Assign row indices for alternating row colors
                for (int i = 0; i < _poolCache.Count; i++)
                    _poolCache[i].RowIndex = i;

                _tokenCache = db.TokenCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();
                _planarCache = db.PlanarCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();
                _schemeCache = db.SchemeCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();
                _vanguardCache = db.VanguardCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();
                _artSeriesCache = db.ArtSeriesCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();
                _conspiracyCache = db.ConspiracyCards.AsNoTracking()
                    .OrderBy(c => c.Name).ToList();

                // Build ownership set for pool row coloring
                _ownedIds = CollectionService.GetOwnedScryfallIds();

                System.Diagnostics.Debug.WriteLine(
                    $"LoadCaches complete: {_poolCache.Count:N0} pool, " +
                    $"{_tokenCache.Count:N0} tokens, " +
                    $"{_planarCache.Count:N0} planar, " +
                    $"{_schemeCache.Count:N0} schemes, " +
                    $"{_vanguardCache.Count:N0} vanguard, " +
                    $"{_artSeriesCache.Count:N0} art series, " +
                    $"{_conspiracyCache.Count:N0} conspiracy");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"LoadCaches error: {ex.Message}");

                // Empty pool on error — page will show "0 cards"
                _poolCache = new List<PoolCard>();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // CARD SELECTION — detail panel binds directly to SelectedPoolCard
        // ══════════════════════════════════════════════════════════════════

        // No UpdateDetailPanel method needed.
        // The XAML binds to SelectedPoolCard.Name, SelectedPoolCard.TypeLine, etc.
        // PropertyChanged fires automatically via [ObservableProperty].

        // ══════════════════════════════════════════════════════════════════
        // COMMANDS
        // ══════════════════════════════════════════════════════════════════

        [RelayCommand]
        private void AddToCollection()
        {
            if (SelectedPoolCard == null) return;

            CollectionService.AddToCollection(SelectedPoolCard);
            _ownedIds = CollectionService.GetOwnedScryfallIds();
        }

        [RelayCommand]
        private void AddToCollectionFoil()
        {
            if (SelectedPoolCard == null) return;

            CollectionService.AddToCollection(SelectedPoolCard, isFoil: true);
            _ownedIds = CollectionService.GetOwnedScryfallIds();
        }

        [RelayCommand]
        private void AddToTradeBinder()
        {
            if (SelectedPoolCard == null) return;

            CollectionService.AddToTradeBinder(SelectedPoolCard);
        }

        [RelayCommand]
        private void AddToWantList()
        {
            if (SelectedPoolCard == null) return;

            CollectionService.AddToWantList(SelectedPoolCard);
        }

        [RelayCommand]
        private void Refresh()
        {
            IsLoading = true;
            LoadingText = "Refreshing...";

            Task.Run(() =>
            {
                LoadCaches();

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    PoolCards = _poolCache;
                    PoolStatusText = $"{_poolCache.Count:N0} cards in pool";
                    IsLoading = false;
                    LoadingText = string.Empty;
                });
            });
        }
    }
}