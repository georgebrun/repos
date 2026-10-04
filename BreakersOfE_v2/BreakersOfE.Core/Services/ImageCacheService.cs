using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Card pictures: kept on disk (CardImages) so they show OFFLINE, or
    /// fetched from the internet just for showing.
    ///
    /// What's kept (Settings → Card pictures): pictures of YOUR cards
    /// (collection, Trade Binder, Want List, decks — downloaded in the
    /// background by <see cref="PictureSync"/>), and optionally every
    /// picture you look at. Pool pictures are otherwise shown from the
    /// internet and not saved. No picture and no internet: the app shows
    /// its "Card not found" picture.
    ///
    /// On-demand card image cache.
    ///
    /// • One file per PRINTING, keyed by ScryfallId: CardImages\{ScryfallId}.jpg
    ///   (v1 keyed by card name, so every printing of Forest shared one image —
    ///   do not repeat that.)
    /// • Downloads only when a card is actually viewed, never twice.
    /// • Throttled to a few concurrent downloads; exposes a pending counter
    ///   for the "N downloads in progress" indicator.
    /// • Writes to a .tmp file then moves, so a cancelled/failed download never
    ///   leaves a half-written image in the cache.
    /// </summary>
    public static class ImageCacheService
    {
        private static readonly HttpClient _http = CreateClient();
        private static readonly SemaphoreSlim _gate = new(6);
        private static readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();
        private static int _pending;

        /// <summary>Raised (on a background thread) when the pending count changes.</summary>
        public static event Action<int>? PendingChanged;

        public static int Pending => _pending;

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.Add("User-Agent", "BreakersOfE/2.0");
            http.DefaultRequestHeaders.Add("Accept", "image/*");
            return http;
        }

        /// <summary>
        /// Should this printing's picture be kept on disk? Yours (when keeping
        /// your cards' pictures is on), or any (when keeping everything you
        /// look at is on).
        /// </summary>
        public static bool ShouldKeep(string? scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return false;
            var s = AppSettingsService.Current;
            return s.SaveViewedPictures || (s.SaveMyPictures && MyCardsService.Contains(scryfallId));
        }

        /// <summary>
        /// A picture to SHOW: from disk when kept, else (kept or not, per
        /// <see cref="ShouldKeep"/>) from the internet. Null when there's no
        /// picture and no connection. Decoded at the given width.
        /// </summary>
        public static async Task<BitmapImage?> GetPictureAsync(string? scryfallId, string? url, int decodePixelWidth = 0)
        {
            string? path = GetCachedPath(scryfallId);
            if (path == null && ShouldKeep(scryfallId))
            {
                path = await EnsureCachedAsync(scryfallId, url).ConfigureAwait(false);
                if (path == null) return null;                // couldn't download it (offline): don't try twice
            }
            if (path != null)
                return await Task.Run(() => LoadBitmap(path, decodePixelWidth)).ConfigureAwait(false);

            byte[]? bytes = await FetchAsync(url).ConfigureAwait(false);
            return bytes == null ? null : await Task.Run(() => LoadBitmap(bytes, decodePixelWidth)).ConfigureAwait(false);
        }

        private static readonly ConcurrentDictionary<string, Task<byte[]?>> _fetching = new();

        /// <summary>Download a picture into memory only (not saved). Null when offline / failed.</summary>
        private static Task<byte[]?> FetchAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<byte[]?>(null);
            return _fetching.GetOrAdd(url, async u =>
            {
                PendingChanged?.Invoke(Interlocked.Increment(ref _pending));
                try
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try { return await _http.GetByteArrayAsync(u).ConfigureAwait(false); }
                    finally { _gate.Release(); }
                }
                catch { return null; }
                finally
                {
                    _fetching.TryRemove(u, out _);
                    PendingChanged?.Invoke(Interlocked.Decrement(ref _pending));
                }
            });
        }

        /// <summary>Space the kept pictures take, and how many there are.</summary>
        public static (long Bytes, int Files) CacheSize() => DataFolderMover.Measure(AppFolderService.CardImagesFolder);

        /// <summary>Delete every kept picture (they come back as needed / by the background download).</summary>
        public static int ClearCache()
        {
            int n = 0;
            foreach (var f in Directory.EnumerateFiles(AppFolderService.CardImagesFolder))
            {
                try { File.Delete(f); n++; } catch { /* in use: next time */ }
            }
            return n;
        }

        public static string CachePathFor(string scryfallId) =>
            Path.Combine(AppFolderService.CardImagesFolder, $"{scryfallId}.jpg");

        /// <summary>Cached file path if this printing's image is on disk, else null.</summary>
        public static string? GetCachedPath(string? scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return null;
            string path = CachePathFor(scryfallId);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Returns the cached path, downloading first if needed.
        /// Concurrent requests for the same card share one download.
        /// Returns null if there's no URL or the download fails (e.g. offline).
        /// </summary>
        public static Task<string?> EnsureCachedAsync(string? scryfallId, string? url)
        {
            if (string.IsNullOrWhiteSpace(scryfallId))
                return Task.FromResult<string?>(null);

            string path = CachePathFor(scryfallId);
            if (File.Exists(path))
                return Task.FromResult<string?>(path);

            if (string.IsNullOrWhiteSpace(url))
                return Task.FromResult<string?>(null);

            return _inFlight.GetOrAdd(scryfallId, _ => DownloadAsync(scryfallId, url, path));
        }

        private static async Task<string?> DownloadAsync(string scryfallId, string url, string path)
        {
            PendingChanged?.Invoke(Interlocked.Increment(ref _pending));
            try
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    byte[] bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                    string tmp = path + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
                    File.Move(tmp, path, overwrite: true);
                    return path;
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch
            {
                return null;   // offline or failed — caller shows placeholder, retries next view
            }
            finally
            {
                _inFlight.TryRemove(scryfallId, out _);
                PendingChanged?.Invoke(Interlocked.Decrement(ref _pending));
            }
        }

        /// <summary>
        /// Loads a frozen bitmap from disk, decoded at the given width to keep
        /// memory low for gallery thumbnails. Safe to call off the UI thread.
        /// </summary>
        public static BitmapImage? LoadBitmap(byte[] bytes, int decodePixelWidth = 0)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapImage? LoadBitmap(string path, int decodePixelWidth = 0)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}