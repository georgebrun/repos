using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// The playtest table (stage 1: solo goldfish). Draws the game
    /// (<see cref="Game"/>, in Core) and turns mouse and keys into moves:
    /// drag cards between hand, battlefield and the piles, double-click to
    /// play or tap, hover to see a card in the side panel. Its settings are its
    /// own (<see cref="PlaytestSettings"/>, the ⚙ Table button).
    /// Parts: this file (start, drawing), .Drag.cs (mouse), .Menus.cs (right-click, keys, buttons).
    /// </summary>
    public partial class PlaytestWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly Deck _deck;
        private readonly Dictionary<string, string> _layouts;
        private readonly Dictionary<string, string> _loyalties;
        private Game _game = null!;
        /// <summary>The player at the near side of the table (two players: whose side you sit at, PlaytestWindow.Seats.cs).</summary>
        private Seat Me => _game.Seats[Math.Clamp(_viewSeat, 0, Math.Max(0, _game.Seats.Count - 1))];
        private static PlaytestSettings S => PlaytestSettings.Current;

        /// <summary>The card under the mouse (keys act on it).</summary>
        private int _hoverId;

        /// <summary>Open the table for a deck (its own window; BoE stays usable behind it).</summary>
        public static void Open(Deck deck, Window? owner)
        {
            if (deck.PlayCards.Count == 0)
            {
                System.Windows.MessageBox.Show("This deck has no cards to play yet.", "Playtest",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var w = new PlaytestWindow(deck);
            if (owner != null) w.Owner = owner;
            w.Show();
        }

        private PlaytestWindow(Deck deck)
        {
            InitializeComponent();
            _deck = deck;
            _layouts = PlaytestDecks.Layouts(deck);
            _loyalties = PlaytestDecks.Loyalties(deck);
            ManaSources.Register(PlaytestDecks.Produced(deck));
            WindowTitleBar.Title = $"Playtest — {deck.Name}";
            DeckText.Text = deck.Name;
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewMouseMove += Window_PreviewMouseMove;
            PreviewMouseLeftButtonDown += Window_PreviewMouseLeftButtonDown;
            HandArea.MouseMove += HandArea_MouseMove;
            HandRow.MouseLeave += HandRow_MouseLeave;
            PreviewMouseLeftButtonUp += Window_PreviewMouseLeftButtonUp;
            LostMouseCapture += (_, _) => { if (_drag != null && Mouse.Captured == null) CancelDrag(); };
            Closed += (_, _) => { KeepGameOnClose(); _game.Changed -= Render; };
            ApplySettings();
            NewGame(OfferSavedGame());
        }

        // ── Starting ─────────────────────────────────────────────────────
        /// <summary>A new game, or (<paramref name="resume"/>) the one left last time.</summary>
        private void NewGame(Game.State? resume = null)
        {
            if (_game != null) _game.Changed -= Render;
            _game = resume != null ? Game.Load(resume) : new Game();
            _viewSeat = 0;                                                // the first player's side until the game is set up
            _game.FreeFirstMulligan = S.FreeFirstMulligan;                // each player by their own deck's format (Game.FirstMulliganFree)
            LogList.Items.Clear();
            foreach (var line in _game.Log) LogList.Items.Add(line);
            _game.Logged += line =>
            {
                LogList.Items.Add(line);
                LogList.ScrollIntoView(line);
            };
            _game.ShowReminders = S.ShowReminders;
            _game.Reminded += list =>                                     // combat's "whenever … attacks / blocks / dies"
            {
                _reminders.AddRange(list);
                RenderReminders();
            };
            _gameOverSeen = "";
            _seatLayouts.Clear();
            _seatCombos.Clear();
            KnowDeck(0, _deck);
            ComboBanner.Visibility = Visibility.Collapsed;
            _reminders.Clear();
            RenderReminders();
            if (resume == null)
            {
                int life = S.StartingLife > 0 ? S.StartingLife : PlaytestDecks.DefaultLife(_deck);
                _game.AddSeat(_deck, _oppDeck != null ? S.PlayerNameOrDefault() : "You", life, _layouts, _loyalties);   // two players: named, so the log reads the same from either side
                if (_oppDeck != null) SeatOpponent(_oppDeck);           // Player 2 across the table
                if (_game.Seats.Count > 1)
                {
                    int first = S.GoesFirst switch
                    {
                        FirstPlayer.You => 0,
                        FirstPlayer.PlayerTwo => 1,
                        _ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(2),
                    };
                    _game.ChooseFirst(first, S.GoesFirst == FirstPlayer.CoinFlip ? "Coin flip" : "Who goes first");
                }
            }
            else
            {
                _game.Note($"Carried on from the game saved {resume.Saved:g}.");
                FindOpponentDeck();
            }
            // Whose side you sit at: the first player's in a new game (opening hands go round), whoever's turn it is in one carried on.
            _viewSeat = _game.Seats.Count > 1 ? _game.ActiveSeat : 0;    // whoever goes first decides their opening hand first
            _lastActive = _game.ActiveSeat;
            _openingHands = resume == null && _game.Seats.Count > 1;
            _quietTurn = false;
            PassCover.Visibility = Visibility.Collapsed;
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
            _game.ManaEmpties = S.ManaEmpties;
            _game.Changed += Render;
            _hoverId = _selectedId = _lastPlayedId = 0;
            _discardDown = 0;
            _handScroll = 0;
            _oppHoverId = 0;
            _portraitBadge = "\0";
            ApplySettings();                                              // both sides' sleeves and playmats
            Render();
        }

        /// <summary>The table's settings (after the ⚙ Table window, and at the start).</summary>
        private void ApplySettings()
        {
            _sleeve = LoadSleeve(MyDeckId);                               // the near-side player's deck's look
            TableBorder.Background = TableBrush(S.TableColor);
            string mat = S.PlaymatPath(MyDeckId);
            _matSource = mat.Length > 0 ? ImageCacheService.LoadBitmap(mat, 2400) : null;
            _matKey = PlaytestSettings.PlaymatKey(S.PlaymatFor(MyDeckId));
            ShowPlaymat();
            if (_game is not null) ShowOpponentLook();
            if (_game is not null) _game.ManaEmpties = S.ManaEmpties;
            // The message bar along the bottom (the game log), or none.
            LogBar.Visibility = LogSplitter.Visibility = S.ShowLog ? Visibility.Visible : Visibility.Collapsed;
            LogRowDef.MinHeight = S.ShowLog ? 30 : 0;
            LogRowDef.Height = S.ShowLog ? (LogRowDef.Height.Value < 40 ? new GridLength(40) : LogRowDef.Height) : new GridLength(0);
            if (!S.ShowCardPanel) HoverLayer.Children.Clear();
        }

        // ── Sizes ────────────────────────────────────────────────────────
        private static double CardW => 100 * Math.Clamp(S.CardScale, 0.6, 1.8);
        private static double CardH => CardW * 680.0 / 488.0;
        private static double PileW => 64;
        private static double PileH => PileW * 680.0 / 488.0;
        internal const double CardAspect = 488.0 / 680.0;

        private void Area_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

        /// <summary>The battlefield's shape (width ÷ height): what the playmat is fitted to.</summary>
        internal double FieldAspect => MatAspect;

        // ── The playmat: the chosen part of the picture (Fit…), filling the battlefield ──
        private BitmapSource? _matSource;
        private string _matKey = "";

        private void FieldArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LayoutMat();
            ShowPlaymat();
        }

        private void ShowPlaymat()
        {
            PlaymatImage.Source = _matSource == null ? null : Framed(_matSource, S.FramingOf(_matKey), FieldAspect);
            PlaymatShade.Opacity = PlaymatImage.Source != null ? Math.Clamp(S.PlaymatDim, 0, 0.85) : 0;
        }

        /// <summary>
        /// A picture placed in a frame shaped <paramref name="aspect"/> (width ÷ height)
        /// the way its framing says (moved, smaller or bigger). Where the
        /// picture doesn't reach, <paramref name="gap"/> shows (none = see-through,
        /// so the table color shows under a playmat).
        /// </summary>
        internal static ImageSource Framed(BitmapSource src, Framing f, double aspect, Brush? gap = null)
        {
            try
            {
                double fh = 1000, fw = 1000 * Math.Clamp(aspect, 0.1, 10);
                var (l, t, w, h) = f.Place(src.PixelWidth, src.PixelHeight, fw, fh);
                var frame = new Rect(0, 0, fw, fh);
                var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(frame) };
                g.Children.Add(new GeometryDrawing(gap ?? Brushes.Transparent, null, new RectangleGeometry(frame)));
                g.Children.Add(new ImageDrawing(src, new Rect(l, t, w, h)));
                var img = new DrawingImage(g);
                img.Freeze();
                return img;
            }
            catch
            {
                return src;
            }
        }

        // ── Pictures ─────────────────────────────────────────────────────
        private ImageSource _sleeve = CardPictures.CardBack;
        private readonly Dictionary<string, ImageSource> _pics = new();
        private readonly HashSet<string> _loading = new();
        private bool _renderQueued;

        /// <summary>A picture finished loading (the zone browser redraws too).</summary>
        internal event Action? PicturesArrived;

        /// <summary>The table color from "#RRGGBB" (the default when it can't be read).</summary>
        internal static SolidColorBrush TableBrush(string? hex)
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex ?? "") is Color c) return new SolidColorBrush(c);
            }
            catch
            {
                // Not a color: the default below.
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(PlaytestSettings.DefaultTableColor));
        }

        private static ImageSource LoadSleeve(string? deckId)
        {
            string p = S.SleevePath(deckId);
            if (p.Length == 0) return CardPictures.CardBack;
            var bmp = ImageCacheService.LoadBitmap(p, 800);
            if (bmp == null) return CardPictures.CardBack;
            return Framed(bmp, S.FramingOf(PlaytestSettings.SleeveKey(S.SleeveFor(deckId))), CardAspect, Brushes.Black);
        }

        /// <summary>The picture for a card as it shows now: the sleeve while face down (or while loading).</summary>
        private ImageSource Picture(GameCard c, bool hidden = false)
        {
            if (hidden || c.FaceDown) return _sleeve;
            bool back = c.BackUp && c.HasBack;
            string key = back ? "B:" + c.Card.ImageBackUrl : "F:" + c.ScryfallId + "|" + c.Card.ImageNormalUrl;
            if (_pics.TryGetValue(key, out var pic)) return pic;
            if (_loading.Add(key)) _ = LoadAsync(key, c.Card, back);
            return _sleeve;
        }

        /// <summary>Front picture of a card that isn't on the table (the modal-face chooser).</summary>
        private ImageSource FacePicture(DeckCard card, bool back)
        {
            string key = back ? "B:" + card.ImageBackUrl : "F:" + card.ScryfallId + "|" + card.ImageNormalUrl;
            if (_pics.TryGetValue(key, out var pic)) return pic;
            if (_loading.Add(key)) _ = LoadAsync(key, card, back);
            return _sleeve;
        }

        private async Task LoadAsync(string key, DeckCard card, bool back)
        {
            ImageSource? pic = null;
            try
            {
                if (back)
                {
                    if (!string.IsNullOrEmpty(card.LocalImageBackPath) && System.IO.File.Exists(card.LocalImageBackPath))
                        pic = await Task.Run(() => ImageCacheService.LoadBitmap(card.LocalImageBackPath, 300));
                    pic ??= await ImageCacheService.GetPictureAsync(null, card.ImageBackUrl, 300);
                }
                else
                {
                    pic = await ImageCacheService.GetPictureAsync(card.ScryfallId, card.ImageNormalUrl, 300);
                    if (pic == null && !string.IsNullOrEmpty(card.LocalImagePath) && System.IO.File.Exists(card.LocalImagePath))
                        pic = await Task.Run(() => ImageCacheService.LoadBitmap(card.LocalImagePath, 300));
                }
            }
            catch
            {
                pic = null;
            }
            await Dispatcher.InvokeAsync(() =>
            {
                _pics[key] = pic ?? CardPictures.NotFound;
                _loading.Remove(key);
                QueueRender();
                PicturesArrived?.Invoke();
            });
        }

        /// <summary>Many pictures arrive at once: draw once for all of them.</summary>
        private void QueueRender()
        {
            if (_renderQueued) return;
            _renderQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _renderQueued = false; Render(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ── Drawing the table ────────────────────────────────────────────
        private void Render()
        {
            if (_game == null || _game.Seats.Count == 0) return;
            if (_drag?.Started == true) return;          // not mid-drag: the dragged card's place would jump
            _visuals.Clear();
            if (_selectedId != 0 && Find(_selectedId) == null) _selectedId = 0;    // gone (a token left, or Undo)
            RenderField();
            RenderFan();
            RenderHand();
            RenderPiles();
            RenderMulligan();
            RenderCombat();
            RenderGameOver();
            LifeText.Text = Me.Life.ToString();
            var taken = Me.CommanderDamage.Where(kv => kv.Value > 0).Select(kv => $"{(_game.FindAny(kv.Key)?.ShownName ?? "Commander").ToUpperInvariant()} · {kv.Value}/21").ToList();
            MyInfo.Text = string.Join("\n", taken);
            MyInfo.Visibility = taken.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TurnText.Text = _game.Seats.Count > 1 ? $"Turn {_game.Turn} · {(_game.Active.IsYou ? "You" : _game.Active.Name)}" : $"Turn {_game.Turn}";
            BtnSwitchSides.Visibility = _game.Seats.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            RenderPhaseBar();
            RenderPlayerCounters();
            RenderMana();
            RenderBadge();
            RenderOpponent();
            RenderStack();
            CheckCombos();
            QueueSeatCheck();
            BtnUndo.IsEnabled = _game.CanUndo;
            BtnUndo.ToolTip = _game.CanUndo ? $"Undo: {_game.UndoWhat} (Ctrl+Z)" : "Nothing to undo (Ctrl+Z)";
            BtnTokens.ToolTip = Me.Tokens.Count > 0
                ? "Make a token: this deck's Tokens part, or Any Token… from the card pool"
                : "Make a token: Any Token… finds any token in the card pool (Suggested Tokens… on the deck page fills this deck's list)";
            // The action bar goes beside the selected card once the new pictures are laid out.
            Dispatcher.BeginInvoke(new Action(PlaceActionBar), System.Windows.Threading.DispatcherPriority.Loaded);
            // The grown card under the mouse follows the redraw (or goes, when the mouse has moved off).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Find(_hoverId) is { } hc && _visuals.TryGetValue(hc.Id, out var hv) && PointerOn(hv)) ShowHover(hc);
                else if (OppHovered() is { } oc && _oppVisuals.TryGetValue(oc.Id, out var ov) && PointerOn(ov)) ShowGrown(oc, ov);
                else HoverLayer.Children.Clear();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>One card as it sits on the table: picture, counters, tapped / flipped.</summary>
        private FrameworkElement CardVisual(GameCard c, double w, double h, bool hidden = false)
        {
            var img = new Image
            {
                Source = Picture(c, hidden), Width = w, Height = h, Stretch = Stretch.Fill,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var grid = new Grid { Width = w, Height = h };
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(w * 0.045), ClipToBounds = true, Child = img,
                // Selected: gold. Under the mouse: a light edge.
                BorderThickness = new Thickness(c.Id == _selectedId ? 3 : c.Id == _hoverId ? 2 : 0),
                BorderBrush = c.Id == _selectedId ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            });
            if (!hidden && c.Counters.Count > 0)
            {
                grid.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(4, 0, 4, h * 0.12), Padding = new Thickness(4, 1, 4, 1),
                    CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(225, 20, 20, 20)),
                    Child = new TextBlock
                    {
                        Text = c.CountersText, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
                        FontSize = Math.Max(10, w * 0.11), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    },
                });
            }
            if (!hidden && c.IsToken)
                grid.Children.Add(Badge("TOKEN", w, top: true));
            if (c.FaceDown && !hidden)
                grid.Children.Add(Badge("FACE DOWN", w, top: true));
            if (!hidden && c.Damage > 0)
            {
                bool lethal = c.ToughnessNow is int t && c.Damage >= t;
                grid.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(lethal ? Color.FromArgb(235, 190, 30, 30) : Color.FromArgb(230, 150, 60, 20)),
                    Child = new TextBlock
                    {
                        Text = $"{c.Damage} damage" + (lethal ? " · lethal" : ""), Foreground = Brushes.White,
                        FontSize = Math.Max(10, w * 0.11), FontWeight = FontWeights.Bold,
                    },
                });
            }
            // Power and toughness when counters or until-end-of-turn changes make them differ from what's printed.
            // A printed * the table counted (Tarmogoyf, a Lhurgoyf) shows too, so you can see what it made of it.
            bool starred = c.IsCreatureNow && c.HasStar && (c.StarPower != null || c.StarToughness != null);
            if (!hidden && !c.FaceDown && (c.PowerToughnessChanged || starred) && c.PowerNow is int pw && c.ToughnessNow is int tn)
            {
                int boost = c.PowerMod + c.ToughnessMod +
                            2 * (c.Counters.GetValueOrDefault("+1/+1") - c.Counters.GetValueOrDefault("-1/-1"));     // up: green, down: red
                grid.Children.Add(new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, w * 0.03, h * 0.015), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(boost > 0 ? Color.FromArgb(240, 30, 120, 50)
                                                   : boost < 0 ? Color.FromArgb(240, 170, 40, 40) : Color.FromArgb(240, 120, 95, 20)),
                    BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                    ToolTip = starred && c.PowerMod == 0 && c.ToughnessMod == 0 && !c.PowerToughnessChanged
                        ? $"Its * counted by the table from its rules text ({c.Card.Power}/{c.Card.Toughness} printed)"
                        : c.PowerMod != 0 || c.ToughnessMod != 0
                        ? $"Until end of turn: {(c.PowerMod >= 0 ? "+" : "")}{c.PowerMod}/{(c.ToughnessMod >= 0 ? "+" : "")}{c.ToughnessMod}"
                        : "With its counters",
                    Child = new TextBlock
                    {
                        Text = $"{pw}/{tn}", Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                        FontSize = Math.Max(11, w * 0.13),
                    },
                });
            }
            if (!hidden && !c.FaceDown && S.ShowKeywords && c.IsCreatureNow && Me.ZoneOf(c) == Zone.Battlefield)
                AddKeywordBadges(grid, c, w, h);
            if (!hidden && CombatMark(c) is { Length: > 0 } mark)
            {
                var cb = Badge(mark, w, top: false);
                cb.Background = new SolidColorBrush(c.Blocking != 0 ? Color.FromArgb(230, 30, 70, 140) : Color.FromArgb(230, 160, 30, 30));
                cb.Margin = new Thickness(0, 0, 0, h * 0.30);
                grid.Children.Add(cb);
            }
            double angle = (c.Tapped ? 90 : 0) + (c.Flipped ? 180 : 0);
            if (angle != 0) grid.RenderTransform = new RotateTransform(angle, w / 2, h / 2);
            grid.Tag = c.Id;
            return grid;
        }

        private static Border Badge(string text, double w, bool top) => new()
        {
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, top ? w * 0.09 : 0, 0, 0), Padding = new Thickness(5, 0, 5, 1),
            CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = Math.Max(9, w * 0.09), FontWeight = FontWeights.Bold },
        };

        private void RenderField()
        {
            Field.Children.Clear();
            double w = CardW, h = CardH;
            double fw = Math.Max(1, Field.ActualWidth - w), fh = Math.Max(1, Field.ActualHeight - h);
            foreach (var group in FieldGroups())
            {
                // What's attached to it first, so it sits behind: cascading down under the card, each
                // showing its rules text (its picture, name and cost tucked under the card above).
                if (group.Count == 1)
                {
                    var attached = AttachedTo(group[0]);
                    var (peek, shown) = Cascade(group[0], attached.Count, h, fh);
                    for (int k = shown - 1; k >= 0; k--)
                    {
                        var a = attached[k];
                        var av = CardVisual(a, w, h);
                        if (k == shown - 1 && shown < attached.Count && av is Grid ag)
                            ag.Children.Add(Badge($"+{attached.Count - shown} MORE UNDER", w, top: false));
                        Canvas.SetLeft(av, group[0].X * fw);
                        Canvas.SetTop(av, group[0].Y * fh + (k + 1) * peek);
                        Hook(av, a, Zone.Battlefield);
                        Field.Children.Add(av);
                    }
                    // The rest (no room): still selectable through the card's menu and the zoom.
                    foreach (var a in attached.Skip(shown)) _visuals.Remove(a.Id);
                }
                // A pile of basic lands acts as its face card: selected, hovered and acted on as one.
                var c = Face(group);
                if (group.Count > 1)
                {
                    if (group.Any(g => g.Id == _selectedId)) _selectedId = c.Id;
                    if (group.Any(g => g.Id == _hoverId)) _hoverId = c.Id;
                }
                var v = group.Count > 1 ? PileVisual(group, w, h) : CardVisual(c, w, h);
                int merged = Me.Battlefield.Count(o => o.MergedInto == c.Id);
                if (merged > 0 && v is Grid vg) vg.Children.Add(Badge($"MUTATED ×{merged + 1}", w, top: false));
                Canvas.SetLeft(v, c.X * fw);
                Canvas.SetTop(v, c.Y * fh);
                Hook(v, c, Zone.Battlefield);
                foreach (var m in group) _visuals[m.Id] = v;
                Field.Children.Add(v);
            }
        }

        private void RenderPiles()
        {
            double w = PileW, h = PileH;

            // Library: the sleeve, face down.
            LibrarySlot.Children.Clear();
            if (Me.Library.Count > 0 && Me.TopRevealed)
            {
                var top = CardVisual(Me.Library[0], w, h);
                Hook(top, Me.Library[0], Zone.Library);
                LibrarySlot.Children.Add(top);
            }
            else LibrarySlot.Children.Add(Me.Library.Count > 0 ? SleeveVisual(w, h) : EmptySlot(w, h));
            LibraryText.Text = Me.TopRevealed ? $"LIBRARY · {Me.Library.Count} · TOP SHOWN" : $"LIBRARY · {Me.Library.Count}";

            // Graveyard and exile: the last card put there.
            FillTopCard(GraveyardSlot, Me.Graveyard, Zone.Graveyard, w, h);
            GraveyardText.Text = $"GRAVEYARD · {Me.Graveyard.Count}";
            FillTopCard(ExileSlot, Me.Exile, Zone.Exile, w, h);
            ExileText.Text = $"EXILE · {Me.Exile.Count}";

            // Command zone: commanders waiting, with their tax.
            CommandSlot.Children.Clear();
            bool any = Me.Command.Count > 0 || Me.Battlefield.Concat(Me.Graveyard).Concat(Me.Exile).Concat(Me.Hand).Concat(Me.Library)
                                                .Any(c => c.IsCommander);
            CommandPile.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            foreach (var c in Me.Command)
            {
                var v = CardVisual(c, w, h);
                v.Margin = new Thickness(2, 0, 2, 0);
                Hook(v, c, Zone.Command);
                CommandSlot.Children.Add(v);
            }
            if (Me.Command.Count == 0) CommandSlot.Children.Add(EmptySlot(w, h));
            var taxes = Me.Command.Concat(Me.Battlefield).Concat(Me.Graveyard).Concat(Me.Exile)
                          .Where(c => c.IsCommander && Me.CommanderTax(c) > 0).ToList();
            CommandText.Text = taxes.Count == 0 ? "COMMAND ZONE"
                : "TAX " + string.Join(" · ", taxes.Select(c => $"+{Me.CommanderTax(c)}"));
        }

        private void FillTopCard(Grid slot, List<GameCard> pile, Zone zone, double w, double h)
        {
            slot.Children.Clear();
            if (pile.Count == 0) { slot.Children.Add(EmptySlot(w, h)); return; }
            var v = CardVisual(pile[0], w, h);
            Hook(v, pile[0], zone);
            slot.Children.Add(v);
        }

        private FrameworkElement SleeveVisual(double w, double h)
        {
            var img = new Image { Source = _sleeve, Width = w, Height = h, Stretch = Stretch.Fill };
            return new Border { CornerRadius = new CornerRadius(w * 0.045), ClipToBounds = true, Child = img };
        }

        private static FrameworkElement EmptySlot(double w, double h) => new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(w * 0.045),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
        };

        // ── The opening hand ─────────────────────────────────────────────
        private void RenderMulligan()
        {
            if (RenderPickBar()) return;                              // choosing what to attach to / mutate onto
            BtnKeep.Content = "Keep (K)";
            BtnSkipDiscard.Visibility = Visibility.Collapsed;
            BtnSkipDiscard.Content = "Skip";
            BtnKeep.Visibility = BtnMulligan.Visibility = Visibility.Visible;
            if (_discardDown > 0)
            {
                // End of turn with too many cards: click the ones to discard (or Skip).
                MulliganBar.Visibility = Visibility.Visible;
                MulliganButtons.Visibility = Visibility.Visible;
                BtnKeep.Visibility = BtnMulligan.Visibility = Visibility.Collapsed;
                BtnSkipDiscard.Visibility = Visibility.Visible;
                MulliganTitle.Text = _discardWhy.Length > 0 ? _discardWhy
                    : $"End of turn: {Me.Hand.Count} cards in hand, the most is {S.MaxHandSize}. Click {_discardDown} to discard.";
                MulliganInfo.Text = _discardWhy.Length > 0 ? "Skip to discard them yourself (drag to the graveyard)."
                    : "Or Skip if something lets you keep more (Reliquary Tower, Thought Vessel …). No maximum: ⚙ Table.";
                return;
            }
            if (Me.Kept && Me.BottomPending == 0)
            {
                MulliganBar.Visibility = Visibility.Collapsed;
                return;
            }
            MulliganBar.Visibility = Visibility.Visible;
            if (Me.Kept)
            {
                MulliganButtons.Visibility = Visibility.Collapsed;
                MulliganTitle.Text = $"Click {Me.BottomPending} card{(Me.BottomPending == 1 ? "" : "s")} in your hand to put on the bottom of your library.";
                MulliganInfo.Text = "London mulligan: one card for each mulligan" +
                                    (_game.FirstMulliganFree(Me) ? " (the first was free)." : ".");
                return;
            }
            MulliganButtons.Visibility = Visibility.Visible;
            var sum = Game.Summarize(Me.Hand);
            int bottom = _game.ToBottomOnKeep(Me);
            MulliganTitle.Text = (Me.Mulligans == 0 ? "Opening hand" : $"Mulligan {Me.Mulligans}") +
                $": {sum.Lands} land{(sum.Lands == 1 ? "" : "s")}, {sum.Spells} spell{(sum.Spells == 1 ? "" : "s")}" +
                (bottom > 0 ? $" · keeping puts {bottom} on the bottom" : "");
            var parts = new List<string>();
            if (sum.LandColors.Length > 0) parts.Add($"Lands make {ColorWords(sum.LandColors)}");
            if (sum.SpellColors.Length > 0) parts.Add($"spells need {ColorWords(sum.SpellColors)}");
            if (sum.MissingColors.Length > 0) parts.Add($"no land yet for {ColorWords(sum.MissingColors)}");
            if (sum.ManaValues.Count > 0) parts.Add("mana values " + string.Join(", ", sum.ManaValues));
            if (sum.ModalLands > 0) parts.Add($"{sum.ModalLands} spell{(sum.ModalLands == 1 ? "" : "s")} with a land on the back");
            MulliganInfo.Text = parts.Count == 0 ? "" : string.Join(" · ", parts) + ".";
            BtnKeep.ToolTip = bottom > 0 ? $"Keep, then pick {bottom} card{(bottom == 1 ? "" : "s")} to put on the bottom" : "Keep this hand";
        }

        private static string ColorWords(string colors) => string.Join(" ", colors.Select(c => c switch
        {
            'W' => "white", 'U' => "blue", 'B' => "black", 'R' => "red", 'G' => "green", _ => c.ToString(),
        }));

        // ── The card under the mouse ─────────────────────────────────────
        /// <summary>The card under the mouse: grown to 200% where it sits (the hand lifts its own card).</summary>
        private void ShowInPanel(GameCard? c) => ShowHover(c);

        /// <summary>The live card with this id (cards are copied by Undo, so look them up by id).</summary>
        private GameCard? Find(int id) => id == 0 || _game.Seats.Count == 0 ? null : Me.Find(id);
    }
}
