using System;
using System.Windows;
using System.Windows.Input;
using BreakersOfE.Services;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// Phone Lists, from the Trade Binder and Want List: the Phone… button,
    /// and once by itself when either page opens while the lists are off
    /// (until "Don't show this again"). The same settings as Settings →
    /// Phone lists (<see cref="PhoneListsUi"/>).
    /// </summary>
    public partial class PhoneListsWindow : FluentWindow
    {
        private bool _loading;
        private static bool _offeredThisRun;

        private PhoneListsWindow(Window? owner, bool offered)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            // From the Phone… button there's nothing to stop showing.
            DontShowCheck.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
            QrImage.Source = PhoneListsUi.Qr;
            UrlText.Text = PhoneListsService.ViewerUrl;
            KeyDown += (_, e) => { if (e.Key == Key.F1) HelpWindow.Open("phone-lists"); };
            PhoneListsWatcher.StatusChanged += ShowStatus;
            Closed += (_, _) => PhoneListsWatcher.StatusChanged -= ShowStatus;
            Refresh();
        }

        /// <summary>The Phone… button.</summary>
        public static void Open(Window? owner) => new PhoneListsWindow(owner, offered: false).ShowDialog();

        /// <summary>
        /// The Trade Binder or Want List just opened: show this while the lists
        /// are off, once each time BoE runs, until "Don't show this again" (or
        /// the lists are on).
        /// </summary>
        public static void OfferOnce(Window? owner)
        {
            var s = AppSettingsService.Current;
            if (s.PhoneListsOn || s.PhoneListsOffered || _offeredThisRun) return;
            _offeredThisRun = true;                        // not again until BoE starts again
            var dlg = new PhoneListsWindow(owner, offered: true);
            dlg.ShowDialog();
        }

        private void Refresh()
        {
            var s = AppSettingsService.Current;
            _loading = true;
            OnCheck.IsChecked = s.PhoneListsOn;
            _loading = false;
            OnPanel.IsEnabled = s.PhoneListsOn;
            OnPanel.Opacity = s.PhoneListsOn ? 1 : 0.5;
            FileText.Text = PhoneListsUi.FileText();
            ShowStatus();
        }

        private void ShowStatus()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowStatus)); return; }
            StatusText.Text = PhoneListsUi.StatusText();
            StatusText.Foreground = PhoneListsWatcher.LastFailed && AppSettingsService.Current.PhoneListsOn
                ? (System.Windows.Media.Brush)FindResource("BoeWarningBrush")
                : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
        }

        private async void On_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var task = PhoneListsUi.SetOnAsync(OnCheck.IsChecked == true);
            Refresh();
            await task;
        }

        private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            await PhoneListsUi.ChangeFolderAsync(this);
            Refresh();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string problem = PhoneListsUi.OpenFolder();
            if (problem.Length > 0) StatusText.Text = problem;
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Writing the lists…";
            await PhoneListsWatcher.WriteNowAsync();
        }

        private void Copy_Click(object sender, RoutedEventArgs e) => StatusText.Text = PhoneListsUi.CopyAddress();

        protected override void OnClosed(EventArgs e)
        {
            // Offered by itself: don't offer again once ticked, or once the lists are on.
            if (DontShowCheck.Visibility == Visibility.Visible && DontShowCheck.IsChecked == true)
            {
                var s = AppSettingsService.Current;
                s.PhoneListsOffered = true;
                AppSettingsService.Save(s);
            }
            base.OnClosed(e);
        }
    }
}
