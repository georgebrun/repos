using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Services
{
    /// <summary>
    /// What Settings → Phone lists and the Phone Lists window share: the QR
    /// code, picking the folder, opening it, copying the address.
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

        /// <summary>The status line: what the watcher last did, or Off.</summary>
        public static string StatusText() =>
            AppSettingsService.Current.PhoneListsOn
                ? (PhoneListsWatcher.Status.Length > 0 ? PhoneListsWatcher.Status : "Writing the lists…")
                : "Off.";
    }
}
