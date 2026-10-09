using System.Windows;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Dialogs
{
    /// <summary>
    /// "A new version of Breakers of E is available!" — this version and the
    /// new one, with Open Download Page (the release on GitHub) or Not Now.
    /// </summary>
    public partial class NewVersionWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ReleaseCheck.Release _release;

        /// <summary>Show it over the owner. Not Now just closes; the next start asks again.</summary>
        public static void Ask(Window? owner, ReleaseCheck.Release release)
        {
            var w = new NewVersionWindow(release);
            if (owner != null && owner.IsVisible) w.Owner = owner;
            else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
        }

        public NewVersionWindow(ReleaseCheck.Release release)
        {
            InitializeComponent();
            _release = release;
            CurrentText.Text = ReleaseCheck.CurrentText;
            NewText.Text = ReleaseCheck.Text(release.Version);
            if (string.IsNullOrWhiteSpace(release.PageUrl)) BtnOpen.IsEnabled = false;
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCheck.OpenPage(_release);
            Close();
        }

        private void BtnNotNow_Click(object sender, RoutedEventArgs e) => Close();
    }
}
