using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    /// <summary>
    /// What Settings → Phone lists and the Phone Lists window share: the QR
    /// code, Dropbox, picking the folder, opening it, copying the address.
    /// </summary>
    public static class PhoneListsUi
    {
        private static BitmapImage? _qr;

        /// <summary>The phone page's address as a QR code (made once).</summary>
        public static BitmapImage? Qr => _qr ??= QrImage(PhoneListsService.ViewerUrl);

        private static BitmapImage? QrImage(string text)
        {
            try
            {
                using var gen = new QRCoder.QRCodeGenerator();
                using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
                byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(10);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(png);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null;                      // the address and Copy Address still work
            }
        }

        /// <summary>"C:\…\OneDrive\Breakers of E\BoE Lists.json", or why there's none.</summary>
        public static string FileText()
        {
            string folder = PhoneListsService.Folder;
            return folder.Length == 0
                ? "None yet: pick a folder in your cloud drive (no OneDrive found on this PC)."
                : Path.Combine(folder, PhoneListsService.FileName);
        }

        /// <summary>Turn the lists on or off (saved), writing them straight away when on.</summary>
        public static async Task SetOnAsync(bool on)
        {
            var s = AppSettingsService.Current;
            s.PhoneListsOn = on;
            AppSettingsService.Save(s);
            if (on) await PhoneListsWatcher.WriteNowAsync();
        }

        /// <summary>Change Folder…: pick a folder in the cloud drive. False when cancelled.</summary>
        public static async Task<bool> ChangeFolderAsync(Window? owner)
        {
            string current = PhoneListsService.Folder;
            string parent = Path.GetDirectoryName(current) ?? "";
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Folder for BoE Lists.json (in your cloud drive)",
                InitialDirectory = Directory.Exists(current) ? current
                    : Directory.Exists(parent) ? parent
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (owner != null ? dlg.ShowDialog(owner) != true : dlg.ShowDialog() != true) return false;
            var s = AppSettingsService.Current;
            // The default folder picked again: store "" so it follows OneDrive if that moves.
            s.PhoneListsFolder = string.Equals(dlg.FolderName, PhoneListsService.DefaultFolder(), StringComparison.OrdinalIgnoreCase)
                ? "" : dlg.FolderName;
            AppSettingsService.Save(s);
            await PhoneListsWatcher.WriteNowAsync();
            return true;
        }

        /// <summary>Open Folder: File Explorer at the file (a cloud-drive folder has no "open" of its own). "" or what went wrong.</summary>
        public static string OpenFolder()
        {
            string folder = PhoneListsService.Folder;
            if (folder.Length == 0) return "";
            try
            {
                Directory.CreateDirectory(folder);
                string file = PhoneListsService.FilePath(folder);
                string args = File.Exists(file) ? $"/select,\"{file}\"" : $"\"{folder}\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
                return "";
            }
            catch (Exception ex)
            {
                return $"Couldn't open the folder: {ex.Message}";
            }
        }

        /// <summary>Copy Address: the phone page's address on the clipboard. The status to show.</summary>
        public static string CopyAddress()
        {
            try
            {
                Clipboard.SetText(PhoneListsService.ViewerUrl);
                return "Address copied: send it to your phone (email or a message) if you can't scan the code.";
            }
            catch
            {
                return "Couldn't copy the address (the clipboard was busy). Try again.";
            }
        }

        // ── Dropbox (hands-free) ─────────────────────────────────────────
        /// <summary>Waiting for the browser sign-in (the button says so and can't be clicked again).</summary>
        public static bool Connecting { get; private set; }

        /// <summary>What the Dropbox line says.</summary>
        public static string DropboxText()
        {
            if (Connecting)
                return "Waiting for Dropbox: finish signing in on the page that opened in your browser.";
            if (!DropboxService.Connected)
                return "Not connected. Connect once and your phone gets the newest lists by itself, " +
                       "every time you open the BoE Lists page: no picking files, no Update. Free; no Dropbox app needed on this PC.";
            string who = DropboxService.AccountName;
            return $"Connected{(who.Length > 0 ? $" ({who})" : "")}. BoE sends the lists to Dropbox › Apps › " +
                   $"{DropboxService.AppName} whenever your collection changes.";
        }

        /// <summary>Connect Dropbox… / Disconnect. "" or what went wrong.</summary>
        public static async Task<string> ToggleDropboxAsync()
        {
            if (DropboxService.Connected)
            {
                await DropboxService.DisconnectAsync();
                await PhoneListsWatcher.WriteNowAsync();          // back to the cloud-drive folder
                return "";
            }
            if (Connecting) return "";
            Connecting = true;
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await DropboxService.ConnectAsync(url =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }), cts.Token);
                Connecting = false;
                await PhoneListsWatcher.WriteNowAsync();
                return "";
            }
            catch (OperationCanceledException)
            {
                return "Dropbox didn't answer within 5 minutes. Try Connect Dropbox… again.";
            }
            catch (Exception ex)
            {
                return ex is System.Net.Http.HttpRequestException ? "Couldn't reach Dropbox. Check the internet connection and try again." : ex.Message;
            }
            finally
            {
                Connecting = false;
            }
        }

        /// <summary>Dropbox's sign-up page (Dropbox Basic: free).</summary>
        public const string SignUpUrl = "https://www.dropbox.com/register";

        /// <summary>How to make one, under the Free Dropbox account link.</summary>
        public const string SignUpSteps =
            "Dropbox Basic is free (2 GB; the lists take a few hundred KB). Sign up with your name, email and a password, " +
            "and confirm the email Dropbox sends. Skip any offer of a paid trial or of installing the Dropbox app: BoE doesn't need either. " +
            "Then come back here and click Connect Dropbox….";

        /// <summary>The Free Dropbox account link: Dropbox's sign-up page in the browser.</summary>
        public static void OpenSignUp()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SignUpUrl) { UseShellExecute = true });
            }
            catch
            {
                // No browser set up: the address is in the tooltip.
            }
        }

        public static string DropboxButton() =>
            Connecting ? "Waiting for Dropbox…" : DropboxService.Connected ? "Disconnect" : "Connect Dropbox…";

        /// <summary>Step 3 on the phone: sign in with Dropbox, or open the file.</summary>
        public static string PhoneStep3() =>
            DropboxService.Connected
                ? "3.  Open it, tap Sign in with Dropbox, and allow it (once, with the same Dropbox). From then on it gets your newest lists by itself each time you open it."
                : "3.  Open it, tap Open my lists file, and pick BoE Lists.json in your cloud drive. Tap Update there to get newer lists.";

        /// <summary>The status line: what the watcher last did, or Off.</summary>
        public static string StatusText() =>
            AppSettingsService.Current.PhoneListsOn
                ? (PhoneListsWatcher.Status.Length > 0 ? PhoneListsWatcher.Status : "Writing the lists…")
                : "Off.";
    }
}
