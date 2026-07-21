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
    public partial class MulliganWindow : Window
    {
        // ================================================================
        // RESULT PROPERTIES
        // ================================================================
        public List<DeckCard> FinalHand { get; private set; } = new();
        public List<DeckCard> BottomCards { get; private set; } = new();
        public bool MulliganedAgain { get; private set; } = false;

        // ================================================================
        // STATE
        // ================================================================
        private readonly List<DeckCard> _hand;
        private readonly List<DeckCard> _library;
        private readonly int _mulliganCount;  // how many times mulliganed so far
        private readonly bool _isCommander;
        private readonly int _putBackCount;   // cards to put on bottom

        private readonly List<DeckCard> _selected = new();
        private readonly List<Border> _cardVisuals = new();

        private const double CardW = 90;
        private const double CardH = 126;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public MulliganWindow(List<DeckCard> hand, List<DeckCard> library,
            int mulliganCount, bool isCommander)
        {
            InitializeComponent();
            _hand = hand;
            _library = library;
            _mulliganCount = mulliganCount;
            _isCommander = isCommander;

            // Commander: first mulligan is free (0 put back)
            _putBackCount = isCommander
                ? Math.Max(0, mulliganCount - 1)
                : mulliganCount;

            UpdateTitle();
            BuildCardDisplay();
        }

        private void UpdateTitle()
        {
            if (_putBackCount == 0)
            {
                TitleText.Text = "Free mulligan — keep all 7 cards (Commander rule)";
                SubtitleText.Text = "Click Keep Hand to continue, or Mulligan Again.";
                BtnKeep.IsEnabled = true;
                SelectionStatus.Text = "No cards to put back";
            }
            else
            {
                TitleText.Text = $"Select {_putBackCount} card{(_putBackCount > 1 ? "s" : "")} " +
                                 $"to place on the bottom of your library";
                SubtitleText.Text = "Click to select/deselect. Right-click to view card.";
            }
        }

        // ================================================================
        // BUILD CARD DISPLAY
        // ================================================================
        private void BuildCardDisplay()
        {
            CardPanel.Children.Clear();
            _cardVisuals.Clear();

            foreach (var card in _hand)
            {
                var border = MakeCardBorder(card);
                _cardVisuals.Add(border);
                CardPanel.Children.Add(border);
            }
        }

        private Border MakeCardBorder(DeckCard card)
        {
            var border = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Margin = new Thickness(4),
                ToolTip = card.Name
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
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill
                    };
                }
                catch { border.Child = MakeCardBack(card.Name); }
            }
            else
                border.Child = MakeCardBack(card.Name);

            // Left click = select/deselect for bottom
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (_putBackCount == 0) return;
                ToggleSelection(card, border);
                e.Handled = true;
            };

            // Right click = view enlarged
            border.MouseRightButtonDown += (s, e) =>
            {
                ShowCardEnlarged(card);
                e.Handled = true;
            };

            return border;
        }

        private static Grid MakeCardBack(string name)
        {
            var g = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E))
            };
            g.Children.Add(new Border
            {
                Margin = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3)
            });
            g.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 8,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6),
                TextAlignment = TextAlignment.Center
            });
            return g;
        }

        // ================================================================
        // SELECTION
        // ================================================================
        private void ToggleSelection(DeckCard card, Border border)
        {
            if (_selected.Contains(card))
            {
                // Deselect
                _selected.Remove(card);
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                border.BorderThickness = new Thickness(2);
                border.Effect = null;
            }
            else
            {
                // Only allow selecting up to _putBackCount
                if (_selected.Count >= _putBackCount) return;

                _selected.Add(card);
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
                border.BorderThickness = new Thickness(3);
                border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x44, 0x44),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int needed = _putBackCount;
            int have = _selected.Count;
            SelectionStatus.Text = $"{have} of {needed} selected";
            BtnKeep.IsEnabled = (have == needed);
        }

        // ================================================================
        // CARD ENLARGED VIEW
        // ================================================================
        private Window? _enlargedWindow;

        private void ShowCardEnlarged(DeckCard card)
        {
            _enlargedWindow?.Close();
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
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform
                    };
                }
                catch
                {
                    win.Content = new TextBlock
                    {
                        Text = card.Name,
                        Foreground = Brushes.White,
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
            }
            else
                win.Content = new TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) => _enlargedWindow = null;
            _enlargedWindow = win;
            win.Show();
        }

        // ================================================================
        // BUTTONS
        // ================================================================
        private void BtnKeep_Click(object sender, RoutedEventArgs e)
        {
            // Final hand = hand minus selected cards
            FinalHand = _hand.Where(c => !_selected.Contains(c)).ToList();
            BottomCards = _selected.ToList();
            _enlargedWindow?.Close();
            DialogResult = true;
        }

        private void BtnMulliganAgain_Click(object sender, RoutedEventArgs e)
        {
            MulliganedAgain = true;
            _enlargedWindow?.Close();
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _enlargedWindow?.Close();
            base.OnClosed(e);
        }
    }
}