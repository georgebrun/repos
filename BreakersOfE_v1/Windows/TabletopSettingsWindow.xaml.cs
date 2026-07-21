using BreakersOfE.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    public partial class TabletopSettingsWindow : Window
    {
        // ── Result properties read by TabletopWindow after close ─────────────
        public int DefaultLife { get; private set; } = 20;
        public bool AutoDraw { get; private set; } = true;
        public bool AutoEmptyMana { get; private set; } = true;
        public bool ShowGameOver { get; private set; } = true;
        public bool BlurOppHand { get; private set; } = true;
        public string TableColor { get; private set; } = "Green";
        public string? NewSleevePath { get; private set; } = null;
        public string? NewYourPlaymat { get; private set; } = null;
        public string? NewOppPlaymat { get; private set; } = null;
        public bool SleepCleared { get; private set; } = false;
        public bool YourPlaymatCleared { get; private set; } = false;
        public bool OppPlaymatCleared { get; private set; } = false;

        // Current values passed in from TabletopWindow
        private readonly string _currentSleeve;
        private readonly string _currentYourPlaymat;
        private readonly string _currentOppPlaymat;

        // Table color options
        private static readonly (string Name, string Light, string Mid, string Dark)[] TableColors =
        {
            ("Green",   "#2D5A27", "#234820", "#152E14"),
            ("Blue",    "#1A2D5A", "#162448", "#0E1830"),
            ("Black",   "#1A1A1A", "#141414", "#0A0A0A"),
            ("Red",     "#4A1A1A", "#3A1414", "#250D0D"),
            ("Purple",  "#2E1A4A", "#24143A", "#160D25"),
            ("Brown",   "#3A2A1A", "#2E2014", "#1E140D"),
        };

        private string _selectedColor = "Green";
        private Border? _selectedSwatch = null;

        public TabletopSettingsWindow(
            int defaultLife, bool autoDraw, bool autoEmptyMana,
            bool showGameOver, bool blurOppHand, string tableColor,
            string currentSleeve, string currentYourPlaymat, string currentOppPlaymat)
        {
            InitializeComponent();

            _currentSleeve = currentSleeve;
            _currentYourPlaymat = currentYourPlaymat;
            _currentOppPlaymat = currentOppPlaymat;
            _selectedColor = tableColor;

            // Populate controls from current values
            if (defaultLife == 20) Life20.IsChecked = true;
            else if (defaultLife == 30) Life30.IsChecked = true;
            else if (defaultLife == 40) Life40.IsChecked = true;
            else
            {
                LifeCustom.IsChecked = true;
                LifeCustomBox.Text = defaultLife.ToString();
            }

            ChkAutoDraw.IsChecked = autoDraw;
            ChkAutoEmptyMana.IsChecked = autoEmptyMana;
            ChkGameOverPrompt.IsChecked = showGameOver;
            ChkBlurOppHand.IsChecked = blurOppHand;

            UpdatePathLabels();
            BuildColorSwatches();
        }

        private void UpdatePathLabels()
        {
            SleevePathText.Text = string.IsNullOrEmpty(_currentSleeve)
                ? "Default (MTG card back)"
                : System.IO.Path.GetFileName(_currentSleeve);

            YourPlaymatText.Text = string.IsNullOrEmpty(_currentYourPlaymat)
                ? "None"
                : System.IO.Path.GetFileName(_currentYourPlaymat);

            OppPlaymatText.Text = string.IsNullOrEmpty(_currentOppPlaymat)
                ? "None"
                : System.IO.Path.GetFileName(_currentOppPlaymat);
        }

        private void BuildColorSwatches()
        {
            ColorPanel.Children.Clear();
            foreach (var (name, light, mid, dark) in TableColors)
            {
                var swatch = new Border
                {
                    Width = 48,
                    Height = 36,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 8, 4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = name,
                    Tag = name,
                    Background = new LinearGradientBrush(
                        Color.FromRgb(
                            Convert.ToByte(light[1..3], 16),
                            Convert.ToByte(light[3..5], 16),
                            Convert.ToByte(light[5..7], 16)),
                        Color.FromRgb(
                            Convert.ToByte(dark[1..3], 16),
                            Convert.ToByte(dark[3..5], 16),
                            Convert.ToByte(dark[5..7], 16)),
                        new Point(0, 0), new Point(1, 1))
                };

                if (name == _selectedColor)
                {
                    swatch.BorderBrush = Brushes.White;
                    swatch.BorderThickness = new Thickness(2.5);
                    _selectedSwatch = swatch;
                }
                else
                {
                    swatch.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                    swatch.BorderThickness = new Thickness(1);
                }

                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    if (_selectedSwatch != null)
                    {
                        _selectedSwatch.BorderBrush =
                            new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                        _selectedSwatch.BorderThickness = new Thickness(1);
                    }
                    swatch.BorderBrush = Brushes.White;
                    swatch.BorderThickness = new Thickness(2.5);
                    _selectedSwatch = swatch;
                    _selectedColor = name;
                };

                // Label inside swatch
                swatch.Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                ColorPanel.Children.Add(swatch);
            }
        }

        // ── Image browse buttons ──────────────────────────────────────────────
        private void BtnBrowseSleeve_Click(object sender, RoutedEventArgs e)
        {
            var path = TabletopWindow.BrowseTabletopImageStatic(
                "Select Card Sleeve Image", AppFolderService.SleeveImagesFolder);
            if (path == null) return;
            NewSleevePath = path;
            SleepCleared = false;
            SleevePathText.Text = System.IO.Path.GetFileName(path);
        }

        private void BtnClearSleeve_Click(object sender, RoutedEventArgs e)
        {
            NewSleevePath = null;
            SleepCleared = true;
            SleevePathText.Text = "Default (MTG card back)";
        }

        private void BtnBrowseYourPlaymat_Click(object sender, RoutedEventArgs e)
        {
            var path = TabletopWindow.BrowseTabletopImageStatic(
                "Select Your Playmat Image", AppFolderService.PlaymatImagesFolder);
            if (path == null) return;
            NewYourPlaymat = path;
            YourPlaymatCleared = false;
            YourPlaymatText.Text = System.IO.Path.GetFileName(path);
        }

        private void BtnClearYourPlaymat_Click(object sender, RoutedEventArgs e)
        {
            NewYourPlaymat = null;
            YourPlaymatCleared = true;
            YourPlaymatText.Text = "None";
        }

        private void BtnBrowseOppPlaymat_Click(object sender, RoutedEventArgs e)
        {
            var path = TabletopWindow.BrowseTabletopImageStatic(
                "Select Opponent Playmat Image", AppFolderService.PlaymatImagesFolder);
            if (path == null) return;
            NewOppPlaymat = path;
            OppPlaymatCleared = false;
            OppPlaymatText.Text = System.IO.Path.GetFileName(path);
        }

        private void BtnClearOppPlaymat_Click(object sender, RoutedEventArgs e)
        {
            NewOppPlaymat = null;
            OppPlaymatCleared = true;
            OppPlaymatText.Text = "None";
        }

        private void BtnOpenSleevesFolder_Click(object sender, RoutedEventArgs e)
            => System.Diagnostics.Process.Start("explorer.exe",
                AppFolderService.SleeveImagesFolder);

        private void BtnOpenPlaymatsFolder_Click(object sender, RoutedEventArgs e)
            => System.Diagnostics.Process.Start("explorer.exe",
                AppFolderService.PlaymatImagesFolder);

        // ── Apply & Cancel ────────────────────────────────────────────────────
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // Read life total
            if (Life20.IsChecked == true) DefaultLife = 20;
            else if (Life30.IsChecked == true) DefaultLife = 30;
            else if (Life40.IsChecked == true) DefaultLife = 40;
            else
            {
                DefaultLife = int.TryParse(LifeCustomBox.Text, out int v) && v > 0
                    ? v : 20;
            }

            AutoDraw = ChkAutoDraw.IsChecked == true;
            AutoEmptyMana = ChkAutoEmptyMana.IsChecked == true;
            ShowGameOver = ChkGameOverPrompt.IsChecked == true;
            BlurOppHand = ChkBlurOppHand.IsChecked == true;
            TableColor = _selectedColor;

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}