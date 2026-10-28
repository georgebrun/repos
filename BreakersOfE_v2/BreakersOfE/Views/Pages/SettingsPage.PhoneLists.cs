using System;
using System.Windows;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Pages
{
    // Settings → Phone lists: the on/off box, the cloud-drive folder, Update
    // Now, the status line, and the QR code of the phone page (the same as the
    // Phone Lists window on the Trade Binder and Want List; see PhoneListsUi).
    // Part of SettingsPage (same class).
    public partial class SettingsPage
    {
        private void ShowPhoneLists()
        {
            var s = AppSettingsService.Current;
            bool was = _loading;
            _loading = true;
            PhoneListsCheck.IsChecked = s.PhoneListsOn;
            _loading = was;
            PhoneListsPanel.IsEnabled = s.PhoneListsOn;
            PhoneListsPanel.Opacity = s.PhoneListsOn ? 1 : 0.5;
            PhoneFolderText.Text = PhoneListsUi.FileText();
            PhoneUrlText.Text = PhoneListsService.ViewerUrl;
            PhoneQr.Source ??= PhoneListsUi.Qr;
            ShowPhoneStatus();
        }

        private void ShowPhoneStatus()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ShowPhoneStatus)); return; }
            PhoneStatusText.Text = PhoneListsUi.StatusText();
            PhoneStatusText.Foreground = PhoneListsWatcher.LastFailed && AppSettingsService.Current.PhoneListsOn
                ? (System.Windows.Media.Brush)FindResource("BoeWarningBrush")
                : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
        }

        private async void PhoneLists_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = PhoneListsCheck.IsChecked == true;
            var task = PhoneListsUi.SetOnAsync(on);
            ShowPhoneLists();
            await task;
        }

        private async void BtnPhoneFolder_Click(object sender, RoutedEventArgs e)
        {
            var task = PhoneListsUi.ChangeFolderAsync(Window.GetWindow(this));
            await task;
            ShowPhoneLists();
        }

        private void BtnPhoneOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string problem = PhoneListsUi.OpenFolder();
            if (problem.Length > 0) PhoneStatusText.Text = problem;
        }

        private async void BtnPhoneUpdate_Click(object sender, RoutedEventArgs e)
        {
            PhoneStatusText.Text = "Writing the lists…";
            await PhoneListsWatcher.WriteNowAsync();
        }

        private void BtnPhoneCopy_Click(object sender, RoutedEventArgs e) =>
            PhoneStatusText.Text = PhoneListsUi.CopyAddress();
    }
}
