using BreakersOfE.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BreakersOfE.Windows
{
    public partial class UpdateDatabaseWindow : Window
    {
        private CancellationTokenSource? _cts;
        private bool _isRunning = false;
        private bool _priceOnly = false;

        // Animation storyboards
        private Storyboard? _dot1;
        private Storyboard? _dot2;
        private Storyboard? _dot3;

        // Elapsed time timer
        private DispatcherTimer? _elapsedTimer;
        private Stopwatch _stopwatch = new();

        public ImportResult? Result { get; private set; }

        public UpdateDatabaseWindow(bool priceOnly = false)
        {
            InitializeComponent();
            _priceOnly = priceOnly;

            if (_priceOnly)
            {
                Title = "Update Card Prices";
                WindowTitleLabel.Text = "Update Card Prices";
                WindowSubtitleLabel.Text = "Downloads the latest prices from Scryfall and updates your card pool. No card data is changed.";
                StartButton.Content = "Start Price Update";
            }

            _dot1 = (Storyboard)Resources["PulseDot1"];
            _dot2 = (Storyboard)Resources["PulseDot2"];
            _dot3 = (Storyboard)Resources["PulseDot3"];
        }

        // ── Animation ─────────────────────────────────────────────────────────
        private void StartAnimation()
        {
            DotsPanel.Visibility = Visibility.Visible;
            _dot1?.Begin();
            _dot2?.Begin();
            _dot3?.Begin();
        }

        private void StopAnimation()
        {
            _dot1?.Stop();
            _dot2?.Stop();
            _dot3?.Stop();
            DotsPanel.Visibility = Visibility.Collapsed;
        }

        // ── Elapsed timer ─────────────────────────────────────────────────────
        private void StartElapsedTimer()
        {
            _stopwatch.Restart();
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (s, e) =>
            {
                var elapsed = _stopwatch.Elapsed;
                ElapsedLabel.Text = elapsed.TotalHours >= 1
                    ? $"Elapsed: {elapsed:h\\:mm\\:ss}"
                    : $"Elapsed: {elapsed:m\\:ss}";
            };
            _elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            _elapsedTimer?.Stop();
            _stopwatch.Stop();
        }

        // ── Start button ──────────────────────────────────────────────────────
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            _isRunning = true;
            StartButton.IsEnabled = false;
            CancelButton.Content = "Cancel";

            StartAnimation();
            StartElapsedTimer();

            _cts = new CancellationTokenSource();

            var progress = new Progress<ImportProgress>(p =>
            {
                StepLabel.Text = p.Step;
                DetailLabel.Text = p.Detail;
                PercentLabel.Text = $"{p.Percentage}%";

                var anim = new DoubleAnimation
                {
                    To = p.Percentage,
                    Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new QuadraticEase
                    { EasingMode = EasingMode.EaseOut }
                };
                MainProgressBar.BeginAnimation(
                    System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                    anim);
            });

            var service = new ScryfallService();

            if (_priceOnly)
                Result = await Task.Run(() =>
                    service.RunPriceUpdateAsync(progress, _cts.Token));
            else
                Result = await Task.Run(() =>
                    service.RunFullUpdateAsync(progress, _cts.Token));

            StopAnimation();
            StopElapsedTimer();
            _isRunning = false;

            if (Result.Success)
            {
                if (_priceOnly)
                {
                    // No verification report for price-only
                    StepLabel.Text = "Prices updated successfully!";
                    DetailLabel.Text = string.Empty;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    var report = new VerificationReportWindow(Result)
                    { Owner = this };
                    report.ShowDialog();
                    DialogResult = true;
                    Close();
                }
            }
            else
            {
                StepLabel.Text = "Failed.";
                DetailLabel.Text = Result.ErrorMessage;
                StartButton.IsEnabled = true;
                CancelButton.Content = "Close";

                if (Result.ErrorMessage != "Import cancelled by user." &&
                    Result.ErrorMessage != "Price update cancelled by user.")
                {
                    MessageBox.Show(
                        $"Update failed:\n\n{Result.ErrorMessage}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        // ── Cancel button ─────────────────────────────────────────────────────
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                StepLabel.Text = "Cancelling...";
                DetailLabel.Text = string.Empty;
            }
            else
            {
                DialogResult = false;
                Close();
            }
        }

        protected override void OnClosing(
            System.ComponentModel.CancelEventArgs e)
        {
            if (_isRunning)
            {
                e.Cancel = true;
                MessageBox.Show(
                    "Please wait for the update to finish or click Cancel first.",
                    "Update Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            base.OnClosing(e);
        }
    }
}