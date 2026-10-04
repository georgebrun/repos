using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Settings (left pane, above Update Database): collection defaults,
    /// card pictures (kept for offline use), startup and update reminder,
    /// and the data folder. Everything saves as it changes.
    /// </summary>
    public partial class SettingsPage : Page
    {
        private sealed record Choice<T>(T Value, string Label);

        private static readonly Choice<int>[] Reminders =
        {
            new(0, "Never"), new(7, "1 week"), new(14, "2 weeks"), new(30, "1 month"), new(60, "2 months"),
        };

        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            _loading = true;
            LanguageBox.ItemsSource = CardLanguage.All;
            ConditionBox.ItemsSource = CardCondition.All.Where(c => c != CardCondition.Unknown).ToList();
            StartPageBox.ItemsSource = AppSettingsService.StartPages.Select(p => new Choice<string>(p.Value, p.Label)).ToList();
            ReminderBox.ItemsSource = Reminders;
            _loading = false;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EditPageKit.DisableHostScroll(this);
            ShowSettings();
            PictureSync.StatusChanged -= OnSyncStatus;
            PictureSync.StatusChanged += OnSyncStatus;
            PictureSync.ManualProgress -= OnManualProgress;
            PictureSync.ManualProgress += OnManualProgress;
            Unloaded += (_, _) =>
            {
                PictureSync.StatusChanged -= OnSyncStatus;
                PictureSync.ManualProgress -= OnManualProgress;
            };
            ShowDownloadState();                                  // a download started earlier may still be running
            _ = RefreshPictureInfo(recount: false);
            _ = RefreshFolderInfo();
        }

        private void ShowSettings()
        {
            var s = AppSettingsService.Current;
            _loading = true;
            try
            {
                LanguageBox.SelectedItem = s.DefaultLanguage;
                ConditionBox.SelectedItem = s.DefaultCondition;
                TradeBox.Text = s.TradePercent.ToString();
                SaveMineCheck.IsChecked = s.SaveMyPictures;
                SaveViewedCheck.IsChecked = s.SaveViewedPictures;
                StartPageBox.SelectedItem = (StartPageBox.ItemsSource as System.Collections.Generic.IEnumerable<Choice<string>>)!
                    .FirstOrDefault(c => c.Value == s.StartPage);
                ReminderBox.SelectedItem = Reminders.FirstOrDefault(r => r.Value == s.UpdateReminderDays)
                                           ?? Reminders.OrderBy(r => Math.Abs(r.Value - s.UpdateReminderDays)).First();
            }
            finally
            {
                _loading = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // SAVE
        // ══════════════════════════════════════════════════════════════════
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsInitialized) return;
            var s = AppSettingsService.Current;
            if (LanguageBox.SelectedItem is string lang) s.DefaultLanguage = lang;
            if (ConditionBox.SelectedItem is string cond) s.DefaultCondition = cond;
            bool mineWasOn = s.SaveMyPictures;
            s.SaveMyPictures = SaveMineCheck.IsChecked == true;
            s.SaveViewedPictures = SaveViewedCheck.IsChecked == true;
            if (StartPageBox.SelectedItem is Choice<string> start) s.StartPage = start.Value;
            if (ReminderBox.SelectedItem is Choice<int> rem) s.UpdateReminderDays = rem.Value;
            AppSettingsService.Save(s);
            // Just turned on: start getting your cards' pictures now, not in a few minutes.
            if (s.SaveMyPictures && !mineWasOn) SyncNow();
            if (s.SaveMyPictures != mineWasOn) _ = RefreshPictureInfo(recount: true);
        }

        private void TradeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Current;
            if (int.TryParse(TradeBox.Text.Trim(), out int pct) && pct is >= 1 and <= 100)
            {
                if (pct != s.TradePercent)
                {
                    s.TradePercent = pct;
                    AppSettingsService.Save(s);
                }
            }
            TradeBox.Text = s.TradePercent.ToString();          // anything else: back to what's saved
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = !e.Text.All(char.IsDigit);

        // ══════════════════════════════════════════════════════════════════
        // CARD PICTURES
        // ══════════════════════════════════════════════════════════════════
        private void OnSyncStatus() => Dispatcher.BeginInvoke(new Action(() => SyncText.Text = PictureSync.Status));

        /// <summary>A Settings download moved on or ended (it carries on even when you leave this page).</summary>
        private void OnManualProgress(PictureSync.Progress? p) => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowDownloadState(p);
            if (!PictureSync.ManualRunning) _ = RefreshPictureInfo(recount: true);
        }));

        private void ShowDownloadState(PictureSync.Progress? p = null)
        {
            bool on = PictureSync.ManualRunning;
            BtnSyncNow.IsEnabled = BtnDownloadAll.IsEnabled = BtnClearPictures.IsEnabled = !on;
            BtnStopDownload.Visibility = DownloadBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (p != null)
            {
                DownloadBar.Maximum = Math.Max(1, p.Total);
                DownloadBar.Value = p.Done;
            }
            DownloadText.Text = PictureSync.ManualText;
        }

        /// <summary>Pictures kept (space), and how many of your cards have theirs — counted, nothing downloaded.</summary>
        private async Task RefreshPictureInfo(bool recount)
        {
            if (!AppSettingsService.Current.SaveMyPictures)
                SyncText.Text = "Keeping your cards' pictures is off.";
            else if (recount || PictureSync.Status.Length == 0)
            {
                SyncText.Text = "Counting your cards' pictures…";
                await Task.Run(PictureSync.CheckStatus);
                SyncText.Text = PictureSync.Status;
            }
            else SyncText.Text = PictureSync.Status;
            var (bytes, files) = await Task.Run(ImageCacheService.CacheSize);
            CacheText.Text = $"Pictures kept: {files:N0} ({DataFolderMover.Size(bytes)}) in {AppFolderService.CardImagesFolder}";
        }

        private void BtnSyncNow_Click(object sender, RoutedEventArgs e) => SyncNow();

        private void SyncNow()
        {
            if (PictureSync.StartManual(all: false)) ShowDownloadState();
        }

        private async void BtnDownloadAll_Click(object sender, RoutedEventArgs e)
        {
            if (PictureSync.ManualRunning) return;
            DownloadText.Text = "Counting…";
            var (count, bytes) = await Task.Run(PictureSync.EstimateAll);
            DownloadText.Text = "";
            if (count == 0)
            {
                DownloadText.Text = "Every card's picture is already kept.";
                return;
            }
            double hours = count / 20.0 / 3600;                       // about 20 pictures a second on a good connection
            string time = hours < 1 ? $"about {Math.Max(1, (int)(hours * 60))} minutes" : $"about {hours:0.#} hours";
            string ask =
                $"Download {count:N0} card pictures?\n\n" +
                $"That's roughly {DataFolderMover.Size(bytes)} of data and disk space, and {time} on a good connection.\n\n" +
                "You can keep using BoE meanwhile (even leave this page), and press Stop at any time — what's downloaded " +
                "stays, and the next Download All carries on from there.";
            if (MessageBox.Show(Window.GetWindow(this), ask, "Download All Pictures",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            if (PictureSync.StartManual(all: true)) ShowDownloadState();
        }

        private void BtnStopDownload_Click(object sender, RoutedEventArgs e) => PictureSync.StopManual();

        private async void BtnClearPictures_Click(object sender, RoutedEventArgs e)
        {
            var (bytes, files) = await Task.Run(ImageCacheService.CacheSize);
            if (files == 0) { DownloadText.Text = "No pictures are kept."; return; }
            string ask = $"Delete all {files:N0} kept card pictures ({DataFolderMover.Size(bytes)})?\n\n" +
                         "Pictures then come from the internet again." +
                         (AppSettingsService.Current.SaveMyPictures ? " Your cards' pictures download again in the background." : "");
            if (MessageBox.Show(Window.GetWindow(this), ask, "Delete Kept Pictures",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            int n = await Task.Run(ImageCacheService.ClearCache);
            DownloadText.Text = $"Deleted {n:N0} pictures.";
            await RefreshPictureInfo(recount: true);
        }

        // ══════════════════════════════════════════════════════════════════
        // DATA FOLDER
        // ══════════════════════════════════════════════════════════════════
        private async Task RefreshFolderInfo()
        {
            string root = AppFolderService.RootFolder;
            bool isDefault = string.Equals(System.IO.Path.GetFullPath(root).TrimEnd('\\'),
                                           System.IO.Path.GetFullPath(AppFolderService.DefaultRootFolder).TrimEnd('\\'),
                                           StringComparison.OrdinalIgnoreCase);
            FolderText.Text = $"Your data folder: {root}" + (isDefault ? "  (the default)" : "");
            BtnDefaultFolder.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            FolderProblem.Text = AppFolderService.DataFolderProblem;
            FolderProblem.Visibility = FolderProblem.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            FolderSizeText.Text = "Measuring…";
            var (bytes, files) = await Task.Run(() => DataFolderMover.Measure(root));
            FolderSizeText.Text = $"{DataFolderMover.Size(bytes)} in {files:N0} files.";
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppFolderService.RootFolder) { UseShellExecute = true });
            }
            catch (Exception ex) { EditPageKit.ShowStatus(MoveText, $"Could not open it: {ex.Message}", true); }
        }

        private async void BtnMoveFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Move BoE's data to… (pick or make an empty folder)" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string target = dlg.FolderName;
            string problem = DataFolderMover.CheckTarget(target);
            if (problem.Length > 0) { EditPageKit.ShowStatus(MoveText, problem, true); return; }

            string ask = $"Copy everything in\n  {AppFolderService.RootFolder}\nto\n  {target}\n\n" +
                         "and restart BoE using the new folder?\n\n" +
                         "Please don't change your collection or decks while it copies. " +
                         "The old folder stays as it was — delete it yourself once you're happy.";
            if (MessageBox.Show(Window.GetWindow(this), ask, "Move Data Folder",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            PictureSync.StopBackground();                        // nothing writing pictures while it copies
            PictureSync.StopManual();
            var cts = new CancellationTokenSource();
            BtnMoveFolder.IsEnabled = BtnOpenFolder.IsEnabled = BtnDefaultFolder.IsEnabled = false;
            MoveBar.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.AppStarting;
            try
            {
                var progress = new Progress<DataFolderMover.Progress>(p =>
                {
                    MoveBar.Maximum = Math.Max(1, p.BytesTotal);
                    MoveBar.Value = p.BytesDone;
                    MoveText.Text = $"Copying {p.FilesDone:N0} of {p.FilesTotal:N0} files ({DataFolderMover.Size(p.BytesDone)} of {DataFolderMover.Size(p.BytesTotal)})…";
                });
                await Task.Run(() => DataFolderMover.Move(target, progress, cts.Token));
                Mouse.OverrideCursor = null;
                MessageBox.Show(Window.GetWindow(this),
                    $"Everything is copied and checked. BoE will now restart using\n  {target}",
                    "Move Data Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                Restart();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                EditPageKit.ShowStatus(MoveText, $"The move didn't finish: {ex.Message} Nothing changed — BoE still uses {AppFolderService.RootFolder}.", true);
                PictureSync.StartBackground();
            }
            finally
            {
                BtnMoveFolder.IsEnabled = BtnOpenFolder.IsEnabled = BtnDefaultFolder.IsEnabled = true;
                MoveBar.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            string ask = $"Go back to the default folder\n  {AppFolderService.DefaultRootFolder}\n\n" +
                         "Nothing is copied: BoE restarts with whatever is in that folder now (it may be older than your current data).\n" +
                         $"Your current folder ({AppFolderService.RootFolder}) is left as it is.";
            if (MessageBox.Show(Window.GetWindow(this), ask, "Data Folder",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try
            {
                AppFolderService.SetDataFolder(null);
                Restart();
            }
            catch (Exception ex) { EditPageKit.ShowStatus(MoveText, $"Couldn't change it: {ex.Message}", true); }
        }

        /// <summary>Start a fresh BoE and close this one (it then reads the new data folder).</summary>
        private static void Restart()
        {
            string? exe = Environment.ProcessPath;
            if (exe != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
    }
}
