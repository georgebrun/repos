using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace BreakersOfE.Windows
{
    public partial class SplashWindow : Window
    {
        private readonly DateTime _showTime = DateTime.Now;
        private const int MinDisplayMs = 2000; // show for at least 2 seconds

        public SplashWindow()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = version != null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.0";
        }

        /// <summary>
        /// Update the loading status and progress bar (0–100).
        /// Safe to call from any thread.
        /// </summary>
        public void SetStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                ProgressBar.Value = Math.Clamp(progress, 0, 100);
            });
        }

        /// <summary>
        /// Wait for the minimum display time then close the splash.
        /// Call this after MainWindow is fully loaded.
        /// </summary>
        public async Task CloseWhenReady()
        {
            int elapsed = (int)(DateTime.Now - _showTime).TotalMilliseconds;
            int remaining = MinDisplayMs - elapsed;
            if (remaining > 0)
                await Task.Delay(remaining);

            Dispatcher.Invoke(Close);
        }
    }
}