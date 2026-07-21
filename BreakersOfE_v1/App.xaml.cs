using BreakersOfE.Services;
using BreakersOfE.Windows;
using System.Linq;
using System.Windows;

namespace BreakersOfE
{
    public partial class App : Application
    {
        internal static SplashWindow? Splash { get; set; }

        /// <summary>
        /// Set by the installer when the user opts in to downloading
        /// the card database on first launch.
        /// </summary>
        public static bool FirstRunDownloadRequested { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply saved theme before any window opens
            ThemeService.ApplySavedTheme();

            // Ensure all app folders exist
            Services.AppFolderService.EnsureAllFolders();

            // If called with --update-db (from installer), run the database
            // update directly and exit without showing the main window.
            if (e.Args != null && e.Args.Any(a =>
                a.Equals("--update-db", System.StringComparison.OrdinalIgnoreCase)))
            {
                var win = new UpdateDatabaseWindow();
                win.ShowDialog();
                Shutdown();
                return;
            }

            // Check if installer requested a first-run database download (legacy flag)
            string flagFile = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "first_run_download.flag");
            if (System.IO.File.Exists(flagFile))
            {
                FirstRunDownloadRequested = true;
                try { System.IO.File.Delete(flagFile); } catch { }
            }

            // Show splash screen
            Splash = new SplashWindow();
            Splash.Show();
            Splash.SetStatus("Starting up...", 5);
        }
    }
}