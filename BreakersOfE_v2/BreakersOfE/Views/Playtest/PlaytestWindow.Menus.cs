using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BreakersOfE.Playtest;
using BreakersOfE.Views.Dialogs;

namespace BreakersOfE.Views.Playtest
{
    // Right-click menus, the keys, and the buttons of the playtest table.
    // Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        // ── Menus ────────────────────────────────────────────────────────
        private static MenuItem Item(string header, Action act, string gesture = "", bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            return mi;
        }

        private static MenuItem Sub(string header, params object[] items)
        {
            var mi = new MenuItem { Header = header };
            foreach (var i in items) mi.Items.Add(i);
            return mi;
        }

        private static void Open(ContextMenu menu, FrameworkElement target)
        {
            menu.PlacementTarget = target;
            menu.IsOpen = true;
        }

        private void ShowCardMenu(int id, Zone zone, FrameworkElement target)
        {
            var c = Find(id);
            if (c == null) return;
            var m = new ContextMenu();
            switch (zone)
            {
                case Zone.Battlefield:
                    if (PileOf(c) is { Count: > 1 } pile)
                    {
                        m.Items.Add(Item($"Tap One {c.FaceName(0)}", () => TapOne(pile), "Enter"));
                        if (pile.Any(p => p.Tapped)) m.Items.Add(Item("Untap One", () => UntapOne(pile)));
                        m.Items.Add(Item($"Tap All {pile.Count}", () => TapCards(pile)));
                        m.Items.Add(Item("Untap All of Them", () => _game.SetTapped(Me, pile, false)));
                        m.Items.Add(new Separator());
                        m.Items.Add(new MenuItem { Header = "One of the pile:", IsEnabled = false });
                    }
                    m.Items.Add(Item(c.Tapped ? "Untap" : "Tap", () => ToggleTap(c), "Enter"));
                    if (ManaMenu(c) is { } mana) m.Items.Add(mana);
                    if (AbilitiesMenu(c) is { } abilities) m.Items.Add(abilities);
                    if (c.HasBack) m.Items.Add(Item("Turn Over", () => _game.TurnOver(c), "T"));
                    if (c.CanFlip) m.Items.Add(Item(c.Flipped ? "Flip Back" : "Flip", () => _game.Flip(c), "F"));
                    m.Items.Add(Item(c.FaceDown ? "Turn Face Up" : "Turn Face Down", () => _game.SetFaceDown(c, !c.FaceDown)));
                    if (c.AttachedTo != 0)
                        m.Items.Add(Item($"Unattach (from {Find(c.AttachedTo)?.ShownName ?? "its card"})", () => _game.Unattach(c, c.X + 0.05, c.Y)));
                    else
                        m.Items.Add(Item("Attach To…", () => StartPick(c, PickKind.Attach)));
                    if (_game.MergedCards(c) is { Count: > 1 } mergedCards)
                        m.Items.Add(new MenuItem { Header = "Mutated: " + string.Join(" + ", mergedCards.Select(x => x.FaceName(0))), IsEnabled = false });
                    m.Items.Add(CountersMenu(c));
                    if (c.IsCreatureNow) m.Items.Add(PumpMenu(c));
                    if (c.IsCreatureNow) m.Items.Add(DamageMenu(c));
                    m.Items.Add(Item("Create a Token Copy", () =>
                        _game.CreateToken(Me, c.Card, c.X + 0.03, c.Y + 0.03, c.Layout, loyalty: c.Loyalty)));
                    m.Items.Add(new Separator());
                    m.Items.Add(MoveMenu(c, zone));
                    break;
                case Zone.Hand:
                    m.Items.Add(Item(c.IsModal ? "Play… (pick a face)" : "Play", () => PlayCard(c), "Enter"));
                    if (!c.IsInstantOrSorceryNow) m.Items.Add(Item("Play Tapped", () => PlayCard(c, tapped: true)));
                    m.Items.Add(Item("Play Face Down", () => PlayCard(c, faceDown: true)));
                    if (c.IsAura || c.IsEquipment) m.Items.Add(Item("Attach To…", () => StartPick(c, PickKind.Attach)));
                    if (c.HasMutate) m.Items.Add(Item("Mutate Onto…", () => StartPick(c, PickKind.Mutate)));
                    if (AbilitiesMenu(c) is { } cycle) m.Items.Add(cycle);
                    m.Items.Add(new Separator());
                    m.Items.Add(Item("Discard", () => _game.Move(c, Zone.Graveyard), "G"));
                    m.Items.Add(Item("Exile", () => _game.Move(c, Zone.Exile), "E"));
                    m.Items.Add(Item("Put on Top of Library", () => _game.Move(c, Zone.Library, Place.Top)));
                    m.Items.Add(Item("Put on Bottom of Library", () => _game.Move(c, Zone.Library, Place.Bottom)));
                    break;
                case Zone.Command:
                    m.Items.Add(Item($"Cast (tax +{Me.CommanderTax(c)})", () => PlayCard(c), "Enter"));
                    m.Items.Add(Item("Put in Hand", () => _game.Move(c, Zone.Hand)));
                    break;
                case Zone.Stack:
                    m.Items.Add(Item("Resolve", () => MainAction(c), "Enter"));
                    m.Items.Add(Item("Counter (to the Graveyard)", () => _game.Move(c, Zone.Graveyard)));
                    m.Items.Add(Item("Back to Hand", () => _game.Move(c, Zone.Hand)));
                    m.Items.Add(Item("Exile", () => _game.Move(c, Zone.Exile)));
                    break;
                case Zone.Library:                                       // the revealed top card
                    ShowLibraryTopMenu(id);
                    return;
                default:                                                 // the top card of the graveyard or exile
                    m.Items.Add(Item($"Look Through the {Game.ZoneWord(zone)}…", () => OpenBrowser(zone)));
                    m.Items.Add(new Separator());
                    m.Items.Add(MoveMenu(c, zone));
                    break;
            }
            m.Items.Add(new Separator());
            m.Items.Add(Item("Zoom", () => Zoom(c), "Z"));
            m.Items.Add(Item("View Card…", () => new CardDetailWindow(c.Card, this).Show()));
            Open(m, target);
        }

