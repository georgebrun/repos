using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
            BuildColorRows();
            BuildPalette();
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
            ThemeService.Changed -= OnThemeChanged;
            ThemeService.Changed += OnThemeChanged;
            Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;
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
                (ThemeService.SelectedMode switch
                {
                    AppTheme.Light => ThemeLight,
                    AppTheme.System => ThemeSystem,
                    AppTheme.Custom => ThemeCustom,
                    _ => ThemeDark,
                }).IsChecked = true;
                ShowThemeNote();
                ShowCustom();
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

        // ══════════════════════════════════════════════════════════════════
        // APPEARANCE
        // ══════════════════════════════════════════════════════════════════
        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsInitialized) return;
            var mode = sender == ThemeLight ? AppTheme.Light
                     : sender == ThemeSystem ? AppTheme.System
                     : sender == ThemeCustom ? AppTheme.Custom
                     : AppTheme.Dark;
            ThemeApplier.Use(mode);
            ShowThemeNote();
            ShowCustom();
        }

        // ── Custom colours ───────────────────────────────────────────────
        private sealed record ColorRow(string Key, Border Swatch, TextBox Hex, Button Default);
        private readonly System.Collections.Generic.List<ColorRow> _colorRows = new();
        private string _paletteKey = "";

        /// <summary>Ready-made colours in the swatch palette: greys, dark and light tints, accents.</summary>
        private static readonly string[] Palette =
        {
            "#000000", "#141414", "#202020", "#2B2B2B", "#333333", "#3C3C3C", "#4D4D4D", "#666666",
            "#808080", "#999999", "#B3B3B3", "#CCCCCC", "#E0E0E0", "#F0F0F0", "#FAFAFA", "#FFFFFF",
            "#1E2A38", "#1F2D24", "#2D1F1F", "#2A2438", "#1B2B2E", "#33291A", "#26303B", "#2E2A22",
            "#EEF2F7", "#EDF7EE", "#F7EDED", "#F3EEF7", "#E8F4F8", "#FFF8E1", "#F5F0E6", "#E6EBF0",
            "#0078D4", "#2D7D9A", "#00B7C3", "#107C10", "#498205", "#5C2D91", "#8E8CD8", "#B4009E",
            "#C50F1F", "#CA5010", "#E8A317", "#FFB900", "#9A7000", "#7A5C00", "#004E8C", "#3A3A8C",
        };

        private static Brush Solid(string hex)
        {
            var b = new SolidColorBrush(ThemeApplier.ToColor(hex));
            b.Freeze();
            return b;
        }

        private void BuildColorRows()
        {
            foreach (var (key, label, hint) in ColorPreset.Slots)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint });
                var swatch = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                swatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                var pick = new Button { Content = swatch, Padding = new Thickness(3), Tag = key, ToolTip = "Pick a color" };
                pick.Click += Swatch_Click;
                row.Children.Add(pick);
                var hex = new TextBox { Width = 96, Margin = new Thickness(8, 0, 0, 0), Tag = key, VerticalContentAlignment = VerticalAlignment.Center,
                                        ToolTip = "Type a color as #RRGGBB, then Enter" };
                hex.LostFocus += (_, _) => HexEntered(hex);
                hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) HexEntered(hex); };
                row.Children.Add(hex);
                var def = new Button { Content = "Default", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), Tag = key,
                                       ToolTip = "Back to the Light / Dark color" };
                def.Click += (_, _) => SetColor(key, "");
                row.Children.Add(def);
                var note = new TextBlock { Text = hint, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                note.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                row.Children.Add(note);
                ColorRows.Children.Add(row);
                _colorRows.Add(new ColorRow(key, swatch, hex, def));
            }
        }

        private void BuildPalette()
        {
            foreach (string hex in Palette)
            {
                var b = new Button
                {
                    Width = 32, Height = 26, Margin = new Thickness(2), Padding = new Thickness(0), Tag = hex, ToolTip = hex, MinWidth = 0,
                    Content = new Border { Width = 26, Height = 20, CornerRadius = new CornerRadius(3), Background = Solid(hex) },
                };
                b.Click += (_, _) =>
                {
                    PalettePopup.IsOpen = false;
                    SetColor(_paletteKey, hex);
                };
                PaletteWrap.Children.Add(b);
            }
        }

        /// <summary>The colour a slot shows: the preset's own, or the Light / Dark default ("" = default).</summary>
        private static string Shown(ColorPreset p, string key)
        {
            string own = p.Get(key);
            if (own.Length > 0) return own;
            if (key == "Accent")
            {
                var c = Wpf.Ui.Appearance.ApplicationAccentColorManager.SystemAccent;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            return ColorPreset.DefaultFor(p.IsLight, key);
        }

        private void ShowCustom()
        {
            bool on = ThemeService.SelectedMode == AppTheme.Custom;
            CustomPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            var s = AppSettingsService.Current;
            var p = s.ActivePreset();
            bool was = _loading;
            _loading = true;
            try
            {
                PresetBox.ItemsSource = s.Presets.Select(x => x.Name).ToList();
                PresetBox.SelectedItem = p.Name;
                PresetNameBox.Text = p.Name;
                BtnDeletePreset.IsEnabled = s.Presets.Count > 1;
                (p.IsLight ? BaseLight : BaseDark).IsChecked = true;
                foreach (var r in _colorRows)
                {
                    string shown = Shown(p, r.Key);
                    r.Swatch.Background = Solid(shown);
                    r.Hex.Text = shown;
                    r.Default.IsEnabled = p.Get(r.Key).Length > 0;
                }
            }
            finally
            {
                _loading = was;
            }
            ShowContrast(p);
        }

        /// <summary>Amber note when a text colour gets too close to its background (a warning only).</summary>
        private void ShowContrast(ColorPreset p)
        {
            string window = Shown(p, "Window"), panel = Shown(p, "Panel");
            var problems = new System.Collections.Generic.List<string>();
            void Check(string what, string fore, string back, string backName, double need)
            {
                double c = ColorPreset.Contrast(fore, back);
                if (c < need) problems.Add($"{what} on the {backName} is hard to read (contrast {c:0.0}; aim for {need:0.#} or more).");
            }
            Check("Text", Shown(p, "Text"), window, "window background", 4.5);
            Check("Text", Shown(p, "Text"), panel, "panel background", 4.5);
            Check("Secondary text", Shown(p, "SecondaryText"), window, "window background", 3);
            Check("Warning", Shown(p, "Warning"), window, "window background", 3);
            ContrastText.Text = string.Join("\n", problems.Select(x => "⚠ " + x));
            ContrastText.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Change one colour of the active preset ("" = back to the default) and show it.</summary>
        private void SetColor(string key, string hex)
        {
            var s = AppSettingsService.Current;
            s.ActivePreset().Set(key, hex);
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string key) return;
            _paletteKey = key;
            PalettePopup.PlacementTarget = b;
            PalettePopup.IsOpen = true;
        }

        private void HexEntered(TextBox box)
        {
            if (_loading || box.Tag is not string key) return;
            var p = AppSettingsService.Current.ActivePreset();
            string hex = ColorPreset.Normalize(box.Text);
            if (hex.Length == 0 || hex == Shown(p, key)) { box.Text = Shown(p, key); return; }   // not a colour, or unchanged
            SetColor(key, hex);
        }

        private void BtnResetColors_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Current;
            var p = s.ActivePreset();
            foreach (var (key, _, _) in ColorPreset.Slots) p.Set(key, "");
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
        }

        private void Base_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsInitialized) return;
            var s = AppSettingsService.Current;
            s.ActivePreset().Base = sender == BaseLight ? "Light" : "Dark";
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
        }

        private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !IsInitialized || PresetBox.SelectedItem is not string name) return;
            var s = AppSettingsService.Current;
            s.CustomPreset = name;
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
        }

        private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Current;
            string name = "Custom";
            for (int i = 2; s.Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = $"Custom {i}";
            s.Presets.Add(s.ActivePreset().Copy(name));
            s.CustomPreset = name;
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
            PresetNameBox.Focus();
            PresetNameBox.SelectAll();
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Current;
            if (s.Presets.Count < 2) return;
            var p = s.ActivePreset();
            if (MessageBox.Show(Window.GetWindow(this), $"Delete the preset \"{p.Name}\"?", "Custom Colors",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            s.Presets.Remove(p);
            s.CustomPreset = s.Presets[0].Name;
            AppSettingsService.Save(s);
            ThemeApplier.Refresh();
            ShowCustom();
        }

        private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) RenamePreset();
        }

        private void PresetNameBox_LostFocus(object sender, RoutedEventArgs e) => RenamePreset();

        private void RenamePreset()
        {
            if (_loading) return;
            var s = AppSettingsService.Current;
            var p = s.ActivePreset();
            string name = PresetNameBox.Text.Trim();
            if (name.Length == 0 || name == p.Name ||
                s.Presets.Any(x => x != p && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                PresetNameBox.Text = p.Name;                          // empty or taken: keep the old name
                return;
            }
            p.Name = name;
            s.CustomPreset = name;
            AppSettingsService.Save(s);
            ShowCustom();
        }

        /// <summary>Follow Windows changed the theme while this page is open.</summary>
        private void OnThemeChanged() => Dispatcher.BeginInvoke(new Action(ShowThemeNote));

        private void ShowThemeNote() =>
            ThemeNote.Text = ThemeService.SelectedMode == AppTheme.System
                ? $"Windows is using {(ThemeService.CurrentTheme == AppTheme.Dark ? "Dark" : "Light")} now."
                : "";

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
