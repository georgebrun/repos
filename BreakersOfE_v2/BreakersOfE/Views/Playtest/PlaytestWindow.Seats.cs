using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BreakersOfE.Models;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    // Two players on one screen: whose side of the table you sit at. "Me" is
    // the player sitting at the near side (their hand in front of you, the
    // buttons acting for them); the table turns round with the turn (⚙ Table →
    // Player), or with Switch Sides [W]. With Pass the Screen on, the table is
    // covered as it turns, until the next player clicks Ready. Each player keeps
    // their own deck's sleeves and playmat, and their own badge, whichever side
    // they're on. Part of PlaytestWindow.
    public partial class PlaytestWindow
    {
        /// <summary>The seat at the near side of the table (the player whose hand you see).</summary>
        private int _viewSeat;

        /// <summary>Whose turn it was at the last look (the table turns when it changes).</summary>
        private int _lastActive;

        /// <summary>A new two-player game: opening hands still being decided, one player after the other.</summary>
        private bool _openingHands;

        /// <summary>The next turn-round doesn't cover the table (an Undo, or you turned it yourself).</summary>
        private bool _quietTurn;

        private bool _seatCheckQueued;

        /// <summary>Each seat's printings' layouts (tokens made from their deck's Tokens part).</summary>
        private readonly Dictionary<int, Dictionary<string, string>> _seatLayouts = new();

        /// <summary>Each seat's deck combos (the combo alert watches the player at the near side).</summary>
        private readonly Dictionary<int, ComboWatch> _seatCombos = new();

        /// <summary>Combos found for a deck, kept while the table is open (finding them reads the combo list).</summary>
        private readonly Dictionary<string, ComboWatch> _comboCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The near-side player's layouts.</summary>
        private Dictionary<string, string> MyLayouts =>
            _game != null && _game.Seats.Count > 0 && _seatLayouts.TryGetValue(Me.Index, out var l) ? l : _layouts;

        /// <summary>A seat's deck id (sleeves and playmat follow it). A game saved before seats knew it: the first seat is this deck.</summary>
        private string DeckIdOf(Seat s) => s.DeckId.Length > 0 ? s.DeckId : s.Index == 0 ? _deck.DeckId ?? "" : "";

        /// <summary>The near-side player's deck id (the table before a game: this deck's).</summary>
        private string MyDeckId => _game != null && _game.Seats.Count > 0 ? DeckIdOf(Me) : _deck.DeckId ?? "";

        /// <summary>A seat's badge picture: the first seat is you (⚙ Table → Player → Badge), the second Player 2.</summary>
        private static string BadgeFileOf(Seat s) => s.Index == 0 ? S.Badge : S.OpponentBadge;

        private static string BadgePathOf(Seat s) => s.Index == 0 ? S.BadgePath() : S.OpponentBadgePath();

        /// <summary>Remember a seat's deck: its layouts and its combos.</summary>
        private void KnowDeck(int seat, Deck deck)
        {
            _seatLayouts[seat] = seat == 0 ? _layouts : PlaytestDecks.Layouts(deck);
            string key = string.IsNullOrEmpty(deck.DeckId) ? deck.Name : deck.DeckId;
            if (!_comboCache.TryGetValue(key, out var watch)) _comboCache[key] = watch = new ComboWatch(deck);
            watch.Reset();
            _seatCombos[seat] = watch;
        }

        private ComboWatch? ComboFor(Seat s) => _seatCombos.TryGetValue(s.Index, out var w) ? w : null;

        /// <summary>
        /// A game carried on from last time with a second player: find their deck
        /// again (by its id, in the Decks folder) for their tokens, combos and
        /// "Against … Again". Not found (renamed away, deleted): the game still plays.
        /// </summary>
        private void FindOpponentDeck()
        {
            if (Opp is not { } opp || DeckIdOf(opp).Length == 0) return;
            try
            {
                string id = DeckIdOf(opp);
                var hit = DeckIndexService.AllDecks().FirstOrDefault(d => string.Equals(d.Deck.DeckId, id, StringComparison.OrdinalIgnoreCase));
                if (hit.Path == null) return;
                var deck = DeckService.Load(hit.Path);
                if (deck == null) return;
                ManaSources.Register(PlaytestDecks.Produced(deck));
                KnowDeck(opp.Index, deck);
                _lastOppDeck = deck;
            }
            catch
            {
                // Their deck can't be read now: the game plays without its extras.
            }
        }

        // ── Turning the table ────────────────────────────────────────────
        private void BtnSwitchSides_Click(object sender, RoutedEventArgs e) => SwitchSides();

        /// <summary>Switch Sides [W]: sit at the other player's side (no cover: you turned it yourself).</summary>
        private void SwitchSides()
        {
            if (Opp is not { } other) return;
            SwitchTo(other.Index, cover: false, "");
        }

        /// <summary>Sit at this seat's side of the table; with <paramref name="cover"/> and Pass the Screen on, cover the table until Ready.</summary>
        private void SwitchTo(int seat, bool cover, string title)
        {
            if (_game == null || _game.Seats.Count < 2) return;
            seat = Math.Clamp(seat, 0, _game.Seats.Count - 1);
            if (seat != _viewSeat)
            {
                _viewSeat = seat;
                _selectedId = _hoverId = _oppHoverId = 0;
                _discardDown = 0;
                _handScroll = 0;
                HoverLayer.Children.Clear();
                ActionLayer.Children.Clear();
                ComboBanner.Visibility = Visibility.Collapsed;
                _portraitBadge = "\0";
                ApplySettings();                                         // this side's sleeves and playmat, the other side's too
                Render();
            }
            if (cover && S.PassScreen)
            {
                PassTitle.Text = title.Length > 0 ? title : $"{Me.Name}'s side";
                PassInfo.Text = $"{Me.DeckName}\nPass the screen{(Me.IsYou ? "" : " to " + Me.Name)}, then Ready: the hand stays hidden until then.";
                PassCover.Background = TableBrush(S.TableColor);
                PassCover.Visibility = Visibility.Visible;
                BtnPassReady.Focus();
            }
        }

        private void BtnPassReady_Click(object sender, RoutedEventArgs e) => PassReady();

        private void PassReady()
        {
            PassCover.Visibility = Visibility.Collapsed;
            Focus();
        }

        /// <summary>Next Step, Next Turn and the steps belong to the player whose turn it is: sit at their side first.</summary>
        private void EnsureActiveView()
        {
            if (Opp != null && _viewSeat != _game.ActiveSeat) SwitchTo(_game.ActiveSeat, cover: false, "");
        }

        /// <summary>After each redraw, once things settle: does the table turn?</summary>
        private void QueueSeatCheck()
        {
            if (_seatCheckQueued) return;
            _seatCheckQueued = true;
            Dispatcher.BeginInvoke(new Action(CheckSeats), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// The table turns round: in a new game, to the next player still deciding
        /// their opening hand, then back to whoever goes first; later, to whoever's
        /// turn it is now (⚙ Table → Player → The table turns to whoever's turn it is).
        /// </summary>
        private void CheckSeats()
        {
            _seatCheckQueued = false;
            if (_game == null || Opp == null) { _openingHands = false; return; }
            if (_drag?.Started == true) { QueueSeatCheck(); return; }
            bool quiet = _quietTurn;
            _quietTurn = false;
            if (_openingHands)
            {
                bool meDone = Me.Kept && Me.BottomPending == 0;
                var waiting = _game.Seats.FirstOrDefault(s => !s.Kept || s.BottomPending > 0);
                if (meDone && waiting != null)
                {
                    SwitchTo(waiting.Index, !quiet, $"{(waiting.IsYou ? "Your" : waiting.Name + "'s")} opening hand");
                    return;
                }
                if (waiting == null)
                {
                    _openingHands = false;
                    _lastActive = _game.ActiveSeat;
                    if (_viewSeat != _game.ActiveSeat)
                        SwitchTo(_game.ActiveSeat, !quiet, $"{(_game.Active.IsYou ? "You go" : _game.Active.Name + " goes")} first");
                    return;
                }
                if (!meDone) return;
            }
            if (_game.ActiveSeat == _lastActive) return;
            _lastActive = _game.ActiveSeat;
            if (S.FollowTurn && _viewSeat != _game.ActiveSeat)
                SwitchTo(_game.ActiveSeat, !quiet, _game.Active.IsYou ? "Your turn" : $"{_game.Active.Name}'s turn");
        }
    }
}
