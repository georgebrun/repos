using BreakersOfE.Services;
using System.Reflection;
using System.Windows;

namespace BreakersOfE.Windows
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly()
                .GetName().Version;
            VersionText.Text = version != null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.0";
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
            => System.Diagnostics.Process.Start("explorer.exe",
                AppFolderService.RootFolder);

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}