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
    public enum ZoneType { Graveyard, Exile, Library }

    public partial class ZoneBrowserWindow : Window
    {
        // ================================================================
        // STATE
        // ================================================================
        private readonly ZoneType _zone;
        private readonly bool _isYour;
        private readonly List<DeckCard> _cards;
        private readonly DeckCard? _commander;    // null if no commander
        private readonly bool _isCommander;  // is this commander's zone?

        // Callbacks — TabletopWindow wires these to modify its own state
        public Action<DeckCard, string>? OnMoveCard;   // (card, destination)
        public Action? OnShuffle;
        public Action<DeckCard>? OnCopyCard;   // create token copy of card

        private const double CardW = 90;
        private const double CardH = 126;

        private Window? _enlargedWindow;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        private readonly bool _faceDown; // true = show card backs (opp library)

        private readonly List<LinkedExile> _linkedExiles;

        public ZoneBrowserWindow(ZoneType zone, bool isYour,
            List<DeckCard> cards, DeckCard? commander = null,
            bool faceDown = false, List<LinkedExile>? linkedExiles = null)
        {
            InitializeComponent();
            _zone = zone;
            _isYour = isYour;
            _cards = cards;
            _commander = commander;
            _faceDown = faceDown;
            _linkedExiles = linkedExiles ?? new List<LinkedExile>();
            _isCommander = commander != null
                && cards.Any(c => c.Name == commander.Name);

            string ownerLabel = isYour ? "Your" : "Opponent's";
            string zoneName = zone switch
            {
                ZoneType.Graveyard => "Graveyard",
                ZoneType.Exile => "Exile",
                ZoneType.Library => "Library",
                _ => "Zone"
            };
            TitleText.Text = $"{ownerLabel} {zoneName}";
            Title = TitleText.Text;

            // Shuffle button only for library
            BtnShuffle.Visibility = zone == ZoneType.Library && isYour
                ? Visibility.Visible : Visibility.Collapsed;

            Rebuild();
        }

        // ================================================================
        // BUILD CARD DISPLAY
        // ================================================================
        private void Rebuild()
        {
            CardPanel.Children.Clear();

            if (_cards.Count == 0)
            {
                EmptyLabel.Visibility = Visibility.Visible;
                CountLabel.Text = "0 cards";
                return;
            }

            EmptyLabel.Visibility = Visibility.Collapsed;
            CountLabel.Text = $"{_cards.Count} card{(_cards.Count != 1 ? "s" : "")}";

            // Library: show top 10 only with indication of rest
            var display = _zone == ZoneType.Library
                ? _cards.Take(10).ToList()
                : _cards.ToList();

            foreach (var card in display)
            {
                var border = MakeCardBorder(card);
                CardPanel.Children.Add(border);
            }

            if (_zone == ZoneType.Library && _cards.Count > 10)
            {
                var more = new TextBlock
                {
                    Text = $"+{_cards.Count - 10}\nmore",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                CardPanel.Children.Add(more);
            }

            // Show linked exile groups
            if (_zone == ZoneType.Exile && _linkedExiles.Count > 0)
            {
                foreach (var link in _linkedExiles)
                {
                    var sep = new TextBlock
                    {
                        Text = $"\u2014 Exiled under {link.HostCardName} \u2014",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 4, 0)
                    };
                    CardPanel.Children.Add(sep);

                    foreach (var card in link.ExiledCards)
                    {
                        bool isCmd = _commander?.Name == card.Name;
                        var b = MakeCardBorder(card);
                        b.BorderBrush = new SolidColorBrush(
                            Color.FromRgb(0xFF, 0xD7, 0x00));
                        b.BorderThickness = new Thickness(2);
                        b.ToolTip = $"{card.Name} (Linked under {link.HostCardName})";
                        CardPanel.Children.Add(b);
                    }
                }
            }
        }

        private Border MakeCardBorder(DeckCard card)
        {
            bool isCmd = _commander != null && card.Name == _commander.Name;

            var border = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(4),
                BorderBrush = isCmd
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(isCmd ? 2.5 : 1.5),
                Cursor = Cursors.Hand,
                Margin = new Thickness(4),
                ToolTip = card.Name + (isCmd ? " ⚔ Commander" : "")
            };

            // Card image — face down shows card back only
            if (_faceDown)
            {
                border.Child = MakeCardBack();
            }
            else if (!string.IsNullOrEmpty(card.LocalImagePath)
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
                catch { border.Child = MakeTextBack(card.Name); }
            }
            else
                border.Child = MakeTextBack(card.Name);

            // Left click = view enlarged (not for face-down cards)
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (!_faceDown) ShowEnlarged(card);
                e.Handled = true;
            };

            // Right click = move menu (not for face-down — only shuffle is allowed)
            border.MouseRightButtonDown += (s, e) =>
            {
                if (!_faceDown) ShowMoveMenu(card, border, isCmd);
                e.Handled = true;
            };

            return border;
        }

        private static Border MakeCardBack()
        {
            // Use MTG_Back.png if available
            string backPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "MTG_Back.png");
            if (System.IO.File.Exists(backPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(backPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    return new Border
                    {
                        Child = new System.Windows.Controls.Image
                        { Source = bmp, Stretch = Stretch.Fill }
                    };
                }
                catch { }
            }
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E)),
                Child = new TextBlock
                {
                    Text = "🂠",
                    FontSize = 32,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private static Grid MakeTextBack(string name)
        {
            var g = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E))
            };
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

        // ================================================================
        // MOVE MENU
        // ================================================================
        private void ShowMoveMenu(DeckCard card, Border border, bool isCmd)
        {
            var menu = new ContextMenu();

            void AddMove(string header, string dest)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (s, e) =>
                {
                    OnMoveCard?.Invoke(card, dest);
                    _cards.Remove(card);
                    Rebuild();
                };
                menu.Items.Add(mi);
            }

            // Commander gets Command Zone option
            if (isCmd)
            {
                var cmdItem = new MenuItem { Header = "⚔ Return to Command Zone" };
                cmdItem.Click += (s, e) =>
                {
                    OnMoveCard?.Invoke(card, "command");
                    _cards.Remove(card);
                    Rebuild();
                };
                menu.Items.Add(cmdItem);
                menu.Items.Add(new Separator());
            }

            // Token copy — always available
            var copyItem = new MenuItem { Header = "🔁 Create Token Copy" };
            copyItem.Click += (s, e) => OnCopyCard?.Invoke(card);
            menu.Items.Add(copyItem);
            menu.Items.Add(new Separator());

            AddMove("▶ Play to Battlefield", "battlefield");
            AddMove("◈ Play to Mana Zone", "manazone");

            menu.Items.Add(new Separator());

            if (_zone != ZoneType.Graveyard)
                AddMove("💀 Move to Graveyard", "graveyard");
            if (_zone != ZoneType.Exile)
                AddMove("✦ Move to Exile", "exile");
            AddMove("🤚 Move to Hand", "hand");
            AddMove("📚 Library — Top", "libtop");
            AddMove("📚 Library — Bottom", "libbot");

            border.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // ================================================================
        // ENLARGED VIEW
        // ================================================================
        private void ShowEnlarged(DeckCard card)
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
        private void BtnShuffle_Click(object sender, RoutedEventArgs e)
        {
            OnShuffle?.Invoke();
            Rebuild();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _enlargedWindow?.Close();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _enlargedWindow?.Close();
            base.OnClosed(e);
        }
    }
}