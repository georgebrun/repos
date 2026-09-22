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