        private MenuItem MoveMenu(GameCard c, Zone from)
        {
            var mi = new MenuItem { Header = "Move To" };
            if (from != Zone.Battlefield) mi.Items.Add(Item("Battlefield", () => PlayCard(c)));
            if (from != Zone.Hand) mi.Items.Add(Item("Hand", () => _game.Move(c, Zone.Hand), "H"));
            if (from != Zone.Graveyard) mi.Items.Add(Item("Graveyard", () => _game.Move(c, Zone.Graveyard), "G"));
            if (from != Zone.Exile) mi.Items.Add(Item("Exile", () => _game.Move(c, Zone.Exile), "E"));
            mi.Items.Add(Item("Top of Library", () => _game.Move(c, Zone.Library, Place.Top)));
            mi.Items.Add(Item("Bottom of Library", () => _game.Move(c, Zone.Library, Place.Bottom)));
            if (c.IsCommander && from != Zone.Command) mi.Items.Add(Item("Command Zone", () => _game.Move(c, Zone.Command)));
            return mi;
        }

        private static readonly string[] CommonCounters = { "+1/+1", "-1/-1", "Loyalty" };

        private MenuItem CountersMenu(GameCard c)
        {
            var mi = new MenuItem { Header = "Counters" };
            foreach (var kind in CommonCounters.Concat(c.Counters.Keys.Where(k => !CommonCounters.Contains(k, StringComparer.OrdinalIgnoreCase))))
            {
                int has = c.Counters.GetValueOrDefault(kind);
                mi.Items.Add(Item($"Add {kind}", () => _game.AddCounters(c, kind, 1), kind == "+1/+1" ? "+" : ""));
                if (has > 0)
                    mi.Items.Add(Item($"Remove {kind} ({has})", () => _game.AddCounters(c, kind, -1), kind == "+1/+1" ? "−" : ""));
            }
            mi.Items.Add(new Separator());
            mi.Items.Add(Item("Other Counter…", () =>
            {
                string? kind = TextPromptDialog.Ask(this, "Add a counter", "What kind of counter? (Shield, Stun, Oil, Charge…)", "");
                if (!string.IsNullOrWhiteSpace(kind)) _game.AddCounters(c, kind, 1);
            }));
            mi.Items.Add(Item("Remove All Counters", () => _game.ClearCounters(c), enabled: c.Counters.Count > 0));
            return mi;
        }

