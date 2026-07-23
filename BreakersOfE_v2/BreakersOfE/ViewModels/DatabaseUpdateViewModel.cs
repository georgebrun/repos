using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BreakersOfE.Services;
using BreakersOfE.Data;
using BreakersOfE.Validation;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// DatabaseUpdateViewModel — owns all logic for the Database Update page.
    /// 
    /// This is your first real MVVM ViewModel. Think of it like a PLC subroutine:
    ///   - The XAML page (View) is the HMI screen — it just displays things
    ///   - This ViewModel is the logic — it does the actual work
    ///   - They're connected via data binding (like tag references)
    /// 
    /// CommunityToolkit.Mvvm magic:
    ///   [ObservableProperty] → auto-generates a public property with change notification
    ///     private string statusText = "" → generates public string StatusText { get; set; }
    ///     and the UI updates automatically when the value changes
    ///   
    ///   [RelayCommand] → auto-generates an ICommand that XAML buttons can bind to
    ///     private async Task StartFullUpdate() → generates StartFullUpdateCommand
    /// </summary>
    public partial class DatabaseUpdateViewModel : ObservableObject
    {
        // ── Services (injected from Core — same code the Agent will use) ──
        private readonly ScryfallService _scryfall;
        private readonly AgentCoordinator _coordinator;
        private CancellationTokenSource? _cts;

        // ── Observable Properties ──────────────────────────────────────────
        // The XAML page binds to these. When we set a value, the UI updates
        // automatically — no manual refresh needed.

        [ObservableProperty]
        private int progressPercent;

        [ObservableProperty]
        private string statusText = "Ready to update.";

        [ObservableProperty]
        private string detailText = "";

        [ObservableProperty]
        private bool isRunning;

        [ObservableProperty]
        private bool canStart = true;

        [ObservableProperty]
        private string schemaWarning = "";

        [ObservableProperty]
        private bool hasSchemaWarning;

        // ── Last Update Info ──────────────────────────────────────────────
        [ObservableProperty]
        private string lastPoolUpdateText = "Never";

        [ObservableProperty]
        private string lastPriceUpdateText = "Never";

        [ObservableProperty]
        private string priceStaleWarning = "";

        // ── Constructor ──────────────────────────────────────────────────
        public DatabaseUpdateViewModel()
        {
            // Create service instances
            // (Future: these will come from DI container)
            _scryfall = new ScryfallService();
            _coordinator = new AgentCoordinator();

            // Load last update timestamps
            RefreshTimestamps();
        }

        // ══════════════════════════════════════════════════════════════════
        // COMMANDS — these are what XAML buttons bind to
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full database update: download bulk data → import all cards →
        /// update prices → download set/mana symbols.
        /// Keyword dictionary rebuilds in the background after completion.
        /// </summary>
        [RelayCommand]
        private async Task StartFullUpdate()
        {
            if (!PreflightCheck()) return;

            IsRunning = true;
            CanStart = false;
            _cts = new CancellationTokenSource();
            _coordinator.SetAppUpdating("updating_pool");

            try
            {
                var progress = new Progress<ImportProgress>(p =>
                {
                    ProgressPercent = p.Percentage;
                    StatusText = p.Step;
                    DetailText = p.Detail;
                });

                // Run the full update (download + import + prices + symbols)
                // Skip keywords — they'll run in the background after completion
                var result = await _scryfall.RunFullUpdateAsync(progress, _cts.Token,
                    skipKeywords: true);

                if (result.Success)
                {
                    // Propagate prices to collection
                    StatusText = "Propagating prices to collection...";
                    ProgressPercent = 95;
                    PropagatePoolPricesToCollection();

                    StatusText = "Update complete!";
                    ProgressPercent = 100;
                    DetailText = BuildCompletionSummary(result);

                    _coordinator.RecordPoolUpdate();
                    _coordinator.RecordPriceUpdate();

                    // Fire keyword rebuild in background (low priority)
                    _ = Task.Run(() => RebuildKeywordDictionaryBackground(), CancellationToken.None);
                }
                else
                {
                    StatusText = "Update failed.";
                    DetailText = result.ErrorMessage;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Update cancelled.";
                DetailText = "";
            }
            catch (Exception ex)
            {
                StatusText = "Update failed.";
                DetailText = ex.Message;

                // Check if it's a schema issue
                if (ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase))
                {
                    SchemaWarning = ex.Message;
                    HasSchemaWarning = true;
                }
            }
            finally
            {
                IsRunning = false;
                CanStart = true;
                _coordinator.SetIdle();
                RefreshTimestamps();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Price-only update: download bulk data → extract prices only →
        /// propagate to collection. Much faster than full update.
        /// </summary>
        [RelayCommand]
        private async Task StartPriceUpdate()
        {
            if (!PreflightCheck()) return;

            IsRunning = true;
            CanStart = false;
            _cts = new CancellationTokenSource();
            _coordinator.SetAppUpdating("updating_prices");

            try
            {
                var progress = new Progress<ImportProgress>(p =>
                {
                    ProgressPercent = p.Percentage;
                    StatusText = p.Step;
                    DetailText = p.Detail;
                });

                var result = await _scryfall.RunPriceUpdateAsync(progress, _cts.Token);

                if (result.Success)
                {
                    StatusText = "Propagating prices to collection...";
                    ProgressPercent = 95;
                    PropagatePoolPricesToCollection();

                    StatusText = "Price update complete!";
                    ProgressPercent = 100;
                    _coordinator.RecordPriceUpdate();
                }
                else
                {
                    StatusText = "Price update failed.";
                    DetailText = result.ErrorMessage;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Price update cancelled.";
            }
            catch (Exception ex)
            {
                StatusText = "Price update failed.";
                DetailText = ex.Message;
            }
            finally
            {
                IsRunning = false;
                CanStart = true;
                _coordinator.SetIdle();
                RefreshTimestamps();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Cancel the current update.
        /// </summary>
        [RelayCommand]
        private void Cancel()
        {
            _cts?.Cancel();
            StatusText = "Cancelling...";
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks if the agent is already running an update.
        /// Returns false (and sets warning text) if we shouldn't start.
        /// </summary>
        private bool PreflightCheck()
        {
            if (_coordinator.IsAgentUpdating())
            {
                StatusText = "The background agent is currently updating. Please wait.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Propagates pool prices to collection, trade binder, and want list.
        /// Moved from MainWindow.xaml.cs — now lives here as shared logic.
        /// </summary>
        private static void PropagatePoolPricesToCollection()
        {
            try
            {
                using var poolDb = new AppDbContext();
                using var colDb = new CollectionDbContext();

                // Build price lookup from pool
                var priceLookup = poolDb.PoolCards
                    .Where(p => p.ScryfallId != null)
                    .ToDictionary(
                        p => p.ScryfallId!,
                        p => new { p.PriceUsd, p.PriceUsdFoil, p.PriceUsdEtched });

                // Update collection entries
                foreach (var entry in colDb.CollectionEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                    }
                }

                // Update trade binder entries
                foreach (var entry in colDb.TradeBinderEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                    }
                }

                // Update want list entries
                foreach (var entry in colDb.WantListEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                    }
                }

                colDb.SaveChanges();
            }
            catch
            {
                // If collection DB doesn't exist yet, that's fine — skip
            }
        }

        /// <summary>
        /// Rebuilds the keyword dictionary in the background.
        /// Runs on a low-priority thread so the user can keep using the app.
        /// </summary>
        private async Task RebuildKeywordDictionaryBackground()
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

                var silentProgress = new Progress<ImportProgress>(_ => { }); // No UI updates
                var result = new ImportResult();
                await _scryfall.FetchAndMergeKeywordCatalogsAsync(
                    result, silentProgress, CancellationToken.None);

                System.Diagnostics.Debug.WriteLine(
                    $"Keyword dictionary rebuild completed (background). " +
                    $"Abilities: {result.KeywordAbilitiesCount}, " +
                    $"Actions: {result.KeywordActionsCount}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Keyword rebuild failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the last-update timestamp display.
        /// </summary>
        private void RefreshTimestamps()
        {
            var status = _coordinator.ReadStatus();

            LastPoolUpdateText = status.LastPoolUpdate.HasValue
                ? status.LastPoolUpdate.Value.ToLocalTime().ToString("g")
                : "Never";

            LastPriceUpdateText = status.LastPriceUpdate.HasValue
                ? status.LastPriceUpdate.Value.ToLocalTime().ToString("g")
                : "Never";

            // Staleness warning
            var sincePrice = _coordinator.TimeSinceLastPriceUpdate();
            if (sincePrice == null)
                PriceStaleWarning = "Prices have never been updated.";
            else if (sincePrice.Value.TotalDays > 7)
                PriceStaleWarning = $"Prices are {(int)sincePrice.Value.TotalDays} days old.";
            else
                PriceStaleWarning = "";
        }

        /// <summary>
        /// Builds a summary string after a successful full update.
        /// </summary>
        private static string BuildCompletionSummary(ImportResult result)
        {
            return $"Pool: {result.PoolCardsImported:N0}  " +
                   $"Tokens: {result.TokenCardsImported:N0}  " +
                   $"Planar: {result.PlanarCardsImported:N0}  " +
                   $"Schemes: {result.SchemeCardsImported:N0}  " +
                   $"Conspiracy: {result.ConspiracyCardsImported:N0}  " +
                   $"Skipped: {result.SkippedCount:N0}";
        }
    }
}