using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Windows
{
    public class ScryWindow : Window
    {
        public List<DeckCard> KeepOnTop { get; } = new();
        public List<DeckCard> PutOnBottom { get; } = new();

        private readonly List<DeckCard> _cards;
        private readonly Dictionary<DeckCard, bool?> _decisions = new();
        // true = keep on top, false = put on bottom, null = undecided

        private const double CardW = 90;
        private const double CardH = 126;
        private Window? _enlarged;

        public ScryWindow(List<DeckCard> topN, int n)
        {
            _cards = topN;
            foreach (var c in topN) _decisions[c] = null;

            Title = $"Scry {n}";
            Width = Math.Max(460, n * (CardW + 24) + 60);
            Height = 320;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));

            BuildUI();
        }

        private StackPanel _cardArea = new();
        private TextBlock _status = new();
        private Button _keepBtn = new();

        private void BuildUI()
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Instructions
            var instr = new TextBlock
            {
                Text = "Left-click = Keep on Top  |  Right-click = Put on Bottom  |  Click again to undo",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(instr, 0);
            root.Children.Add(instr);

            // Status
            _status = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(_status, 1);
            root.Children.Add(_status);

            // Card area
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _cardArea = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4)
            };
            scroll.Content = _cardArea;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // Buttons
            var btnRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _keepBtn = new Button
            {
                Content = "✓ Confirm",
                Width = 120,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x66, 0x22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0xAA, 0x44)),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false
            };
            _keepBtn.Click += Confirm_Click;
            Grid.SetColumn(_keepBtn, 1);
            btnRow.Children.Add(_keepBtn);
            Grid.SetRow(btnRow, 3);
            root.Children.Add(btnRow);

            Content = root;
            RebuildCards();
        }

        private void RebuildCards()
        {
            _cardArea.Children.Clear();

            foreach (var card in _cards)
            {
                var decision = _decisions[card];

                var border = new Border
                {
                    Width = CardW,
                    Height = CardH,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(2.5),
                    BorderBrush = decision switch
                    {
                        true => new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x44)), // green = top
                        false => new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44)), // red = bottom
                        null => new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
                    },
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(6),
                    ToolTip = card.Name + (decision == true ? " → TOP" :
                              decision == false ? " → BOTTOM" : " → undecided")
                };

                // Card image
                if (!string.IsNullOrEmpty(card.LocalImagePath)
                    && System.IO.File.Exists(card.LocalImagePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = (int)CardW;
                        bmp.EndInit();
                        border.Child = new System.Windows.Controls.Image
                        { Source = bmp, Stretch = Stretch.Fill };
                    }
                    catch { border.Child = MakeTextBack(card.Name); }
                }
                else
                    border.Child = MakeTextBack(card.Name);

                // Decision label overlay
                if (decision.HasValue)
                {
                    var grid = new Grid();
                    var child = border.Child;
                    border.Child = null;
                    grid.Children.Add(child!);
                    grid.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Height = 22,
                        Child = new TextBlock
                        {
                            Text = decision == true ? "▲ TOP" : "▼ BOTTOM",
                            Foreground = decision == true
                                ? new SolidColorBrush(Color.FromRgb(0x44, 0xFF, 0x44))
                                : new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
                            FontSize = 10,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                    border.Child = grid;
                }

                var captured = card;

                // Left-click = keep on top (toggle)
                border.MouseLeftButtonDown += (s, e) =>
                {
                    _decisions[captured] = _decisions[captured] == true ? null : true;
                    RebuildCards(); UpdateStatus();
                    e.Handled = true;
                };

                // Right-click = put on bottom (toggle)
                border.MouseRightButtonDown += (s, e) =>
                {
                    _decisions[captured] = _decisions[captured] == false ? null : false;
                    RebuildCards(); UpdateStatus();
                    e.Handled = true;
                };

                // Middle-click or double = enlarge
                border.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle || e.ClickCount == 2)
                        ShowEnlarged(captured);
                };

                _cardArea.Children.Add(border);
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int decided = _decisions.Count(kv => kv.Value.HasValue);
            int total = _decisions.Count;
            int onTop = _decisions.Count(kv => kv.Value == true);
            int onBottom = _decisions.Count(kv => kv.Value == false);

            _status.Text = $"▲ {onTop} on top   ▼ {onBottom} on bottom   " +
                           $"({decided}/{total} decided)";

            // Enable confirm when all cards have a decision
            _keepBtn.IsEnabled = decided == total;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            KeepOnTop.AddRange(_cards.Where(c => _decisions[c] == true));
            PutOnBottom.AddRange(_cards.Where(c => _decisions[c] == false));
            // Undecided cards go on top by default
            KeepOnTop.AddRange(_cards.Where(c => _decisions[c] == null));
            _enlarged?.Close();
            DialogResult = true;
        }

        private static Grid MakeTextBack(string name)
        {
            var g = new Grid
            { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E)) };
            g.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 8,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4),
                TextAlignment = TextAlignment.Center
            });
            return g;
        }

        private void ShowEnlarged(DeckCard card)
        {
            _enlarged?.Close();
            var win = new Window
            {
                Title = card.Name,
                Width = 340,
                Height = 480,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Black
            };
            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    win.Content = new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Uniform };
                }
                catch { }
            }
            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) => _enlarged = null;
            _enlarged = win;
            win.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _enlarged?.Close();
            base.OnClosed(e);
        }
    }
}