        private void LibraryPile_RightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowLibraryMenu();
        }

        /// <summary>The library's menu (the pile, or its revealed top card: then that card's moves too).</summary>
        private void ShowLibraryMenu(GameCard? top = null)
        {
            var m = new ContextMenu();
            if (top != null)
            {
                m.Items.Add(Item(top.IsModal ? "Play the Top Card… (pick a face)" : "Play the Top Card", () => PlayCard(top)));
                m.Items.Add(MoveMenu(top, Zone.Library));
                m.Items.Add(new Separator());
            }
            var reveal = new MenuItem
            {
                Header = "Play With the Top Card Revealed", IsCheckable = true, IsChecked = Me.TopRevealed,
                ToolTip = "Courser of Kruphix, Future Sight, Experimental Frenzy …: the top card shows on the library, and you can drag it out",
            };
            reveal.Click += (_, _) => _game.SetTopRevealed(Me, !Me.TopRevealed);
            m.Items.Add(reveal);
            m.Items.Add(new Separator());
            m.Items.Add(Item("Draw", () => _game.Draw(Me), "D"));
            m.Items.Add(Item("Draw X…", () => { if (AskNumber("Draw cards", "How many?") is int n) _game.Draw(Me, n); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Look at the Top X…", () => { if (AskNumber("Look at the top", "How many cards?") is int n) OpenBrowser(Zone.Library, n); }));
            m.Items.Add(Item("Search the Library…", () => OpenBrowser(Zone.Library)));
            m.Items.Add(Item("Shuffle", () => _game.Shuffle(Me), "S"));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Mill X…", () => { if (AskNumber("Mill", "How many cards to the graveyard?") is int n) _game.MoveTop(Me, n, Zone.Graveyard); }));
            m.Items.Add(Item("Exile the Top X…", () => { if (AskNumber("Exile from the top", "How many cards?") is int n) _game.MoveTop(Me, n, Zone.Exile); }));
            Open(m, LibraryPile);
        }

        private void ShowLibraryTopMenu(int id)
        {
            if (Find(id) is { } top) ShowLibraryMenu(top);
        }

        private int? AskNumber(string title, string prompt)
        {
            string? s = TextPromptDialog.Ask(this, title, prompt, "1");
            return int.TryParse(s?.Trim(), out int n) && n > 0 ? Math.Min(n, 200) : null;
        }

        // ── Piles ────────────────────────────────────────────────────────
        private void LibraryPile_Click(object sender, MouseButtonEventArgs e) => _game.Draw(Me);
        private void GraveyardPile_Click(object sender, MouseButtonEventArgs e) => OpenBrowser(Zone.Graveyard);
        private void ExilePile_Click(object sender, MouseButtonEventArgs e) => OpenBrowser(Zone.Exile);

        /// <summary>Look through a zone: yours, or (<paramref name="whose"/>) the other player's: their graveyard, exile, library.</summary>
        private void OpenBrowser(Zone zone, int topOnly = 0, Seat? whose = null)
        {
            var seat = whose ?? Me;
            var list = seat.ZoneList(zone);
            if (list.Count == 0) return;
            if (seat.Index == Me.Index)
            {
                new ZoneBrowserWindow(this, _game, Me, zone, topOnly, c => Picture(c), c => PlayCard(c), c => { _hoverId = c.Id; ShowInPanel(c); })
                {
                    AbilityMenu = AbilitiesMenu,                   // a graveyard card's "Exile ~ from your graveyard:", a hand card's cycling
                }.Show();
                return;
            }
            // Theirs: a card goes to its owner's zones (onto their side of the table).
            new ZoneBrowserWindow(this, _game, seat, zone, topOnly, c => Picture(c),
                                  c => _game.Move(c, Zone.Battlefield, Place.Top, 0.3, 0.1), c => ShowInPanel(c))
            {
                Whose = seat.Whose,
            }.Show();
        }

        // ── Buttons ──────────────────────────────────────────────────────
        /// <summary>New Game ▾: on your own, against the last deck again, or against another deck.</summary>
        private void BtnNewGame_Click(object sender, RoutedEventArgs e)
        {
            var m = new ContextMenu();
            m.Items.Add(Item("On Your Own", () => StartOver(null)));
            if (_lastOppDeck != null) m.Items.Add(Item($"Against {_lastOppDeck.Name} Again", () => StartOver(_lastOppDeck)));
            m.Items.Add(Item("Against Another Deck…", PickOpponent));
            Open(m, BtnNewGame);
        }

        /// <summary>A new game: on your own (<paramref name="against"/> null) or with that deck across the table.</summary>
        private void StartOver(BreakersOfE.Models.Deck? against)
        {
            if (_game.Turn > 1 || Me.Kept)
            {
                var r = System.Windows.MessageBox.Show(this, "Start a new game? This game ends (Undo can't bring it back).",
                    "Playtest", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (r != MessageBoxResult.OK) return;
            }
            SavedGames.Delete(_deck);
            _oppDeck = against;
            if (against != null) _lastOppDeck = against;
            NewGame();
        }

        private void BtnSampleHands_Click(object sender, RoutedEventArgs e) =>
            new SampleHandsWindow(this, _deck, _layouts).Show();

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => UndoMove();

        /// <summary>Undo (button or Ctrl+Z); while discarding down, the count follows the hand.</summary>
        private void UndoMove()
        {
            _quietTurn = true;                                  // if the table turns back, it doesn't cover
            _game.Undo();
            if (_discardDown > 0)
            {
                _discardDown = S.MaxHandSize > 0 ? Math.Max(0, Me.Hand.Count - S.MaxHandSize) : 0;
                Render();
            }
        }
        private void BtnDraw_Click(object sender, RoutedEventArgs e) => _game.Draw(Me);
        private void BtnShuffle_Click(object sender, RoutedEventArgs e) => _game.Shuffle(Me);
        private void BtnUntapAll_Click(object sender, RoutedEventArgs e) => _game.UntapAll(Me);
        private void BtnPassTurn_Click(object sender, RoutedEventArgs e) => NextTurn();
        private void BtnKeep_Click(object sender, RoutedEventArgs e)
        {
            if (_spread != null && _pick is { } p && Find(p.CardId) is { } c) { FinishSpread(c); return; }     // Done: those are all the targets
            _game.Keep(Me);
        }
        private void BtnMulligan_Click(object sender, RoutedEventArgs e) => _game.Mulligan(Me);

        private void NextTurn()
        {
            EnsureActiveView();
            if (!Me.Kept) _game.Keep(Me);                    // playing on means the hand is kept
            AfterCleanup(() =>
            {
                _game.NextTurn(Me, S.DrawOnNewTurn);
                ShowReminders();
            });
        }

        private void BtnTokens_Click(object sender, RoutedEventArgs e)
        {
            var m = new ContextMenu();
            // The deck's Tokens part first (Suggested Tokens… fills it on the deck page).
            foreach (var t in Me.Tokens.OrderBy(t => t.Name))
            {
                string pt = string.IsNullOrEmpty(t.PowerToughness) ? "" : $"  {t.PowerToughness}";
                m.Items.Add(Item(t.Name + pt, () =>
                {
                    var (x, y) = FreeSpot(LaneOf(t));
                    _game.CreateToken(Me, t, x, y, MyLayouts.GetValueOrDefault(t.ScryfallId ?? "", ""));
                }));
            }
            if (Me.Tokens.Count == 0)
                m.Items.Add(new MenuItem { Header = "(This deck's Tokens part is empty)", IsEnabled = false });
            m.Items.Add(new Separator());
            m.Items.Add(Item("Any Token…", MakeAnyToken));
            Open(m, BtnTokens);
        }

        /// <summary>Any token of the card pool, whether or not the deck lists it.</summary>
        private void MakeAnyToken()
        {
            var pick = TokenSearchWindow.Ask(this);
            if (pick == null) return;
            var (token, count) = pick.Value;
            var card = Services.DeckService.FromTokenCard(token);
            var (x, y) = FreeSpot(LaneOf(card));
            _game.CreateToken(Me, card, x, y, token.Layout ?? "", count);
        }

        private void LifeDown_Click(object sender, RoutedEventArgs e) =>
            _game.ChangeLife(Me, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -5 : -1);

        private void LifeUp_Click(object sender, RoutedEventArgs e) =>
            _game.ChangeLife(Me, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1);

        private void LifeText_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            string? s = TextPromptDialog.Ask(this, "Life", "Your life total:", Me.Life.ToString());
            if (int.TryParse(s?.Trim(), out int life)) _game.SetLife(Me, life);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            (string, string)? other = _game.Seats.Count > 1 ? (DeckIdOf(_game.Seats[1]), _game.Seats[1].DeckName) : null;
            new PlaytestSettingsWindow(this, _deck, FieldAspect, other).ShowDialog();
            ApplySettings();
            _game.FreeFirstMulligan = S.FreeFirstMulligan;
            _game.ShowReminders = S.ShowReminders;
            Render();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e) => HelpWindow.Open("playtest");

        // ── Full screen ──────────────────────────────────────────────────
        private bool _full;
        private WindowState _stateBefore;

        private void BtnFullScreen_Click(object sender, RoutedEventArgs e) => FullScreen(!_full);

        private void FullScreen(bool on)
        {
            if (on == _full) return;
            _full = on;
            WindowTitleBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on)
            {
                _stateBefore = WindowState;
                WindowState = WindowState.Normal;                   // so maximizing covers the taskbar too
                WindowStyle = WindowStyle.None;
                Topmost = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                Topmost = false;
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _stateBefore;
            }
            BtnFullScreen.Content = on ? "⛶ Leave Full Screen" : "⛶ Full Screen";
        }

        // ── Keys ─────────────────────────────────────────────────────────
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;

            // Passing the screen: only Ready (Enter or Space) until the next player is in front of it.
            if (PassCover.Visibility == Visibility.Visible)
            {
                if (e.Key is Key.Enter or Key.Space) PassReady();
                e.Handled = e.Key != Key.F11;
                return;
            }

            // A zoom closes with any key (Esc included), and that key does nothing else.
            if (ZoomLayer.Visibility == Visibility.Visible)
            {
                CloseZoom();
                e.Handled = true;
                return;
            }

            var mods = Keyboard.Modifiers;
            var card = Target();                                          // the selected card, else the one under the mouse
            Zone? where = card == null ? null : Me.ZoneOf(card);
            bool onField = where == Zone.Battlefield;
            bool topCard = where == Zone.Library && Me.TopRevealed && Me.Library.FirstOrDefault()?.Id == card?.Id;
            bool handled = true;
            switch (e.Key)
            {
                case Key.Z when mods == ModifierKeys.Control: UndoMove(); break;
                case Key.F11: FullScreen(!_full); break;
                case Key.Escape when _pick != null: CancelPick(); break;
                case Key.Escape when _selectedId != 0: Deselect(); break;
                case Key.Escape when _full: FullScreen(false); break;
                case Key.F1: HelpWindow.Open("playtest"); break;

                // The card: its main action, a closer look, the last one played.
                case Key.Enter or Key.Space when card != null && mods == ModifierKeys.None: MainAction(card); break;
                case Key.Z when mods == ModifierKeys.None && card != null: Zoom(card); break;
                case Key.L when mods == ModifierKeys.None:
                    if (Find(_lastPlayedId) is { } last) Zoom(last);
                    break;

                // The game.
                case Key.D when mods == ModifierKeys.None: _game.Draw(Me); break;
                case Key.S when mods == ModifierKeys.None: _game.Shuffle(Me); break;
                case Key.U when mods == ModifierKeys.None: _game.UntapAll(Me); break;
                case Key.N when mods == ModifierKeys.None: NextTurn(); break;
                case Key.P when mods == ModifierKeys.None: NextStep(); break;
                case Key.R when mods == ModifierKeys.None: ResolveTop(); break;
                case Key.K when mods == ModifierKeys.None && !Me.Kept: _game.Keep(Me); break;
                case Key.W when mods == ModifierKeys.None && Opp != null: SwitchSides(); break;
                case Key.M when mods == ModifierKeys.None && !Me.Kept: _game.Mulligan(Me); break;

                // Quick moves for the selected (or hovered) card.
                case Key.T when card != null && card.HasBack && onField: _game.TurnOver(card); break;
                case Key.F when card != null && card.CanFlip && onField: _game.Flip(card); break;
                case Key.G when card != null && where != Zone.Graveyard && (where != Zone.Library || topCard): _game.Move(card, Zone.Graveyard); break;
                case Key.E when card != null && where != Zone.Exile && (where != Zone.Library || topCard): _game.Move(card, Zone.Exile); break;
                case Key.H when card != null && where != Zone.Hand && (where != Zone.Library || topCard): _game.Move(card, Zone.Hand); break;
                case Key.OemPlus or Key.Add when onField: _game.AddCounters(card!, "+1/+1", 1); break;
                case Key.OemMinus or Key.Subtract when onField: _game.AddCounters(card!, "+1/+1", -1); break;
                default: handled = false; break;
            }
            if (handled) e.Handled = true;
        }
    }
}
