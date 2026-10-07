using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BreakersOfE.Services;
using BreakersOfE.Data;
using BreakersOfE.Validation;
using Microsoft.EntityFrameworkCore;

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

        /// <summary>Cancel button: on while an update runs, off once Cancel is clicked.</summary>
        [ObservableProperty]
        private bool canCancel;

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
        /// Full database update + rulings download in one click.
        /// </summary>
        [RelayCommand]
        private async Task StartFullUpdateWithRulings()
        {
            await StartFullUpdate();
            // Only continue to rulings if the full update succeeded (a flag, not the
            // status text: a late progress report could have changed the text).
            if (_fullUpdateOk)
                await DownloadRulings();
        }

        private bool _fullUpdateOk;

        /// <summary>
        /// Full database update: download bulk data → import all cards →
        /// update prices → download set/mana symbols.
        /// Keyword dictionary rebuilds in the background after completion.
        /// </summary>
        [RelayCommand]
        private async Task StartFullUpdate()
        {
            _fullUpdateOk = false;
            if (!PreflightCheck()) return;

            IsRunning = true;
            CanStart = false;
            CanCancel = true;
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
                // OFF the UI thread, so the window stays responsive and Cancel
                // works. Skip keywords — they run in the background afterwards.
                var token = _cts.Token;
                var result = await Task.Run(() => _scryfall.RunFullUpdateAsync(progress, token,
                    skipKeywords: true));

                if (result.Success)
                {
                    // Propagate prices to collection
                    CanCancel = false;                 // the card database is already saved
                    StatusText = "Propagating prices to collection…";
                    ProgressPercent = 95;
                    await Task.Run(PropagatePoolPricesToCollection);
                    StatusText = "Updating card text in your collection…";
                    await Task.Run(RefreshCollectionCardText);

                    StatusText = "Update complete!";
                    ProgressPercent = 100;
                    DetailText = BuildCompletionSummary(result);

                    _coordinator.RecordPoolUpdate();
                    _coordinator.RecordPriceUpdate();
                    _fullUpdateOk = true;
                    // Symbols may have been re-downloaded: draw them fresh.
                    Services.ImageSourceConverter.ClearCache();
                    Services.ManaCostConverter.ClearCache();

                    // Fire keyword rebuild in background (low priority)
                    _ = Task.Run(() => RebuildKeywordDictionaryBackground(), CancellationToken.None);
                }
                else if (result.Cancelled)
                {
                    StatusText = "Update cancelled.";
                    DetailText = result.ErrorMessage;
                    ProgressPercent = 0;
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
                DetailText = "Your card database was not changed.";
                ProgressPercent = 0;
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
                CanCancel = false;
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
            CanCancel = true;
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

                // Off the UI thread (responsive window, working Cancel).
                var token = _cts.Token;
                var result = await Task.Run(() => _scryfall.RunPriceUpdateAsync(progress, token));

                if (result.Success)
                {
                    CanCancel = false;                 // the card database is already saved
                    StatusText = "Propagating prices to collection…";
                    ProgressPercent = 95;
                    await Task.Run(PropagatePoolPricesToCollection);

                    StatusText = "Price update complete!";
                    ProgressPercent = 100;
                    _coordinator.RecordPriceUpdate();
                }
                else if (result.Cancelled)
                {
                    StatusText = "Price update cancelled.";
                    DetailText = result.ErrorMessage;
                    ProgressPercent = 0;
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
                DetailText = "Your card database was not changed.";
                ProgressPercent = 0;
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
                CanCancel = false;
                _coordinator.SetIdle();
                RefreshTimestamps();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Cancel the current update. The button greys out and the status
        /// shows "Cancelling..." until the update has actually stopped.
        /// </summary>
        [RelayCommand]
        private void Cancel()
        {
            if (_cts == null || !CanCancel) return;
            CanCancel = false;
            _cts.Cancel();
            StatusText = "Cancelling…";
            DetailText = "Stopping safely — your databases are not being changed.";
        }

        /// <summary>
        /// Download all card rulings from Scryfall into rulings.db.
        /// Separate from the main update — optional, larger download.
        /// </summary>
        [RelayCommand]
        private async Task DownloadRulings()
        {
            if (!PreflightCheck()) return;

            IsRunning = true;
            CanStart = false;
            CanCancel = true;
            _cts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<ImportProgress>(p =>
                {
                    ProgressPercent = p.Percentage;
                    StatusText = p.Step;
                    DetailText = p.Detail;
                });

                // Off the UI thread (responsive window, working Cancel).
                var token = _cts.Token;
                int count = await Task.Run(() => _scryfall.DownloadRulingsAsync(progress, token));
                StatusText = "Rulings download complete!";
                DetailText = $"{count:N0} rulings imported into rulings.db";
                ProgressPercent = 100;
            }
            catch (OperationCanceledException)
            {
                StatusText = "Rulings download cancelled.";
                DetailText = "Your rulings were not changed.";
                ProgressPercent = 0;
            }
            catch (Exception ex)
            {
                StatusText = "Rulings download failed.";
                DetailText = ex.Message;
            }
            finally
            {
                IsRunning = false;
                CanStart = true;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
            }
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

                // Price lookup from the pool: prices + which finishes exist.
                var priceLookup = poolDb.PoolCards
                    .Where(p => p.ScryfallId != null)
                    .Select(p => new PoolPrice(p.ScryfallId!, p.PriceUsd, p.PriceUsdFoil, p.PriceUsdEtched, p.IsFoil, p.IsEtched))
                    .ToList()
                    .GroupBy(p => p.ScryfallId)
                    .ToDictionary(g => g.Key, g => g.First());

                // Each row gets the price of ITS finish (CardFinish.PriceFor, the
                // one price rule). A v1 foil row of an etched-only printing is
                // etched (CardFinish.Shown), so it gets the etched price.
                static decimal? RowPrice(string? finish, PoolPrice p) =>
                    Models.CardFinish.PriceFor(
                        Models.CardFinish.Shown(finish, p.IsEtched && !p.IsFoil),
                        p.PriceUsd, p.PriceUsdFoil, p.PriceUsdEtched);

                foreach (var entry in colDb.CollectionEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                        entry.PriceUsdEtched = prices.PriceUsdEtched;
                        entry.Price = RowPrice(entry.Finish, prices);
                    }
                }

                foreach (var entry in colDb.TradeBinderEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                        entry.Price = RowPrice(entry.Finish, prices);
                    }
                }

                foreach (var entry in colDb.WantListEntries)
                {
                    if (entry.ScryfallId != null &&
                        priceLookup.TryGetValue(entry.ScryfallId, out var prices))
                    {
                        entry.PriceUsd = prices.PriceUsd;
                        entry.PriceUsdFoil = prices.PriceUsdFoil;
                        entry.Price = RowPrice(entry.Finish, prices);
                    }
                }

                colDb.SaveChanges();
            }
            catch
            {
                // If collection DB doesn't exist yet, that's fine — skip
            }

            // Online: MTGO rows get the MTGO price in event tickets (the regular
            // version's; Scryfall has no foil tix price). Arena has no prices.
            try
            {
                using var onlineDb = new OnlineDbContext();
                using var colDb = new CollectionDbContext();
                var tix = onlineDb.OnlineCards
                    .Where(c => c.IsOnMtgo)
                    .Select(c => new { c.ScryfallId, c.PriceTix })
                    .ToList()
                    .GroupBy(c => c.ScryfallId)
                    .ToDictionary(g => g.Key, g => g.First().PriceTix);

                foreach (var entry in colDb.OnlineCollectionEntries.Where(e => e.Game == Models.OnlineGame.Mtgo))
                {
                    if (tix.TryGetValue(entry.ScryfallId, out var price))
                        entry.Price = entry.Finish == Models.CardFinish.NonFoil ? price : null;
                }
                colDb.SaveChanges();
            }
            catch
            {
                // No online pool or collection yet — skip
            }
        }

        /// <summary>A printing's text, cost and power / toughness / loyalty, as the card database has them.</summary>
        private sealed record CardText(string ManaCost, string OracleText, string FlavorText,
                                       string Power, string Toughness, string LoyaltyOrDefense);

        /// <summary>
        /// Collection rows keep a copy of each card's text. After a card data
        /// update, bring it in line with the card database, so rows saved before
        /// a fix get it too (two-sided cards were blank: text, cost and power
        /// per side; Battles had no defense). Only changed values are written.
        /// </summary>
        private static void RefreshCollectionCardText()
        {
            static Dictionary<string, CardText> TextOf(IQueryable<Models.PoolCard> cards, List<string> ids)
            {
                var wanted = ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                if (wanted.Count == 0) return new();
                return cards.Where(p => wanted.Contains(p.ScryfallId))
                    .Select(p => new { p.ScryfallId, p.ManaCost, p.OracleText, p.FlavorText, p.Power, p.Toughness, p.LoyaltyOrDefense })
                    .ToList()
                    .GroupBy(p => p.ScryfallId)
                    .ToDictionary(g => g.Key, g =>
                    {
                        var p = g.First();
                        return new CardText(p.ManaCost ?? "", p.OracleText ?? "", p.FlavorText ?? "",
                                            p.Power ?? "", p.Toughness ?? "", p.LoyaltyOrDefense ?? "");
                    });
            }

            try
            {
                using var poolDb = new AppDbContext();
                using var colDb = new CollectionDbContext();

                var ids = colDb.CollectionEntries.Select(e => e.ScryfallId).ToList();
                ids.AddRange(colDb.TradeBinderEntries.Select(e => e.ScryfallId));
                ids.AddRange(colDb.WantListEntries.Select(e => e.ScryfallId));
                var text = TextOf(poolDb.PoolCards.AsNoTracking(), ids);

                foreach (var e in colDb.CollectionEntries)
                    if (e.ScryfallId != null && text.TryGetValue(e.ScryfallId, out var t))
                    {
                        e.ManaCost = t.ManaCost; e.OracleText = t.OracleText; e.FlavorText = t.FlavorText;
                        e.Power = t.Power; e.Toughness = t.Toughness; e.LoyaltyOrDefense = t.LoyaltyOrDefense;
                    }
                foreach (var e in colDb.TradeBinderEntries)
                    if (e.ScryfallId != null && text.TryGetValue(e.ScryfallId, out var t))
                    {
                        e.ManaCost = t.ManaCost; e.OracleText = t.OracleText; e.FlavorText = t.FlavorText;
                        e.Power = t.Power; e.Toughness = t.Toughness;
                    }
                foreach (var e in colDb.WantListEntries)
                    if (e.ScryfallId != null && text.TryGetValue(e.ScryfallId, out var t))
                    {
                        e.ManaCost = t.ManaCost; e.OracleText = t.OracleText; e.FlavorText = t.FlavorText;
                        e.Power = t.Power; e.Toughness = t.Toughness;
                    }

                // Tokens (there are two-sided tokens too).
                var tokenIds = colDb.TokenCollectionEntries.Select(e => e.ScryfallId).ToList();
                var tokenText = poolDb.TokenCards.AsNoTracking()
                    .Where(t => tokenIds.Contains(t.ScryfallId))
                    .Select(t => new { t.ScryfallId, t.OracleText, t.FlavorText, t.Power, t.Toughness })
                    .ToList()
                    .GroupBy(t => t.ScryfallId)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var e in colDb.TokenCollectionEntries)
                    if (e.ScryfallId != null && tokenText.TryGetValue(e.ScryfallId, out var t))
                    {
                        e.OracleText = t.OracleText ?? ""; e.FlavorText = t.FlavorText ?? "";
                        e.Power = t.Power ?? ""; e.Toughness = t.Toughness ?? "";
                    }

                colDb.SaveChanges();
            }
            catch
            {
                // No collection yet — skip
            }

            // Online (MTGO / Arena) rows: from the online card database.
            try
            {
                using var onlineDb = new OnlineDbContext();
                using var colDb = new CollectionDbContext();
                var text = TextOf(onlineDb.OnlineCards.AsNoTracking(),
                                  colDb.OnlineCollectionEntries.Select(e => e.ScryfallId).ToList());
                foreach (var e in colDb.OnlineCollectionEntries)
                    if (e.ScryfallId != null && text.TryGetValue(e.ScryfallId, out var t))
                    {
                        e.ManaCost = t.ManaCost; e.OracleText = t.OracleText; e.FlavorText = t.FlavorText;
                        e.Power = t.Power; e.Toughness = t.Toughness; e.LoyaltyOrDefense = t.LoyaltyOrDefense;
                    }
                colDb.SaveChanges();
            }
            catch
            {
                // No online pool or collection yet — skip
            }
        }

        /// <summary>A pool printing's prices and which finishes it exists in.</summary>
        private sealed record PoolPrice(string ScryfallId, decimal? PriceUsd, decimal? PriceUsdFoil,
                                        decimal? PriceUsdEtched, bool IsFoil, bool IsEtched);

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
                   $"Online (MTGO/Arena): {result.OnlineCardsImported:N0}  " +
                   $"Skipped: {result.SkippedCount:N0}";
        }
    }
}