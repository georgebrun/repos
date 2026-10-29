using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Settings → Phone Lists: keeps "BoE Lists.json" up to date. Every few
    /// seconds it looks at when the collection last changed (any change: an
    /// edit, Got It, a deck claiming cards, an import) and, if it moved on,
    /// writes the file again in the background. Cheap when nothing changed.
    /// With Dropbox connected the file goes to Dropbox instead of the folder;
    /// when that fails (no internet) it tries again a minute later.
    /// </summary>
    public static class PhoneListsWatcher
    {
        private static DispatcherTimer? _timer;
        private static DateTime _written = DateTime.MinValue;
        private static bool _busy;
        private static DateTime _retryAt = DateTime.MinValue;

        /// <summary>"Updated 6:42 PM: 34 binder cards, 12 wants." — or what went wrong.</summary>
        public static string Status { get; private set; } = "";
        public static bool LastFailed { get; private set; }
        public static event Action? StatusChanged;

        /// <summary>Start with BoE (MainWindow): write once now, then whenever the collection changes.</summary>
        public static void Start()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += async (_, _) => await UpdateAsync(force: false);
            _timer.Start();
            _ = UpdateAsync(force: true);
        }

        /// <summary>Settings → Update Now, or just turned on / a new folder.</summary>
        public static Task WriteNowAsync() => UpdateAsync(force: true);

        private static async Task UpdateAsync(bool force)
        {
            var s = AppSettingsService.Current;
            if (!s.PhoneListsOn || _busy) return;
            var stamp = PhoneListsService.CollectionStamp();
            if (!force && (stamp == _written || DateTime.UtcNow < _retryAt)) return;
            bool dropbox = DropboxService.Connected;
            string folder = PhoneListsService.Folder;
            if (!dropbox && folder.Length == 0)
            {
                Show("Pick a folder in your cloud drive (OneDrive, Google Drive, iCloud Drive, Dropbox) for the lists.", failed: true);
                return;
            }
            _busy = true;
            try
            {
                int binder, want;
                if (dropbox)
                {
                    var (json, b, w) = await Task.Run(PhoneListsService.Build);
                    await DropboxService.UploadAsync(json);
                    (binder, want) = (b, w);
                }
                else
                {
                    (binder, want) = await Task.Run(() => PhoneListsService.Write(folder));
                }
                _written = stamp;
                _retryAt = DateTime.MinValue;
                Show($"{(dropbox ? "Sent to Dropbox" : "Updated")} {DateTime.Now:t}: {binder} card{(binder == 1 ? "" : "s")} in the Trade Binder, " +
                     $"{want} on the Want List.", failed: false);
            }
            catch (Exception ex)
            {
                _retryAt = DateTime.UtcNow.AddMinutes(1);
                Show(dropbox
                    ? $"Couldn't send the phone lists to Dropbox (trying again in a minute): {Message(ex)}"
                    : $"Couldn't write the phone lists: {ex.Message}", failed: true);
            }
            finally
            {
                _busy = false;
            }
        }

        private static string Message(Exception ex) =>
            ex is System.Net.Http.HttpRequestException or TaskCanceledException
                ? "no connection to Dropbox."
                : ex.Message;

        private static void Show(string text, bool failed)
        {
            Status = text;
            LastFailed = failed;
            StatusChanged?.Invoke();
        }
    }
}
