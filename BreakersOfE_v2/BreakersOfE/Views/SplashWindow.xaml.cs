using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace BreakersOfE.Views
{
    /// <summary>
    /// The startup picture (v1's splash window, carried over): title, a status
    /// line with progress while BoE gets ready, and the version.
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "Version 2.0.1";
        }

        /// <summary>
        /// Show what start-up is doing (0–100), and paint it right away: the
        /// start-up work runs on this same thread, so without the paint the
        /// window would only update once it's all done.
        /// </summary>
        public void SetStatus(string message, int progress)
        {
            StatusText.Text = message;
            ProgressBar.Value = Math.Clamp(progress, 0, 100);
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }
    }
}
