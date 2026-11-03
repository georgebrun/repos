using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BreakersOfE.Playtest;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// Looking through a zone at the playtest table: the graveyard, exile, the
    /// whole library (a search) or its top few cards. Each card can go
    /// anywhere (right-click), or to the hand (double-click). Stays open and
    /// follows the game; the library search ends with Shuffle and Close.
    /// </summary>
    internal sealed class ZoneBrowserWindow : Window
    {
        private readonly PlaytestWindow _table;
        private readonly Game _game;
        private readonly int _seat;
        private readonly Zone _zone;
        private readonly int _topOnly;
        private readonly Action<GameCard> _play;
        private readonly Action<GameCard> _hover;
        private readonly WrapPanel _tiles = new() { Margin = new Thickness(8) };
        private readonly TextBox _filter = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0) };
        private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        private Seat Seat => _game.Seats[_seat];

        /// <summary>A card's abilities from where it is (the table's "Abilities" menu), or null.</summary>
        internal Func<GameCard, MenuItem?>? AbilityMenu { get; init; }

        internal ZoneBrowserWindow(PlaytestWindow table, Game game, Seat seat, Zone zone, int topOnly,
                                   Func<GameCard, ImageSource> picture, Action<GameCard> play, Action<GameCard> hover)
        {
            _table = table;
            _game = game;
            _seat = seat.Index;
            _zone = zone;
            _topOnly = topOnly;
            _picture = picture;
            _play = play;
            _hover = hover;
            Owner = table;
            Width = 920;
            Height = 620;
            MinWidth = 520;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

            var top = new DockPanel { Margin = new Thickness(12, 10, 12, 4) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(right, Dock.Right);
            if (zone == Zone.Library && topOnly == 0)
            {
                right.Children.Add(new TextBlock { Text = "Find", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                right.Children.Add(_filter);
                _filter.TextChanged += (_, _) => Rebuild();
            }
            top.Children.Add(right);
            top.Children.Add(_title);

            var hint = new TextBlock
            {
                Margin = new Thickness(12, 0, 12, 4), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Text = zone == Zone.Hand
                    ? "Double-click a card to play it; right-click to send it anywhere else."
                    : "Double-click a card to put it in your hand; right-click to send it anywhere else.",
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 6, 12, 12) };
            if (zone == Zone.Library)
            {
                var shuffle = new Button { Content = "Shuffle and Close", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
                shuffle.Click += (_, _) => { _game.Shuffle(Seat); Close(); };
                buttons.Children.Add(shuffle);
            }
            var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
            close.Click += (_, _) => Close();
            buttons.Children.Add(close);

            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(hint, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(top);
            dock.Children.Add(hint);
            dock.Children.Add(buttons);
            dock.Children.Add(new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;

            _game.Changed += Rebuild;
            _table.PicturesArrived += Rebuild;
            Closed += (_, _) => { _game.Changed -= Rebuild; _table.PicturesArrived -= Rebuild; };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            Rebuild();
        }

        private readonly Func<GameCard, ImageSource> _picture;

        private void Rebuild()
        {
            var all = Seat.ZoneList(_zone);
            var cards = _topOnly > 0 ? all.Take(_topOnly).ToList() : all.ToList();
            string find = _filter.Text.Trim();
            if (find.Length > 0)
                cards = cards.Where(c => c.Card.Name.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                                         c.Card.TypeLine.Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
            string zoneName = Game.ZoneWord(_zone);
            Title = _topOnly > 0 ? $"Top {_topOnly} of your library" : $"Your {zoneName}";
            _title.Text = _topOnly > 0
                ? $"Top {Math.Min(_topOnly, all.Count)} of your library (top first)"
                : $"Your {zoneName} · {all.Count} card{(all.Count == 1 ? "" : "s")}" +
                  (_zone == Zone.Library ? " (top first)" : _zone == Zone.Graveyard ? " (newest first)" : "");

            _tiles.Children.Clear();
            if (cards.Count == 0)
            {
                _tiles.Children.Add(new TextBlock { Text = all.Count == 0 ? "Empty." : "Nothing matches.", Margin = new Thickness(8) });
                return;
            }
            foreach (var c in cards)
            {
                var img = new Image { Source = _picture(c), Width = 130, Height = 130 * 680.0 / 488.0, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var tile = new StackPanel { Margin = new Thickness(6), Width = 130, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                tile.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = img });
                tile.Children.Add(new TextBlock
                {
                    Text = c.FaceName(0), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
                });
                var card = c;
                tile.MouseEnter += (_, _) => _hover(card);
                tile.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount != 2) return;
                    if (_zone == Zone.Hand) _play(card); else _game.Move(card, Zone.Hand);
                };
                tile.MouseRightButtonUp += (_, e) => { e.Handled = true; Menu(card, tile); };
                _tiles.Children.Add(tile);
            }
        }

        private void Menu(GameCard c, FrameworkElement target)
        {
            var m = new ContextMenu { PlacementTarget = target };
            void Add(string header, Action act)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => act();
                m.Items.Add(mi);
            }
            if (_zone != Zone.Hand) Add("Hand", () => _game.Move(c, Zone.Hand));
            Add("Battlefield", () => _play(c));
            if (_zone != Zone.Graveyard) Add("Graveyard", () => _game.Move(c, Zone.Graveyard));
            if (_zone != Zone.Exile) Add("Exile", () => _game.Move(c, Zone.Exile));
            Add("Top of Library", () => _game.Move(c, Zone.Library, Place.Top));
            Add("Bottom of Library", () => _game.Move(c, Zone.Library, Place.Bottom));
            if (c.IsCommander) Add("Command Zone", () => _game.Move(c, Zone.Command));
            if (AbilityMenu?.Invoke(c) is { } abilities)
            {
                m.Items.Add(new Separator());
                m.Items.Add(abilities);
            }
            m.Items.Add(new Separator());
            Add("View Card…", () => new CardDetailWindow(c.Card, this).Show());
            m.IsOpen = true;
        }
    }
}
