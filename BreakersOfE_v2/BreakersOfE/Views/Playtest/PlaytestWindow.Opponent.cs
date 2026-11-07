using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    // Player 2, across the table: their mat (farther away, so smaller), their
    // badge and life, their hand held at the far edge (the backs of their
    // cards), their piles. For now it's to look at: their cards grow under the
    // mouse like yours; playing their side comes next. Part of PlaytestWindow.
    public partial class PlaytestWindow
    {
        /// <summary>The deck across the table for the next New Game (null: on your own).</summary>
        private Deck? _oppDeck;

        /// <summary>The last deck played against (New Game ▾ offers it again).</summary>
        private Deck? _lastOppDeck;

        private ImageSource _oppSleeve = CardPictures.CardBack;
        private BitmapSource? _oppMatSource;
        private string _oppMatKey = "";
        private string _oppPortrait = "\0";
        private bool _oppShown;

        /// <summary>Their card under the mouse (grown like yours; your keys don't act on it).</summary>
        private int _oppHoverId;
        private readonly Dictionary<int, FrameworkElement> _oppVisuals = new();

        /// <summary>The player across the table, when there is one.</summary>
        private Seat? Opp => _game != null && _game.Seats.Count > 1 ? _game.Seats.FirstOrDefault(s => s.Index != Me.Index) : null;

        private GameCard? OppHovered() => _oppHoverId == 0 ? null : Opp?.Find(_oppHoverId);

        // ── Sitting them down ────────────────────────────────────────────
        /// <summary>Player 2 sits down with this deck: shuffled, seven cards, kept (they don't mulligan yet).</summary>
        private void SeatOpponent(Deck deck)
        {
            int life = S.StartingLife > 0 ? S.StartingLife : PlaytestDecks.DefaultLife(deck);
            ManaSources.Register(PlaytestDecks.Produced(deck));
            var seat = _game.AddSeat(deck, S.OpponentNameOrDefault(), life, PlaytestDecks.Layouts(deck), PlaytestDecks.Loyalties(deck));
            KnowDeck(seat.Index, deck);                                    // their tokens and combos (they decide their own opening hand)
        }

        /// <summary>Ask for the deck to play against, then start.</summary>
        private void PickOpponent()
        {
            string? path = OpponentDeckWindow.Ask(this);
            if (path == null) return;
            Deck? deck;
            try
            {
                deck = DeckService.Load(path);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Playtest", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (deck == null || deck.PlayCards.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "That deck has no cards to play yet.", "Playtest", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            StartOver(deck);
        }

        // ── Their look: the table split, their sleeves and playmat ───────
        /// <summary>Show or hide their side of the table, and load their deck's sleeves and playmat (⚙ Table → Look, "This deck only").</summary>
        private void ShowOpponentLook()
        {
            var opp = Opp;
            bool two = opp != null;
            if (two != _oppShown)
            {
                // Their side takes the far part of the table (drag the line between to share it differently).
                _oppShown = two;
                OppArea.Visibility = OppSplitter.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
                OppRow.Height = two ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
                SplitRow.Height = two ? new GridLength(6) : new GridLength(0);
                MyRow.Height = new GridLength(two ? 3 : 1, GridUnitType.Star);
            }
            string first = _game.Seats.Count > 0 ? _game.Seats[0].DeckName : _deck.Name, second = two ? _game.Seats[1].DeckName : "";
            WindowTitleBar.Title = two ? $"Playtest — {first} against {second}" : $"Playtest — {_deck.Name}";
            DeckText.Text = two ? $"{first}  against  {second}" : _deck.Name;
            _oppVisuals.Clear();
            if (opp == null) return;
            _oppSleeve = LoadSleeve(DeckIdOf(opp));
            string mat = S.PlaymatPath(DeckIdOf(opp));
            _oppMatSource = mat.Length > 0 ? ImageCacheService.LoadBitmap(mat, 1600) : null;
            _oppMatKey = PlaytestSettings.PlaymatKey(S.PlaymatFor(DeckIdOf(opp)));
            _oppPortrait = "\0";
            ShowOppPlaymat();
        }

        private void OppFieldArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LayoutOppMat();
            ShowOppPlaymat();
        }

        /// <summary>Their mat: playmat proportions, as big as their part of the table allows (farther, so smaller).</summary>
        private void LayoutOppMat()
        {
            double w = Math.Max(80, OppFieldArea.ActualWidth - 12), h = Math.Max(40, OppFieldArea.ActualHeight - 8);
            double mw = Math.Min(w, h * MatAspect);
            OppMatBox.Width = mw;
            OppMatBox.Height = mw / MatAspect;
        }

        private void ShowOppPlaymat()
        {
            OppPlaymatImage.Source = _oppMatSource == null ? null : Framed(_oppMatSource, S.FramingOf(_oppMatKey), MatAspect);
            OppPlaymatShade.Opacity = OppPlaymatImage.Source != null ? Math.Clamp(S.PlaymatDim, 0, 0.85) : 0;
        }

        /// <summary>Their cards' size: yours, shrunk as their mat is smaller than yours.</summary>
        private double OppScale()
        {
            double mine = MatBox.ActualWidth, theirs = OppMatBox.ActualWidth;
            return mine > 1 && theirs > 1 ? Math.Clamp(theirs / mine, 0.3, 1) : 0.6;
        }

        /// <summary>Draw with a player's sleeve on face-down cards and backs (theirs across the table, yours at your side).</summary>
        private T InSleeveOf<T>(int seat, Func<T> draw)
        {
            if (_game == null || _game.Seats.Count < 2 || seat == Me.Index) return draw();
            var mine = _sleeve;
            _sleeve = _oppSleeve;
            try { return draw(); }
            finally { _sleeve = mine; }
        }

        // ── Drawing their side ───────────────────────────────────────────
        private void RenderOpponent()
        {
            _oppVisuals.Clear();
            var opp = Opp;
            if (opp == null) return;
            RenderOppBadge(opp);
            InSleeveOf(opp.Index, () =>
            {
                RenderOppField(opp);
                RenderOppHand(opp);
                RenderOppPiles(opp);
                return 0;
            });
        }

        /// <summary>Their badge: their picture (⚙ Table → Player), name and deck, life, and the gold edge on their turn.</summary>
        private void RenderOppBadge(Seat opp)
        {
            string path = BadgePathOf(opp);                                  // their own picture, whichever side they're on
            var f = S.FramingOf(PlaytestSettings.BadgeKey(BadgeFileOf(opp)));
            string shown = path.Length == 0 ? "" : $"{path}|{f.Zoom}|{f.CenterX}|{f.CenterY}";
            if (shown != _oppPortrait)
            {
                _oppPortrait = shown;
                var bmp = path.Length > 0 ? ImageCacheService.LoadBitmap(path, 300) : null;
                if (bmp != null)
                {
                    OppPortraitBox.Child = null;
                    OppPortraitBox.Background = new ImageBrush(Framed(bmp, f, 1.0, Brushes.Black)) { Stretch = Stretch.Fill };
                }
                else
                {
                    OppPortraitBox.Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
                    OppPortraitBox.Child = new TextBlock
                    {
                        Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 36,
                        Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    };
                }
            }
            OppName.Text = opp.Name.ToUpperInvariant() + (opp.DeckName.Length > 0 ? " · " + opp.DeckName.ToUpperInvariant() : "");
            OppLife.Text = opp.Life.ToString();
            var extra = new List<string> { $"HAND · {opp.Hand.Count}" };
            if (opp.Poison > 0) extra.Add($"POISON · {opp.Poison}");
            if (opp.Energy > 0) extra.Add($"ENERGY · {opp.Energy}");
            if (opp.ManaTotal > 0) extra.Add($"MANA · {opp.ManaText.ToUpperInvariant()}");
            foreach (var (cmdrId, taken) in opp.CommanderDamage.Where(kv => kv.Value > 0))
                extra.Add($"{(_game.FindAny(cmdrId)?.ShownName ?? "COMMANDER").ToUpperInvariant()} · {taken}/21");
            OppInfo.Text = string.Join("\n", extra);
            bool theirTurn = _game.ActiveSeat == opp.Index;
            OppPortraitBox.BorderBrush = theirTurn ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255));
            OppMatBox.BorderBrush = theirTurn ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
        }

        /// <summary>
        /// Their battlefield on their mat, as they laid it out, seen from across the
        /// table: what's near them is far from you (their places mirrored). Cards
        /// stay upright so you can read them; what's attached peeks out behind its card.
        /// </summary>
        private void RenderOppField(Seat opp)
        {
            OppField.Children.Clear();
            double w = CardW * OppScale(), h = w / CardAspect;
            double fw = Math.Max(1, OppField.ActualWidth - w), fh = Math.Max(1, OppField.ActualHeight - h);
            var cards = opp.Battlefield.Where(c => c.MergedInto == 0)
                                       .OrderBy(c => c.AttachedTo != 0 ? 0 : 1)        // attached first: behind its card
                                       .ToList();
            foreach (var c in cards)
            {
                var host = c.AttachedTo != 0 ? opp.Find(c.AttachedTo) : null;
                var at = host ?? c;
                var v = CardVisual(c, w, h);
                double x = (1 - at.X) * fw, y = (1 - at.Y) * fh;
                if (host != null) y = Math.Max(0, y - h * 0.18);
                Canvas.SetLeft(v, x);
                Canvas.SetTop(v, y);
                HookOpp(v, c);
                OppField.Children.Add(v);
            }
        }

        /// <summary>Their hand, held at the far edge: the backs of their cards (their sleeve), fanned, the rest below the edge of the table.</summary>
        private void RenderOppHand(Seat opp)
        {
            OppHand.Children.Clear();
            int n = opp.Hand.Count;
            if (n == 0) return;
            double w = PileW * 0.85, h = w / CardAspect;
            double room = Math.Max(w, OppHand.ActualWidth);
            double step = n == 1 ? 0 : Math.Min(w * 0.55, (room - w) / (n - 1));
            double start = (room - ((n - 1) * step + w)) / 2;
            double mid = (n - 1) / 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = n == 1 ? 0 : (i - mid) / Math.Max(1, mid);
                var b = SleeveVisual(w, h);
                b.Width = w;
                b.Height = h;
                b.RenderTransform = new RotateTransform(180 - t * Math.Min(10, 2 * n), w / 2, h / 2);     // held from their side, fanned
                b.ToolTip = $"{opp.Name}: {n} card{(n == 1 ? "" : "s")} in hand";
                Canvas.SetLeft(b, start + i * step);
                Canvas.SetTop(b, OppHand.Height - h - t * t * h * 0.06);                               // the ends of the fan sit a little farther
                OppHand.Children.Add(b);
            }
        }

        /// <summary>Their piles, small: command zone (when they have a commander), exile, graveyard, library.</summary>
        private void RenderOppPiles(Seat opp)
        {
            OppPiles.Children.Clear();
            double w = PileW * 0.72, h = w / CardAspect;
            bool commander = opp.Command.Count > 0 || opp.Battlefield.Concat(opp.Graveyard).Concat(opp.Exile).Concat(opp.Hand).Any(c => c.IsCommander);
            if (commander) OppPiles.Children.Add(OppPile(opp.Command.FirstOrDefault(), false, "COMMAND", w, h, () => OpenBrowser(Zone.Command, 0, opp)));
            OppPiles.Children.Add(OppPile(opp.Exile.FirstOrDefault(), false, $"EXILE · {opp.Exile.Count}", w, h, () => OpenBrowser(Zone.Exile, 0, opp)));
            OppPiles.Children.Add(OppPile(opp.Graveyard.FirstOrDefault(), false, $"GRAVEYARD · {opp.Graveyard.Count}", w, h, () => OpenBrowser(Zone.Graveyard, 0, opp)));
            OppPiles.Children.Add(OppPile(opp.Library.FirstOrDefault(), !opp.TopRevealed, $"LIBRARY · {opp.Library.Count}", w, h, null, opp));
        }

        /// <summary>Their library's menu: look at the top few, search, shuffle, mill, exile from the top, play with the top revealed.</summary>
        private void ShowOppLibraryMenu(Seat opp, FrameworkElement at)
        {
            var m = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom };
            m.Items.Add(new MenuItem { Header = $"{opp.Whose} library · {opp.Library.Count}", IsEnabled = false });
            m.Items.Add(Item("Look at the Top X…", () => { if (AskNumber("Look at the top", "How many cards?") is int n) OpenBrowser(Zone.Library, n, opp); }, enabled: opp.Library.Count > 0));
            m.Items.Add(Item("Search the Library…", () => OpenBrowser(Zone.Library, 0, opp), enabled: opp.Library.Count > 0));
            m.Items.Add(Item("Shuffle", () => _game.Shuffle(opp)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Mill X…", () => { if (AskNumber("Mill", "How many cards to the graveyard?") is int n) _game.MoveTop(opp, n, Zone.Graveyard); }, enabled: opp.Library.Count > 0));
            m.Items.Add(Item("Exile the Top X…", () => { if (AskNumber("Exile from the top", "How many cards?") is int n) _game.MoveTop(opp, n, Zone.Exile); }, enabled: opp.Library.Count > 0));
            m.Items.Add(new Separator());
            var reveal = new MenuItem { Header = "Played With the Top Card Revealed", IsCheckable = true, IsChecked = opp.TopRevealed };
            reveal.Click += (_, _) => _game.SetTopRevealed(opp, !opp.TopRevealed);
            m.Items.Add(reveal);
            m.IsOpen = true;
        }

        /// <summary>One of their piles: its top card (or a card back) and its name; a click looks through it (the library: its menu).</summary>
        private FrameworkElement OppPile(GameCard? top, bool hidden, string label, double w, double h, Action? open, Seat? library = null)
        {
            var sp = new StackPanel();
            FrameworkElement pic = top == null ? EmptySlot(w, h) : hidden ? SleeveVisual(w, h) : CardVisual(top, w, h);
            pic.HorizontalAlignment = HorizontalAlignment.Center;
            if (top != null && !hidden) HookOpp(pic, top);
            sp.Children.Add(pic);
            sp.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("PileName"), FontSize = 9 });
            var box = new Border { Style = (Style)FindResource("PileBox"), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(4, 3, 4, 3), Child = sp };
            if (open != null || library != null)
            {
                box.Cursor = Cursors.Hand;
                box.ToolTip = library != null ? "Their library: look at the top, search, mill …" : "Click to look through it";
                box.MouseLeftButtonUp += (_, ev) =>
                {
                    if (_pick != null || ev.Handled) return;                 // picking a target: the click was for that
                    ev.Handled = true;
                    if (library != null) ShowOppLibraryMenu(library, box); else open!();
                };
            }
            return box;
        }

        // ── Their cards under the mouse ──────────────────────────────────
        /// <summary>Their card grows under the mouse like yours (to read it); your keys and drags stay with your own cards.</summary>
        private void HookOpp(FrameworkElement v, GameCard c)
        {
            int id = c.Id;
            _oppVisuals[id] = v;
            v.MouseEnter += (_, _) =>
            {
                _oppHoverId = id;
                if (_drag?.Started != true && OppHovered() is { } oc) ShowGrown(oc, v);
            };
            // Their card as a target: click it while an ability waits for one. A double-click shows it big.
            v.MouseLeftButtonDown += (_, ev) =>
            {
                if (_pick is { } p && p.Kind == PickKind.Ability)
                {
                    ev.Handled = true;
                    if (Opp?.Find(id) is { } picked) CompletePick(picked);
                    return;
                }
                if (ev.ClickCount == 2 && Opp?.Find(id) is { } shown)
                {
                    ev.Handled = true;
                    Zoom(shown);
                }
            };
            v.MouseLeave += (_, _) =>
            {
                if (_oppHoverId == id) _oppHoverId = 0;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_oppVisuals.TryGetValue(id, out var ov) && PointerOn(ov)) return;
                    if (OppHovered() is { } oc && _oppVisuals.TryGetValue(oc.Id, out var hv) && PointerOn(hv)) return;
                    if (Find(_hoverId) is { } mc && _visuals.TryGetValue(mc.Id, out var mv) && PointerOn(mv)) return;
                    HoverLayer.Children.Clear();
                }), System.Windows.Threading.DispatcherPriority.Input);
            };
        }
    }
}
