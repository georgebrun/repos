using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BreakersOfE.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Keeps card pictures on disk for offline use:
    /// • YOUR cards (collection, Trade Binder, Want List, decks) — in the
    ///   background, every few minutes while the app runs, so a card you add
    ///   gets its picture soon after; offline it simply tries again later
    ///   (catching up once you're back online).
    /// • EVERY card — on request (Settings → Download All Pictures), with a
    ///   size warning first.
    /// Downloads are throttled (a few at a time) and never saved half-written.
    /// </summary>
    public static class PictureSync
    {
        public sealed record Progress(int Done, int Total, int Failed);

        /// <summary>Last background pass: "1,234 of your 5,342 cards have pictures saved".</summary>
        public static string Status { get; private set; } = "";

        /// <summary>How many of your cards' pictures are kept (counts only — downloads nothing).</summary>
        public static void CheckStatus()
        {
            var mine = MyCardsService.All();
            var urls = PictureUrls();
            var wanted = mine.Where(urls.ContainsKey).ToList();
            SetStatus(wanted.Count, wanted.Count(id => ImageCacheService.GetCachedPath(id) != null), "");
        }

        /// <summary>Raised (background thread) when <see cref="Status"/> changes.</summary>
        public static event Action? StatusChanged;

        private static CancellationTokenSource? _loop;
        private static int _running;

        // ── A download the user started in Settings (outlives the page) ──
        private static CancellationTokenSource? _manual;

        /// <summary>A Settings download ("my cards now" / "all") is running.</summary>
        public static bool ManualRunning => _manual != null;

        /// <summary>What the Settings download is doing ("Downloading all pictures: 120 of 9,000"), or its result.</summary>
        public static string ManualText { get; private set; } = "";

        /// <summary>Raised (any thread) as the Settings download moves on or ends.</summary>
        public static event Action<Progress?>? ManualProgress;

        /// <summary>Stop the Settings download (what's saved stays).</summary>
        public static void StopManual() => _manual?.Cancel();

        /// <summary>
        /// Run a Settings download: <paramref name="all"/> = every card's picture,
        /// else your cards'. Only one at a time (returns false when one is running).
        /// </summary>
        public static bool StartManual(bool all)
        {
            if (_manual != null) return false;
            var cts = new CancellationTokenSource();
            _manual = cts;
            string what = all ? "all pictures" : "your cards' pictures";
            var progress = new System.Progress<Progress>(p =>
            {
                if (_manual != cts) return;                 // a late report after the run ended: keep the result text
                ManualText = $"Downloading {what}: {p.Done:N0} of {p.Total:N0}" + (p.Failed > 0 ? $" ({p.Failed:N0} not available)" : "");
                ManualProgress?.Invoke(p);
            });
            ManualText = $"Getting ready to download {what}…";
            ManualProgress?.Invoke(null);
            Task.Run(async () =>
            {
                try
                {
                    if (!all)
                        while (Volatile.Read(ref _running) != 0)       // the background pass is busy: wait for it
                            await Task.Delay(500, cts.Token).ConfigureAwait(false);
                    int saved = all ? await DownloadAllAsync(progress, cts.Token).ConfigureAwait(false)
                                    : await SyncMyCardsAsync(progress, cts.Token).ConfigureAwait(false);
                    _manual = null;
                    ManualText = saved > 0 ? $"Saved {saved:N0} pictures." : "Nothing new to download (or you're offline — try again later).";
                }
                catch (OperationCanceledException) { _manual = null; ManualText = all ? "Stopped. Download All carries on from here next time." : "Stopped."; }
                catch (Exception ex) { _manual = null; ManualText = $"Could not download: {ex.Message}"; }
                finally
                {
                    _manual = null;
                    ManualProgress?.Invoke(null);
                }
            });
            return true;
        }

        // ── Background: your cards ───────────────────────────────────────
        /// <summary>Start the background pass (every few minutes; first one shortly after startup).</summary>
        public static void StartBackground()
        {
            if (_loop != null) return;
            _loop = new CancellationTokenSource();
            var ct = _loop.Token;
            Task.Run(() => MyCardsService.All());                    // ready before the first gallery asks
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);   // let the app settle
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        if (AppSettingsService.Current.SaveMyPictures)
                            await SyncMyCardsAsync(null, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Picture sync: {ex.Message}"); }
                    try { await Task.Delay(TimeSpan.FromMinutes(3), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }, ct);
        }

        public static void StopBackground()
        {
            _loop?.Cancel();
            _loop = null;
        }

        /// <summary>
        /// Save the missing pictures of your cards. Stops early when offline
        /// (several downloads in a row fail) — the next pass tries again.
        /// Returns how many were saved.
        /// </summary>
        public static async Task<int> SyncMyCardsAsync(IProgress<Progress>? progress, CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return 0;   // one pass at a time
            try
            {
                var mine = MyCardsService.All();
                var urls = PictureUrls();
                // Only printings with a picture to get (not in the card pool → nothing to download).
                var wanted = mine.Where(urls.ContainsKey).ToList();
                var missing = wanted.Where(id => ImageCacheService.GetCachedPath(id) == null).ToList();
                SetStatus(wanted.Count, wanted.Count - missing.Count, missing.Count > 0 ? "downloading…" : "");
                if (missing.Count == 0) return 0;
                int saved = await DownloadAsync(missing, progress, ct, stopWhenOffline: true, urls).ConfigureAwait(false);
                int have = wanted.Count(id => ImageCacheService.GetCachedPath(id) != null);
                SetStatus(wanted.Count, have, have < wanted.Count ? "the rest when online" : "");
                return saved;
            }
            finally
            {
                _running = 0;
            }
        }

        private static void SetStatus(int total, int have, string note)
        {
            Status = total == 0
                ? "No cards of yours yet."
                : $"{have:N0} of your {total:N0} card printings have pictures saved" + (note.Length > 0 ? $" ({note})." : ".");
            StatusChanged?.Invoke();
        }

        // ── Everything ───────────────────────────────────────────────────
        /// <summary>How many pictures "Download All" would fetch, and roughly how much space.</summary>
        public static (int Count, long Bytes) EstimateAll()
        {
            var urls = PictureUrls();
            int missing = urls.Keys.Count(id => ImageCacheService.GetCachedPath(id) == null);
            var (bytes, files) = ImageCacheService.CacheSize();
            long avg = files >= 20 ? bytes / files : 120 * 1024;     // a typical card picture is ~100–150 KB
            return (missing, missing * avg);
        }

        /// <summary>Download every card picture not on disk yet (stoppable; continues where it left off next time).</summary>
        public static async Task<int> DownloadAllAsync(IProgress<Progress>? progress, CancellationToken ct)
        {
            // The background pass waits while this runs (one bulk download at a time).
            while (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                await Task.Delay(500, ct).ConfigureAwait(false);
            try
            {
                var urls = PictureUrls();
                var missing = urls.Keys.Where(id => ImageCacheService.GetCachedPath(id) == null).ToList();
                return await DownloadAsync(missing, progress, ct, stopWhenOffline: true, urls).ConfigureAwait(false);
            }
            finally
            {
                _running = 0;
            }
        }

        // ── Shared ───────────────────────────────────────────────────────
        private static async Task<int> DownloadAsync(List<string> ids, IProgress<Progress>? progress, CancellationToken ct,
                                                     bool stopWhenOffline, Dictionary<string, string>? urls = null)
        {
            urls ??= PictureUrls();
            ids = ids.Where(urls.ContainsKey).ToList();               // no picture link: nothing to get
            int done = 0, failed = 0, saved = 0, failedInRow = 0;
            const int batch = 3;                                      // half the picture slots: the gallery keeps the rest
            for (int i = 0; i < ids.Count; i += batch)
            {
                ct.ThrowIfCancellationRequested();
                var part = ids.Skip(i).Take(batch).ToList();
                var results = await Task.WhenAll(part.Select(id => ImageCacheService.EnsureCachedAsync(id, urls[id])))
                    .ConfigureAwait(false);
                foreach (var r in results)
                {
                    done++;
                    if (r != null) { saved++; failedInRow = 0; }
                    else { failed++; failedInRow++; }
                }
                progress?.Report(new Progress(done, ids.Count, failed));
                if (stopWhenOffline && failedInRow >= 15) break;     // five batches in a row failed: offline
            }
            return saved;
        }

        /// <summary>Picture link of every printing in the card pool (all card kinds).</summary>
        private static Dictionary<string, string> PictureUrls()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(IEnumerable<(string Id, string Url)> rows)
            {
                foreach (var (id, url) in rows)
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(url)) map.TryAdd(id, url);
            }
            try
            {
                using var db = new AppDbContext();
                Add(db.PoolCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.TokenCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.PlanarCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.SchemeCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.VanguardCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.ArtSeriesCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
                Add(db.ConspiracyCards.AsNoTracking().Select(c => new { c.ScryfallId, c.ImageNormalUrl }).AsEnumerable().Select(c => (c.ScryfallId, c.ImageNormalUrl)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Picture links: {ex.Message}");
            }
            return map;
        }
    }
}
