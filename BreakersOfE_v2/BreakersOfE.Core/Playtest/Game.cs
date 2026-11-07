using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BreakersOfE.Models;

namespace BreakersOfE.Playtest
{
    /// <summary>When unspent mana leaves the pool.</summary>
    public enum ManaEmptying
    {
        /// <summary>As each step and phase ends (the rules, 500.4).</summary>
        EveryStep,
        /// <summary>Only at the end of the turn (the cleanup).</summary>
        EndOfTurn,
        /// <summary>Never: it stays until you spend it or empty it.</summary>
        Never,
    }

    /// <summary>Where in a zone a card goes.</summary>
    public enum Place { Top, Bottom }

    /// <summary>
    /// The steps of a turn the table shows. Untap happens as a new turn
    /// starts (it has nothing to stop for); Cleanup happens as the end step is left.
    /// </summary>
    public enum Phase { Upkeep, Draw, Main1, Combat, Main2, End }

    /// <summary>
    /// A game at the playtest table: the seats, the turn, the log, and every
    /// move a player can make. Each move can be undone. No screen code: the
    /// table window draws <see cref="Seats"/> and calls these methods, and
    /// redraws on <see cref="Changed"/>.
    ///
    /// Stage 1 plays one seat (goldfish); the game already holds any number,
    /// for the opponent later and for the Raspberry Pi table.
    /// </summary>
    public sealed class Game
    {
        public const int HandSize = 7;

        public List<Seat> Seats { get; private set; } = new();
        public int Turn { get; private set; } = 1;

        /// <summary>The step of the turn now. A game starts in the first main phase (the first turn has no draw).</summary>
        public Phase Phase { get; private set; } = Phase.Main1;

        /// <summary>The steps entered by the last step / turn move, in order (for "at the beginning of …" reminders).</summary>
        public List<Phase> LastPassed { get; private set; } = new();

        private int _stackCounter;

        /// <summary>What happened, newest last ("Turn 3", "Drew Forest", "Cast Llanowar Elves").</summary>
        public List<string> Log { get; private set; } = new();

        /// <summary>Raised after every change (and after Undo).</summary>
        public event Action? Changed;

        /// <summary>Raised for each new log line.</summary>
        public event Action<string>? Logged;

        /// <summary>The first mulligan is free for players whose deck is Commander-style (each player by their own deck's format).</summary>
        public bool FreeFirstMulligan { get; set; } = true;

        private int _nextId = 1;

        // ── Undo ─────────────────────────────────────────────────────────
        private sealed record Snapshot(string What, List<Seat> Seats, int Turn, int LogCount, Phase Phase, int StackCounter, int ActiveSeat);

        // ── Whose turn ───────────────────────────────────────────────────
        /// <summary>The seat whose turn it is (0 in a game on your own). Passes round the table at each new turn.</summary>
        public int ActiveSeat { get; private set; }

        /// <summary>The player whose turn it is.</summary>
        public Seat Active => Seats[Math.Clamp(ActiveSeat, 0, Math.Max(0, Seats.Count - 1))];

        /// <summary>A card anywhere at the table, any player's (ids are never shared).</summary>
        public GameCard? FindAny(int id)
        {
            foreach (var s in Seats)
                if (s.Find(id) is { } c) return c;
            return null;
        }

        /// <summary>Everyone but this player.</summary>
        public IEnumerable<Seat> Others(Seat seat) => Seats.Where(s => s.Index != seat.Index);

        /// <summary>The other players lose life (an opponent's damage); on your own, it's logged.</summary>
        private int HurtOthers(Seat seat, int n, string why)
        {
            var others = Others(seat).ToList();
            if (others.Count == 0) { Write($"{why}: no opponent at the table (theirs to count)."); return 0; }
            foreach (var o in others) ChangeLife(o, -n);
            return n * others.Count;
        }

        /// <summary>Damage to one target: a card id on the battlefield (anyone's), -1 = you, -2 = the other players.</summary>
        private void DamageTo(Seat seat, int target, int n, string source)
        {
            if (n <= 0) return;
            if (target == -1) ChangeLife(seat, -n);
            else if (target == -2) HurtOthers(seat, n, $"{source} deals {n} damage to an opponent");
            else if (FindAny(target) is { } o && Seats[o.Seat].ZoneOf(o) == Zone.Battlefield) MarkDamage(o, n);
        }

        /// <summary>The power of the card using the ability, and of the creature sacrificed to pay for it (as they were, before paying).</summary>
        private int _selfPower, _sacPower, _sacToughness;
        private readonly List<Snapshot> _undo = new();
        private const int UndoDepth = 100;

        public bool CanUndo => _undo.Count > 0;
        public string UndoWhat => _undo.Count > 0 ? _undo[^1].What : "";

        /// <summary>Before each move: remember the table so Undo can put it back.</summary>
        /// <summary>When unspent mana empties (the table's setting): every step as the rules say, at the end of the turn, or never.</summary>
        public ManaEmptying ManaEmpties { get; set; } = ManaEmptying.EndOfTurn;

        /// <summary>Inside one move made of several (tapping for mana and sacrificing): no undo step or redraw of their own.</summary>
        private int _batch;

        private void Remember(string what)
        {
            if (_batch > 0) return;
            _undo.Add(new Snapshot(what, Seats.Select(s => s.Clone()).ToList(), Turn, Log.Count, Phase, _stackCounter, ActiveSeat));
            if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        }

        /// <summary>Take back the last move. False when there's nothing to undo.</summary>
        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var s = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            Seats = s.Seats;
            Turn = s.Turn;
            Phase = s.Phase;
            _stackCounter = s.StackCounter;
            ActiveSeat = s.ActiveSeat;
            LastPassed = new List<Phase>();
            if (Log.Count > s.LogCount) Log.RemoveRange(s.LogCount, Log.Count - s.LogCount);
            Write($"Undid: {s.What}");
            RefreshStars();
            Changed?.Invoke();
            return true;
        }

        private void Write(string line)
        {
            Log.Add(line);
            Logged?.Invoke(line);
        }

        private void Done()
        {
            if (_batch == 0)
            {
                RefreshStars();
                Changed?.Invoke();
            }
        }

        /// <summary>Every creature with a * in its power or toughness: counted again from the table as it is now (<see cref="Stars"/>).</summary>
        public void RefreshStars()
        {
            foreach (var s in Seats)
                foreach (var c in s.Battlefield)
                {
                    if (!c.HasStar) { c.StarPower = c.StarToughness = null; continue; }
                    (c.StarPower, c.StarToughness) = Stars.Count(c, s, Seats);
                }
        }

        // ── Starting ─────────────────────────────────────────────────────
        /// <summary>
        /// Sit a deck down: commanders to the command zone, the main deck
        /// shuffled into the library, the sideboard and tokens set aside, and
        /// an opening hand of 7. <paramref name="layouts"/> gives each
        /// printing's Scryfall layout by ScryfallId (from the card pool).
        /// </summary>
        public Seat AddSeat(Deck deck, string playerName, int startingLife,
                            IReadOnlyDictionary<string, string>? layouts = null,
                            IReadOnlyDictionary<string, string>? loyalties = null)
        {
            var seat = new Seat
            {
                Index = Seats.Count,
                Name = playerName,
                DeckName = deck.Name,
                DeckId = deck.DeckId ?? "",
                IsCommanderGame = deck.DeckType != DeckType.Standard && deck.DeckType != DeckType.Limited,
                Life = startingLife,
                StartingLife = startingLife,
            };
            string LayoutOf(DeckCard c) =>
                layouts != null && layouts.TryGetValue(c.ScryfallId ?? "", out var l) ? l : "";

            foreach (var line in deck.Cards)
            {
                if (line.IsTokenLine)
                {
                    seat.Tokens.Add(line);
                    continue;
                }
                bool commander = line.IsCommander || line.Category == DeckCardCategory.Commander;
                int copies = Math.Max(1, line.TotalQuantity);
                for (int i = 0; i < copies; i++)
                {
                    var gc = new GameCard
                    {
                        Id = _nextId++, Card = line, Layout = LayoutOf(line), Seat = seat.Index,
                        IsCommander = commander,
                        Loyalty = loyalties != null && loyalties.TryGetValue(line.ScryfallId ?? "", out var lo) ? lo : "",
                    };
                    if (commander) seat.Command.Add(gc);
                    else if (line.Category == DeckCardCategory.Sideboard) seat.Sideboard.Add(gc);
                    else seat.Library.Add(gc);
                }
            }
            Seats.Add(seat);
            ShuffleList(seat.Library);
            Write($"{seat.Name}: {deck.Name} ({seat.Library.Count + seat.Command.Count} cards). Shuffled.");
            DrawCore(seat, HandSize);
            Write($"{seat.Name} drew an opening hand of {seat.Hand.Count}.");
            Done();
            return seat;
        }

        // ── Library ──────────────────────────────────────────────────────
        private static void ShuffleList(List<GameCard> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);         // fair, not System.Random
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public void Shuffle(Seat seat)
        {
            Remember("Shuffle");
            ShuffleList(seat.Library);
            Write($"{seat.Name} shuffled the library.");
            Done();
        }

        private int DrawCore(Seat seat, int n)
        {
            int drawn = 0;
            for (; drawn < n && seat.Library.Count > 0; drawn++)
            {
                var c = seat.Library[0];
                seat.Library.RemoveAt(0);
                seat.Hand.Add(c);
            }
            if (drawn < n) seat.DrewFromEmpty = true;                     // drawing from an empty library loses the game
            return drawn;
        }

        /// <summary>Draw <paramref name="n"/> cards. Fewer when the library runs out (logged).</summary>
        public int Draw(Seat seat, int n = 1)
        {
            if (n <= 0) return 0;
            Remember(n == 1 ? "Draw" : $"Draw {n}");
            int got = DrawCore(seat, n);
            if (got == 1 && n == 1) Write($"{seat.Name} drew a card.");
            else if (got > 0) Write($"{seat.Name} drew {got} cards.");
            if (got < n) Write($"{seat.Whose} library is empty: couldn't draw {n - got} (that loses the game).");
            Done();
            return got;
        }

        /// <summary>Play with the top card of the library revealed, or stop.</summary>
        public void SetTopRevealed(Seat seat, bool on)
        {
            if (seat.TopRevealed == on) return;
            Remember(on ? "Top card revealed" : "Top card hidden");
            seat.TopRevealed = on;
            Write(on ? $"{seat.Name} {seat.Plays} with the top card of the library revealed."
                     : $"{seat.Name} stopped revealing the top card of the library.");
            Done();
        }

        /// <summary>The top <paramref name="n"/> cards of the library to the graveyard (mill) or exile, as one move.</summary>
        public int MoveTop(Seat seat, int n, Zone to)
        {
            if (n <= 0 || seat.Library.Count == 0 || (to != Zone.Graveyard && to != Zone.Exile)) return 0;
            Remember(to == Zone.Graveyard ? $"Mill {n}" : $"Exile the top {n}");
            int moved = 0;
            for (; moved < n && seat.Library.Count > 0; moved++)
            {
                var c = seat.Library[0];
                seat.Library.RemoveAt(0);
                seat.ZoneList(to).Insert(0, c);
            }
            Write(to == Zone.Graveyard
                ? $"{seat.Name} milled {moved} card{(moved == 1 ? "" : "s")}."
                : $"{seat.Name} exiled the top {moved} card{(moved == 1 ? "" : "s")} of the library.");
            Done();
            return moved;
        }

        // ── Mulligan (London) ────────────────────────────────────────────
        /// <summary>Hand back into the library, shuffle, draw 7 again.</summary>
        public void Mulligan(Seat seat)
        {
            if (seat.Kept) return;
            Remember("Mulligan");
            seat.Library.AddRange(seat.Hand);
            seat.Hand.Clear();
            ShuffleList(seat.Library);
            DrawCore(seat, HandSize);
            seat.Mulligans++;
            Write($"{seat.Name} took a mulligan ({seat.Mulligans}).");
            Done();
        }

        /// <summary>Cards to put on the bottom for keeping now (the first is free when <see cref="FreeFirstMulligan"/>).</summary>
        public int ToBottomOnKeep(Seat seat) =>
            Math.Max(0, seat.Mulligans - (FirstMulliganFree(seat) && seat.Mulligans > 0 ? 1 : 0));

        /// <summary>Is this player's first mulligan free? Commander-style decks only (Standard and Limited never).</summary>
        public bool FirstMulliganFree(Seat seat) => FreeFirstMulligan && seat.IsCommanderGame;

        /// <summary>Two or more players: who takes the first turn (before anyone plays). <paramref name="how"/>: "Coin flip" and the like, for the log.</summary>
        public void ChooseFirst(int seat, string how)
        {
            if (Seats.Count < 2) return;
            ActiveSeat = Math.Clamp(seat, 0, Seats.Count - 1);
            var s = Active;
            Write($"{how}: {(s.IsYou ? "you go" : s.Name + " goes")} first.");
            Done();
        }

        /// <summary>Keep the hand. Then <see cref="Seat.BottomPending"/> cards go to the bottom, one by one (<see cref="PutOnBottom"/>).</summary>
        public void Keep(Seat seat)
        {
            if (seat.Kept) return;
            Remember("Keep");
            seat.Kept = true;
            seat.BottomPending = Math.Min(ToBottomOnKeep(seat), seat.Hand.Count);
            Write(seat.BottomPending > 0
                ? $"{seat.Name} kept {seat.Hand.Count}; {seat.BottomPending} to put on the bottom."
                : $"{seat.Name} kept {seat.Hand.Count}.");
            Done();
        }

        /// <summary>After a mulligan: this card from the hand to the bottom of the library.</summary>
        public void PutOnBottom(Seat seat, GameCard card)
        {
            if (seat.BottomPending <= 0) return;
            var c = seat.Hand.FirstOrDefault(x => x.Id == card.Id);
            if (c == null) return;
            Remember("Put on the bottom");
            seat.Hand.Remove(c);
            seat.Library.Add(c);
            seat.BottomPending--;
            Write($"{seat.Name} put a card on the bottom.");
            Done();
        }

        // ── Moving cards ─────────────────────────────────────────────────
        /// <summary>
        /// Move a card to a zone. Leaving the battlefield turns it back to how
        /// it's printed (untapped, front up, no counters). A token that leaves
        /// the battlefield is gone. <paramref name="x"/>/<paramref name="y"/>:
        /// where it goes on the battlefield (0–1). <paramref name="backUp"/>:
        /// played on its back face (a modal double-faced card's other side).
        /// <paramref name="tapped"/>: it enters the battlefield tapped (straight away, or as it resolves from the stack).
        /// </summary>
        public void Move(GameCard card, Zone to, Place place = Place.Top, double x = 0, double y = 0,
                         bool backUp = false, bool faceDown = false, bool tapped = false)
        {
            var seat = Seats[card.Seat];
            var c = seat.Find(card.Id);
            if (c == null) return;
            var from = seat.ZoneOf(c);
            if (from == null) return;
            if (from == to && to != Zone.Battlefield && to != Zone.Library) return;
            if (to == Zone.Command && !c.IsCommander) return;            // only commanders live there

            Remember(MoveWhat(c, to));
            if (from == Zone.Battlefield && to != Zone.Battlefield) LeaveTogether(seat, c, to, place);
            seat.ZoneList(from.Value).Remove(c);
            if ((from == Zone.Battlefield || from == Zone.Stack) && to != Zone.Battlefield && to != Zone.Stack) c.Reset();

            if (c.IsToken && to != Zone.Battlefield)
            {
                Write($"{c.FaceName(0)} (token) left the battlefield and is gone.");
                Done();
                return;
            }

            if (to == Zone.Battlefield)
            {
                c.X = Math.Clamp(x, 0, 1);
                c.Y = Math.Clamp(y, 0, 1);
                c.PendingTarget = 0;
                c.PendingMutate = false;
                if (from != Zone.Battlefield && from != Zone.Stack)        // off the stack: the face it was cast on
                {
                    c.BackUp = backUp;
                    c.FaceDown = faceDown;
                    if (from == Zone.Command && c.IsCommander)
                        seat.CommanderCasts[c.Id] = seat.CommanderCasts.GetValueOrDefault(c.Id) + 1;
                }
                if (from != Zone.Battlefield) c.EnteredTurn = Turn;          // summoning sickness counts from here
                if (from != Zone.Battlefield && (tapped || c.PendingTapped)) c.Tapped = true;     // Play Tapped
                c.PendingTapped = false;
                // A planeswalker enters with its loyalty, a battle with its defense (as counters).
                if (from != Zone.Battlefield && !c.FaceDown && c.EntersWith is { } enters && !c.Counters.ContainsKey(enters.Kind))
                    c.Counters[enters.Kind] = enters.Count;
                seat.Battlefield.Add(c);                                  // last added draws on top
            }
            else if (to == Zone.Stack)
            {
                if (from != Zone.Stack) c.BackUp = backUp;
                if (tapped) c.PendingTapped = true;                          // enters tapped as it resolves
                c.StackOrder = ++_stackCounter;
                if (from == Zone.Command && c.IsCommander)
                    seat.CommanderCasts[c.Id] = seat.CommanderCasts.GetValueOrDefault(c.Id) + 1;
                seat.Stack.Insert(0, c);
            }
            else
            {
                var list = seat.ZoneList(to);
                bool atBottom = to == Zone.Library && place == Place.Bottom;
                if (to == Zone.Hand || atBottom) list.Add(c);
                else list.Insert(0, c);
            }
            Write(MoveLine(seat, c, from.Value, to, place) +
                  (to == Zone.Battlefield && from != Zone.Battlefield && c.Tapped ? " It entered tapped." :
                   to == Zone.Stack && c.PendingTapped ? " It enters tapped." : ""));
            Done();
        }

        private static string MoveWhat(GameCard c, Zone to) => to switch
        {
            Zone.Battlefield => $"Play {c.FaceName(0)}",
            Zone.Graveyard => $"{c.ShownName} to the graveyard",
            Zone.Exile => $"Exile {c.ShownName}",
            Zone.Hand => $"{c.ShownName} to the hand",
            Zone.Library => $"{c.ShownName} to the library",
            Zone.Command => $"{c.ShownName} to the command zone",
            Zone.Stack => $"Cast {c.FaceName(c.BackUp ? 1 : 0)}",
            _ => $"Move {c.ShownName}",
        };

        private static string MoveLine(Seat seat, GameCard c, Zone from, Zone to, Place place)
        {
            string name = c.FaceDown ? "a face-down card" : c.ShownName;
            string who = seat.Name;
            return (from, to) switch
            {
                (Zone.Battlefield, Zone.Battlefield) => $"{who} moved {name}.",
                (_, Zone.Battlefield) when c.FaceDown => $"{who} played a card face down.",
                (Zone.Command, Zone.Battlefield) => $"{who} cast {name} from the command zone (tax next time: {seat.CommanderTax(c)}).",
                (Zone.Hand, Zone.Battlefield) => $"{who} played {name}.",
                (Zone.Stack, Zone.Battlefield) => $"{name} resolved onto the battlefield.",
                (Zone.Stack, Zone.Graveyard) => $"{name} went to the graveyard from the stack.",
                (_, Zone.Stack) => $"{who} cast {name} (on the stack).",
                (_, Zone.Battlefield) => $"{who} put {name} onto the battlefield from the {ZoneWord(from)}.",
                (Zone.Library, Zone.Hand) => $"{who} put a card from the library into the hand.",
                (_, Zone.Library) => $"{who} put {(from == Zone.Hand ? "a card" : name)} on the {(place == Place.Bottom ? "bottom" : "top")} of the library.",
                _ => $"{who}: {name} from the {ZoneWord(from)} to the {ZoneWord(to)}.",
            };
        }

        public static string ZoneWord(Zone z) => z switch
        {
            Zone.Command => "command zone",
            _ => z.ToString().ToLowerInvariant(),
        };

        /// <summary>Move a card within the battlefield (dragged).</summary>
        public void Reposition(GameCard card, double x, double y)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null) return;
            // Dragging isn't worth an Undo step of its own, nor a log line.
            c.X = Math.Clamp(x, 0, 1);
            c.Y = Math.Clamp(y, 0, 1);
            var bf = Seats[card.Seat].Battlefield;
            bf.Remove(c);
            bf.Add(c);                                                   // dropped card on top
            Done();
        }

        // ── On the battlefield ───────────────────────────────────────────
        public void Tap(GameCard card)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || Seats[card.Seat].ZoneOf(c) != Zone.Battlefield) return;
            Remember(c.Tapped ? $"Untap {c.ShownName}" : $"Tap {c.ShownName}");
            c.Tapped = !c.Tapped;
            Write($"{Seats[card.Seat].Name} {(c.Tapped ? "tapped" : "untapped")} {(c.FaceDown ? "a face-down card" : c.ShownName)}.");
            Done();
        }

        /// <summary>Tap or untap several cards as one move (a pile of basic lands).</summary>
        public void SetTapped(Seat seat, IEnumerable<GameCard> cards, bool tapped)
        {
            var live = cards.Select(c => seat.Find(c.Id)).Where(c => c != null && c.Tapped != tapped).ToList();
            if (live.Count == 0) return;
            string name = live[0]!.ShownName;
            Remember($"{(tapped ? "Tap" : "Untap")} {live.Count} {name}");
            foreach (var c in live) c!.Tapped = tapped;
            Write($"{seat.Name} {(tapped ? "tapped" : "untapped")} {(live.Count == 1 ? name : $"{live.Count} × {name}")}.");
            Done();
        }

        /// <summary>Move several battlefield cards to one spot (a pile dragged), no Undo step, like any drag.</summary>
        public void RepositionMany(Seat seat, IEnumerable<GameCard> cards, double x, double y)
        {
            foreach (var card in cards)
                if (seat.Find(card.Id) is { } c)
                {
                    c.X = Math.Clamp(x, 0, 1);
                    c.Y = Math.Clamp(y, 0, 1);
                }
            Done();
        }

        public void UntapAll(Seat seat)
        {
            if (!seat.Battlefield.Any(c => c.Tapped)) return;
            Remember("Untap all");
            foreach (var c in seat.Battlefield) c.Tapped = false;
            Write($"{seat.Name} untapped everything.");
            Done();
        }

        /// <summary>Turn a double-faced card or two-sided token over (counters and tapped stay).</summary>
        public void TurnOver(GameCard card)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || !c.HasBack) return;
            Remember($"Turn over {c.ShownName}");
            string was = c.ShownName;
            c.BackUp = !c.BackUp;
            Write($"{Seats[card.Seat].Name} turned {was} over: {c.ShownName}.");
            Done();
        }

        /// <summary>A Kamigawa flip card: turn it upside down (or back).</summary>
        public void Flip(GameCard card)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null) return;
            Remember($"Flip {c.ShownName}");
            c.Flipped = !c.Flipped;
            Write($"{Seats[card.Seat].Name} flipped {c.FaceName(0)}{(c.Flipped ? "" : " back")}.");
            Done();
        }

        public void SetFaceDown(GameCard card, bool down)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || c.FaceDown == down) return;
            Remember(down ? "Turn face down" : $"Turn {c.ShownName} face up");
            c.FaceDown = down;
            Write(down ? $"{Seats[card.Seat].Name} turned a card face down."
                       : $"{Seats[card.Seat].Name} turned {c.ShownName} face up.");
            Done();
        }

        /// <summary>Add (or with a negative number, take off) counters of a kind.</summary>
        public void AddCounters(GameCard card, string kind, int n)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || n == 0 || string.IsNullOrWhiteSpace(kind)) return;
            kind = kind.Trim();
            int now = c.Counters.GetValueOrDefault(kind);
            int next = Math.Max(0, now + n);
            if (next == now) return;
            Remember($"{kind} counter on {c.ShownName}");
            if (next == 0) c.Counters.Remove(kind); else c.Counters[kind] = next;
            Write($"{Seats[card.Seat].Name}: {c.ShownName} has {(next == 0 ? "no" : next.ToString())} {kind} counter{(next == 1 ? "" : "s")}.");
            Done();
        }

        public void ClearCounters(GameCard card)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || c.Counters.Count == 0) return;
            Remember($"Remove counters from {c.ShownName}");
            c.Counters.Clear();
            Write($"{Seats[card.Seat].Name} removed every counter from {c.ShownName}.");
            Done();
        }

        /// <summary>
        /// Tokens onto the battlefield (one move): from the deck's Tokens part,
        /// any token of the card pool, or a copy of a card. Several fan out
        /// from (<paramref name="x"/>, <paramref name="y"/>).
        /// </summary>
        public GameCard CreateToken(Seat seat, DeckCard token, double x, double y, string layout = "", int count = 1, string loyalty = "")
        {
            count = Math.Clamp(count, 1, 100);
            string name = CardFaces.Sides(token.Name)[0];
            Remember(count == 1 ? $"Create {name}" : $"Create {count} {name}");
            GameCard first = null!;
            for (int i = 0; i < count; i++)
            {
                var gc = new GameCard
                {
                    Id = _nextId++, Card = token, Layout = layout, Seat = seat.Index, IsToken = true, Loyalty = loyalty,
                    X = Math.Clamp(x + i * 0.025, 0, 1), Y = Math.Clamp(y + (i % 2) * 0.02, 0, 1), EnteredTurn = Turn,
                };
                if (gc.EntersWith is { } enters) gc.Counters[enters.Kind] = enters.Count;     // a token copy of a planeswalker
                seat.Battlefield.Add(gc);
                first ??= gc;
            }
            Write(count == 1 ? $"{seat.Name} created a {name} token." : $"{seat.Name} created {count} {name} tokens.");
            Done();
            return first;
        }

        // ── Life and turns ───────────────────────────────────────────────
        public void ChangeLife(Seat seat, int delta)
        {
            if (delta == 0) return;
            Remember(delta > 0 ? $"Gain {delta} life" : $"Lose {-delta} life");
            seat.Life += delta;
            Write($"{seat.Name} {(delta > 0 ? "gained" : "lost")} {Math.Abs(delta)} life: {seat.Life}.");
            Done();
        }

        public void SetLife(Seat seat, int life)
        {
            if (life == seat.Life) return;
            Remember($"Life {life}");
            seat.Life = life;
            Write($"{seat.Whose} life is now {life}.");
            Done();
        }

        /// <summary>Next turn: untap everything and (when <paramref name="draw"/>) draw a card.</summary>
        /// <summary>Next turn, straight to its first main phase: cleanup, untap, upkeep, draw (when <paramref name="draw"/>).</summary>
        public void NextTurn(Seat seat, bool draw)
        {
            Remember($"Turn {Turn + 1}");
            LastPassed = new List<Phase>();
            int turn = Turn;
            do StepCore(seat, draw); while (!(Turn > turn && Phase == Phase.Main1));
            Done();
        }

        /// <summary>The next step of the turn (from the end step: the next turn's upkeep).</summary>
        public void NextStep(Seat seat, bool draw)
        {
            Remember(Phase == Phase.End ? $"Turn {Turn + 1}" : $"To {PhaseName(Next(Phase))}");
            LastPassed = new List<Phase>();
            StepCore(seat, draw);
            Done();
        }

        /// <summary>Forward to a step (later this turn, or the next turn's when it's behind), as one move.</summary>
        public void GoTo(Seat seat, Phase target, bool draw)
        {
            if (target == Phase) return;
            Remember($"To {PhaseName(target)}");
            LastPassed = new List<Phase>();
            do StepCore(seat, draw); while (Phase != target);
            Done();
        }

        private static Phase Next(Phase p) => p == Phase.End ? Phase.Upkeep : p + 1;

        /// <summary>One step on. Leaving the end step is the cleanup (damage wears off) and a new turn's untap.</summary>
        private void StepCore(Seat _, bool draw)
        {
            if (Phase == Phase.Combat) EndCombatCore();                    // leaving combat: nobody's attacking or blocking any more
            EmptyPools(Phase == Phase.End);
            var seat = Active;                                           // the step belongs to the player whose turn it is
            if (Phase == Phase.End)
            {
                foreach (var s in Seats)
                    foreach (var c in s.Battlefield) { c.Damage = 0; c.PowerMod = 0; c.ToughnessMod = 0; c.TempKeywords.Clear(); }
                Turn++;
                if (Seats.Count > 1) ActiveSeat = (ActiveSeat + 1) % Seats.Count;      // the turn passes round the table
                seat = Active;
                foreach (var c in seat.Battlefield) c.Tapped = false;
                Phase = Phase.Upkeep;
                Write(Seats.Count > 1 ? $"── Turn {Turn}: {seat.Whose} turn ──" : $"── Turn {Turn} ──");
                Write($"{seat.Name} untapped.");
            }
            else
            {
                Phase = Next(Phase);
                if (Phase == Phase.Upkeep)
                    foreach (var s in Seats.Where(s => s.AtNextUpkeep.Count > 0))
                    {
                        foreach (var line in s.AtNextUpkeep) Write($"Upkeep: {line}");
                        s.AtNextUpkeep.Clear();
                    }
                if (Phase == Phase.End)
                    foreach (var s in Seats.Where(s => s.AtNextEndStep.Count > 0))
                    {
                        foreach (var line in s.AtNextEndStep) Write($"End step: {line}");
                        s.AtNextEndStep.Clear();
                    }
                if (Phase == Phase.Draw && draw)
                {
                    if (DrawCore(seat, 1) == 1) Write($"{seat.Name} drew a card.");
                    else Write($"{seat.Whose} library is empty: couldn't draw (that loses the game).");
                }
            }
            LastPassed.Add(Phase);
        }

        public static string PhaseName(Phase p) => p switch
        {
            Phase.Upkeep => "Upkeep",
            Phase.Draw => "Draw",
            Phase.Main1 => "Main 1",
            Phase.Combat => "Combat",
            Phase.Main2 => "Main 2",
            _ => "End",
        };

        // ── Mana ─────────────────────────────────────────────────────────
        /// <summary>
        /// Tap cards for mana (one move, one undo): each makes the option's mana
        /// (with Mana Reflection, Mana Flare, Wild Growth … from the battlefield)
        /// plus <paramref name="chosen"/> (Utopia Sprawl's color, already picked).
        /// Its cost comes out of the pool (a Signet's {1}), its damage off your
        /// life (a Talisman), and a Treasure is sacrificed.
        /// </summary>
        public void TapForMana(Seat seat, IList<GameCard> cards, ManaOption option, string chosen = "")
        {
            var live = cards.Select(c => seat.Find(c.Id)).Where(c => c != null && !c.Tapped && seat.ZoneOf(c) == Zone.Battlefield)
                            .Select(c => c!).ToList();
            if (live.Count == 0) return;
            string name = live[0].ShownName;
            Remember($"Tap {(live.Count == 1 ? name : $"{live.Count} {name}")} for mana");
            _batch++;
            try
            {
                string made = "";
                int damage = 0;
                bool short_ = false;
                var because = new List<string>();
                foreach (var c in live)
                {
                    var extra = ManaSources.ExtraFor(c, seat, Seats);
                    foreach (var b in extra.Because) if (!because.Contains(b)) because.Add(b);
                    c.Tapped = true;
                    if (option.Cost.Length > 0 && !PayCore(seat, option.Cost)) short_ = true;
                    string one = ManaSources.Total(option, extra, chosen);
                    foreach (char ch in one) seat.Mana[ch.ToString()] = seat.Mana.GetValueOrDefault(ch.ToString()) + 1;
                    made += one;
                    damage += option.Damage;
                }
                string who = live.Count == 1 ? (live[0].FaceDown ? "a face-down card" : name) : $"{live.Count} × {name}";
                Write($"{seat.Name} tapped {who} for {ManaSources.Symbols(made)}" +
                      (because.Count > 0 ? $" ({string.Join(", ", because)})" : "") +
                      (option.Cost.Length > 0 ? (short_ ? $", paying {option.Cost} (the pool was short: pay the rest yourself)" : $", paying {option.Cost} from the pool") : "") +
                      $". Pool: {seat.ManaText}.");
                if (damage > 0)
                {
                    seat.Life -= damage;
                    Write($"{name} dealt {damage} damage to {(seat.IsYou ? "you" : seat.Name)}: {seat.Life} life.");
                }
                if (option.Sacrifice)
                    foreach (var c in live) Move(c, Zone.Graveyard);
            }
            finally
            {
                _batch--;
            }
            Done();
        }

        /// <summary>Take a cost ("{1}", "{W/U}", "{2}{G}") out of the pool: colors first, then generic from colorless and the biggest piles. False when the pool was short.</summary>
        private static bool PayCore(Seat seat, string cost)
        {
            bool Take(string m)
            {
                if (seat.Mana.GetValueOrDefault(m) <= 0) return false;
                if (--seat.Mana[m] == 0) seat.Mana.Remove(m);
                return true;
            }
            bool ok = true;
            int generic = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cost, @"\{([^}]+)\}"))
            {
                string s = m.Groups[1].Value.ToUpperInvariant();
                if (int.TryParse(s, out int n)) generic += n;
                else if (s.Contains('/')) ok &= s.Split('/').Any(Take) || (s.Split('/').Any(h => int.TryParse(h, out _)) && (generic += 2) > 0);
                else if (s.Length == 1 && "WUBRGC".Contains(s)) ok &= Take(s);
            }
            for (int i = 0; i < generic; i++)
            {
                string? from = seat.Mana.GetValueOrDefault("C") > 0 ? "C"
                    : seat.Mana.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                if (from == null) { ok = false; break; }
                Take(from);
            }
            return ok;
        }

        /// <summary>Add (or with a negative number, spend) mana of one kind by hand.</summary>
        public void ChangeMana(Seat seat, string kind, int delta)
        {
            int now = seat.Mana.GetValueOrDefault(kind);
            int next = Math.Max(0, now + delta);
            if (next == now) return;
            Remember(delta > 0 ? $"Add {{{kind}}}" : $"Spend {{{kind}}}");
            if (next == 0) seat.Mana.Remove(kind); else seat.Mana[kind] = next;
            Write($"{seat.Name} {(delta > 0 ? "added" : "spent")} {ManaSources.Symbols(new string(kind[0], Math.Abs(next - now)))}. Pool: {seat.ManaText}.");
            Done();
        }

        /// <summary>Empty the pool now (spent on something, or let go).</summary>
        public void EmptyMana(Seat seat)
        {
            if (seat.ManaTotal == 0) return;
            Remember("Empty the mana pool");
            seat.Mana.Clear();
            Write($"{seat.Name} emptied {seat.Whose.ToLowerInvariant()} mana pool.");
            Done();
        }

        /// <summary>Keep this turn's mana as steps end (until the cleanup).</summary>
        public void SetKeepMana(Seat seat, bool keep)
        {
            if (seat.KeepManaThisTurn == keep) return;
            Remember(keep ? "Keep mana this turn" : "Let mana empty");
            seat.KeepManaThisTurn = keep;
            Write(keep ? $"{seat.Whose} mana stays until end of turn." : $"{seat.Whose} mana empties as steps end again.");
            Done();
        }

        /// <summary>
        /// As a step ends, unspent mana empties: except what Omnath, Upwelling and
        /// the like keep, or what Kruphix turns colorless, or all of it this turn
        /// when you've said so (until the cleanup).
        /// </summary>
        private void EmptyPools(bool cleanup)
        {
            foreach (var s in Seats)
            {
                bool keepAll = s.KeepManaThisTurn && !cleanup;
                if (cleanup) s.KeepManaThisTurn = false;
                if (ManaEmpties == ManaEmptying.Never || (ManaEmpties == ManaEmptying.EndOfTurn && !cleanup)) continue;
                if (s.ManaTotal == 0 || keepAll) continue;
                var k = ManaSources.KeepingFor(s, Seats);
                string lost = "", greyed = "";
                foreach (var m in ManaSources.Colors)
                {
                    int n = s.Mana.GetValueOrDefault(m);
                    if (n == 0 || k.Keep.Contains(m)) continue;
                    s.Mana.Remove(m);
                    if (k.ToColorless) { s.Mana["C"] = s.Mana.GetValueOrDefault("C") + n; greyed += new string(m[0], n); }
                    else lost += new string(m[0], n);
                }
                string why = k.Because.Count > 0 ? $" ({string.Join(", ", k.Because)})" : "";
                if (lost.Length > 0) Write($"{s.Whose} unspent {ManaSources.Symbols(lost)} emptied" + (s.ManaTotal > 0 ? $"; kept {s.ManaText}{why}." : "."));
                else if (greyed.Length == 0 && s.ManaTotal > 0) Write($"{s.Whose} mana stays: {s.ManaText}{why}.");
                if (greyed.Length > 0) Write($"{s.Whose} unspent {ManaSources.Symbols(greyed)} became colorless{why}. Pool: {s.ManaText}.");
            }
        }

        // ── Abilities ────────────────────────────────────────────────────
        /// <summary>What an activated ability leaves for the table to do (look at cards, search, discard, make a token it couldn't match, add mana).</summary>
        public sealed record AbilityFollowUp(EffectKind Kind, int N, string Text, TokenSpec? Token = null, int Seat = -1, IReadOnlyList<int>? Choices = null);

        /// <summary>
        /// Activate an ability (one move, one undo): pay its costs (mana from the
        /// pool, {T}, life, loyalty, counters, sacrificing it), then do each
        /// effect the table reads, on <paramref name="target"/> when it needs one
        /// (a card id; -1 = you, -2 = an opponent). <paramref name="x"/> is X.
        /// <paramref name="tokenFor"/> finds the token an effect makes (its card,
        /// layout and spot). What's left for you (scry, search …) comes back.
        /// </summary>
        public List<AbilityFollowUp> Activate(Seat seat, GameCard card, Ability ability, int target = 0, int x = 0,
                                              Func<TokenSpec, (DeckCard Card, string Layout, double X, double Y)?>? tokenFor = null,
                                              IReadOnlyList<int>? paid = null,
                                              Func<GameCard, (double X, double Y)>? spotFor = null,
                                              IReadOnlyList<(int Target, int Amount)>? spread = null, int target2 = 0)
        {
            var left = new List<AbilityFollowUp>();
            var c = seat.Find(card.Id);
            if (c == null) return left;
            string name = c.ShownName;
            bool spell = ability.Kind == AbilityKind.Spell;
            _selfPower = c.PowerNow ?? 0;
            _sacPower = _sacToughness = 0;
            Remember(spell ? $"Resolve {name}" : $"{name}: {ability.Label}");
            _batch++;
            try
            {
                Write(spell ? $"{name} resolves: {ability.Text.Replace("~", name)}" + (ability.HasX ? $" (X = {x})" : "")
                            : $"{seat.Name} activated {name}: {ability.Text.Replace("~", name)}" + (ability.HasX ? $" (X = {x})" : ""));
                // ── Costs ──
                if (ability.ManaCost.Length > 0)
                {
                    string cost = ability.ManaCost.Replace("{X}", x > 0 ? "{" + x + "}" : "");
                    // "This ability costs {1} less to activate for each legendary creature you control": counted now.
                    var less = Regex.Match(ability.Timing, @"costs \{(\d+)\} less to activate for each (.+?)\.", RegexOptions.IgnoreCase);
                    if (less.Success)
                    {
                        if (Stars.Phrase(less.Groups[2].Value, seat, Seats) is int times)
                        {
                            int off = int.Parse(less.Groups[1].Value) * times;
                            var generic = Regex.Match(cost, @"\{(\d+)\}");
                            if (generic.Success && off > 0)
                            {
                                int left0 = Math.Max(0, int.Parse(generic.Groups[1].Value) - off);
                                cost = cost.Remove(generic.Index, generic.Length).Insert(generic.Index, left0 > 0 ? "{" + left0 + "}" : "");
                                Write($"It costs {Math.Min(off, int.Parse(generic.Groups[1].Value))} less ({times} × {less.Groups[2].Value}).");
                            }
                        }
                        else
                        {
                            Write($"It costs less for each {less.Groups[2].Value}: the table can't count that, so pay it from the pool yourself.");
                            cost = "";
                        }
                    }
                    if (cost.Length > 0 && !PayCore(seat, cost)) Write($"The pool was short of {cost}: pay the rest yourself.");
                    else if (cost.Length > 0) Write($"Paid {cost} from the pool. Pool: {seat.ManaText}.");
                }
                if (ability.Tap)
                {
                    if (c.Tapped) Write($"{name} was already tapped: {{T}} can't be paid (Undo takes it back).");
                    c.Tapped = true;
                }
                if (ability.Untap) c.Tapped = false;
                if (ability.Life > 0) { seat.Life -= ability.Life; Write($"{seat.Name} paid {ability.Life} life: {seat.Life}."); }
                if (ability.LifeX && x > 0) { seat.Life -= x; Write($"{seat.Name} paid {x} life: {seat.Life}."); }
                if (ability.HalfLife) { int half = (seat.Life + 1) / 2; seat.Life -= half; Write($"{seat.Name} paid half {(seat.IsYou ? "your" : "their")} life ({half}): {seat.Life}."); }
                if (ability.Exert) Write($"{name} is exerted: it won't untap during {seat.Whose.ToLowerInvariant()} next untap step.");
                if (ability.PutCount > 0) AddCounters(c, ability.PutKind, ability.PutCount);
                if (ability.MillCost > 0) MoveTop(seat, ability.MillCost, Zone.Graveyard);
                if (ability.ExileTopCost > 0) MoveTop(seat, ability.ExileTopCost, Zone.Exile);
                if (ability.TapEnchanted && seat.Find(c.AttachedTo) is { } host) { host.Tapped = true; Write($"Tapped {host.ShownName}."); }
                if (ability.Kind == AbilityKind.Loyalty)
                {
                    if (c.LoyaltyUsedTurn == Turn) Write($"{name} already used a loyalty ability this turn (one per turn).");
                    c.LoyaltyUsedTurn = Turn;
                    int change = ability.LoyaltyX ? ability.Loyalty * x : ability.Loyalty;
                    int now = c.Counters.GetValueOrDefault("Loyalty");
                    if (now + change < 0) Write($"{name} has only {now} loyalty: it can't pay {change}.");
                    AddCounters(c, "Loyalty", change);
                }
                if (ability.RemoveCount > 0)
                {
                    // "Remove a counter from ~": whichever kind it has the most of; "Remove all … counters": every one.
                    string kind = ability.RemoveKind == "*" ? c.Counters.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "" : ability.RemoveKind;
                    if (kind.Length > 0) AddCounters(c, kind, -Math.Min(ability.RemoveCount, Math.Max(1, c.Counters.GetValueOrDefault(kind))));
                    else Write($"{name} has no counters to remove.");
                }
                if (ability.Energy > 0)
                {
                    if (seat.Energy < ability.Energy) Write($"{seat.Name} {seat.Has} only {seat.Energy} energy: {ability.Energy} needed.");
                    seat.Energy = Math.Max(0, seat.Energy - ability.Energy);
                    Write($"Paid {ability.Energy} energy: {seat.Energy} left.");
                }
                // The cards chosen to pay with, in the order of its choices.
                int next = 0;
                foreach (var choice in ability.Choices)
                {
                    var chosen = new List<GameCard>();
                    if (choice.Random || choice.Count >= 999)
                    {
                        var hand = seat.Hand.Where(h => h.Id != c.Id && Abilities.Fits(h, choice.Filter)).ToList();
                        int take = Math.Min(choice.Count, hand.Count);
                        for (int i = 0; i < take; i++)
                        {
                            var pick = hand[RandomNumberGenerator.GetInt32(hand.Count)];
                            hand.Remove(pick);
                            chosen.Add(pick);
                        }
                    }
                    else
                        for (int i = 0; i < choice.Count && paid != null && next < paid.Count; i++)
                            if (seat.Find(paid[next++]) is { } p) chosen.Add(p);
                    if (chosen.Count < Math.Min(choice.Count, 999) && !choice.Random && choice.Count < 999)
                        Write($"Cost for you to pay: {choice.Text.Replace("~", name)} ({chosen.Count} of {choice.Count} chosen).");
                    foreach (var p in chosen)
                        switch (choice.Kind)
                        {
                            case ChoiceKind.Sacrifice:
                                _sacPower = p.PowerNow ?? 0;
                                _sacToughness = p.ToughnessNow ?? 0;
                                Move(p, Zone.Graveyard);
                                break;
                            case ChoiceKind.Tap: p.Tapped = true; Write($"Tapped {p.ShownName}."); break;
                            case ChoiceKind.ExileFromGraveyard: Move(p, Zone.Exile); break;
                            case ChoiceKind.Discard: Move(p, Zone.Graveyard); break;
                            case ChoiceKind.ReturnToHand: Move(p, Zone.Hand); break;
                            case ChoiceKind.ExileFromBattlefield: Move(p, Zone.Exile); break;
                            case ChoiceKind.RemoveCounter:
                            {
                                string kind = choice.Counter.Length > 0 ? choice.Counter
                                            : p.Counters.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
                                if (kind.Length > 0) AddCounters(p, kind, -1);
                                break;
                            }
                            case ChoiceKind.PutCounter: AddCounters(p, choice.Counter, Math.Max(1, choice.Amount)); break;
                        }
                }
                foreach (var other in ability.OtherCosts) Write($"Cost for you to pay: {other.Replace("~", name)}.");
                if (ability.Kind == AbilityKind.Cycling || ability.DiscardSelf) Move(c, Zone.Graveyard);
                if (ability.ExileSelf) Move(c, Zone.Exile);
                if (ability.ReturnSelfToHand) Move(c, Zone.Hand);
                if (ability.SacrificeSelf) Move(c, Zone.Graveyard);
                if (spell && seat.ZoneOf(c) == Zone.Stack) Move(c, Zone.Graveyard);       // a resolved instant or sorcery

                DoEffects(seat, c, name, ability, target, x, tokenFor, spotFor, left, spread, target2);
            }
            finally
            {
                _batch--;
            }
            Done();
            return left;
        }

        /// <summary>An ability's or spell's effects, in order (inside <see cref="Activate"/>'s one move).</summary>
        private void DoEffects(Seat seat, GameCard c, string name, Ability ability, int target, int x,
                               Func<TokenSpec, (DeckCard Card, string Layout, double X, double Y)?>? tokenFor,
                               Func<GameCard, (double X, double Y)>? spotFor, List<AbilityFollowUp> left,
                               IReadOnlyList<(int Target, int Amount)>? spread = null, int target2 = 0)
        {
            // ── Effects ──
            GameCard? picked = target > 0 ? FindAny(target) : null;                 // yours or an opponent's
            bool? won = null;                                         // the last coin flip
            int lastLost = 0;                                         // life the other players just lost
            int lastDestroyed = 0, lastDealt = 0;                     // what the effect before destroyed, and the damage it dealt
            foreach (var e in ability.Effects)
            {
                if (e.Cond == "yourturn") { if (ActiveSeat != seat.Index) { Write($"Not {seat.Whose.ToLowerInvariant()} turn: {e.Text.Replace("~", name)} doesn't happen."); continue; } }
                else if (e.Cond.Length > 0 && won is bool w && (e.Cond == "win") != w) continue;   // the other half of the flip
                int n = e.Amount(x);
                if (e.Per.Length > 0)
                {
                    // "… for each Elf you control": counted now. "For each creature destroyed this way": what the effect before destroyed.
                    if (System.Text.RegularExpressions.Regex.IsMatch(e.Per, @"destroyed this way$")) n *= lastDestroyed;
                    else if (Stars.Phrase(e.Per, seat, Seats) is int times) n *= times;
                    else { Write($"For you to do: {e.Text.Replace("~", name)} (the table can't count \"{e.Per}\")."); continue; }
                }
                GameCard? on = e.On == EffectOn.Self ? seat.Find(c.Id)
                             : e.On is EffectOn.Target or EffectOn.AnyTarget ? picked
                             : e.On == EffectOn.Enchanted && seat.Find(c.Id) is { AttachedTo: > 0 } self ? seat.Find(self.AttachedTo)
                             : null;
                IEnumerable<GameCard> each = e.What.StartsWith("multi:") && spread != null
                                               ? spread.Select(sp => FindAny(sp.Target)).Where(o => o != null && Seats[o.Seat].ZoneOf(o) == Zone.Battlefield).Select(o => o!).ToList()
                                           : e.On == EffectOn.EachYours ? seat.Battlefield.Where(o => o.IsCreatureNow && o.MergedInto == 0).ToList()
                                           : e.On == EffectOn.EachCreature ? Seats.SelectMany(s => s.Battlefield.Where(o => o.IsCreatureNow && o.MergedInto == 0)).ToList()
                                           : on != null && Seats[on.Seat].ZoneOf(on) == Zone.Battlefield ? new[] { on } : Array.Empty<GameCard>();
                switch (e.Kind)
                {
                    case EffectKind.Draw when e.What.StartsWith("sacrificed"):
                        Draw(seat, e.What.EndsWith("power") ? _sacPower : _sacToughness);
                        break;
                    case EffectKind.Draw when e.What == "greatest power":
                        Draw(seat, seat.Battlefield.Where(o => o.IsCreatureNow && o.MergedInto == 0).Select(o => o.PowerNow ?? 0).DefaultIfEmpty(0).Max());
                        break;
                    case EffectKind.Draw: Draw(seat, n); break;
                    case EffectKind.TapAll:
                    {
                        var tapParts = e.What.Split('|');
                        foreach (var ts in Seats.Where(s => (!tapParts.Contains("yours") || s == seat) && (!tapParts.Contains("theirs") || s != seat)).ToList())
                            SetTapped(ts, ts.Battlefield.Where(o => !o.Tapped && o.MergedInto == 0 && Abilities.Fits(o, tapParts[0])).ToList(), true);
                        break;
                    }
                    case EffectKind.ShuffleGraveyard:
                        foreach (var gc in seat.Graveyard.ToList()) Move(gc, Zone.Library);
                        Shuffle(seat);
                        break;
                    case EffectKind.DoubleCounters:
                        foreach (var o in each) AddCounters(o, e.What, o.Counters.GetValueOrDefault(e.What));
                        break;
                    case EffectKind.PutBack:
                        left.Add(new AbilityFollowUp(EffectKind.PutBack, n, e.Text));
                        break;
                    case EffectKind.GainLife when e.What == "per creature":
                        ChangeLife(seat, n * seat.Battlefield.Count(o => o.IsCreatureNow && o.MergedInto == 0));
                        break;
                    case EffectKind.GainLife when e.What.StartsWith("sacrificed"):
                        ChangeLife(seat, e.What.EndsWith("power") ? _sacPower : _sacToughness);
                        break;
                    case EffectKind.GainLife when e.What == "dealt":
                        if (lastDealt > 0) ChangeLife(seat, lastDealt);
                        break;
                    case EffectKind.GainLife when e.What == "lost":
                        if (lastLost > 0) ChangeLife(seat, lastLost); else Write("No life was lost this way: nothing to gain.");
                        break;
                    case EffectKind.GainLife: ChangeLife(seat, n); break;
                    case EffectKind.LoseLife: ChangeLife(seat, -n); break;
                    case EffectKind.OpponentsLoseLife:
                        lastLost = HurtOthers(seat, n, $"Each opponent loses {n} life");
                        if (e.What.Length > 0 && Abilities.Number(e.What) is int gain) ChangeLife(seat, gain < 0 ? x : gain);
                        break;
                    case EffectKind.Counters: foreach (var o in each) AddCounters(o, e.What, n); break;
                    case EffectKind.Pump:
                        foreach (var o in each)
                        {
                            if (e.Other && o.Id == c.Id) continue;                  // "Other creatures you control"
                            if (e.What == "|attacking" && !o.Attacking) continue;  // "Attacking creatures get +1/+0"
                            Pump(o, e.P, e.T);
                            foreach (var k in Keywords.ListOf(e.What)) if (!o.TempKeywords.Contains(k)) o.TempKeywords.Add(k);
                        }
                        break;
                    case EffectKind.GainKeywords:
                    {
                        // "Creatures without flying can't block this turn": What is "keywords|-flying".
                        var kwParts = e.What.Split('|');
                        foreach (var o in each)
                        {
                            if (kwParts.Length > 1)
                            {
                                bool flies = Keywords.For(o, Seats[o.Seat], Seats).Contains("Flying");
                                if (kwParts[1] == "-flying" && flies || kwParts[1] == "flying" && !flies) continue;
                            }
                            foreach (var k in kwParts[0].Split(", ")) if (!o.TempKeywords.Contains(k)) o.TempKeywords.Add(k);
                            Write($"{o.ShownName} gains {kwParts[0].ToLowerInvariant()} until end of turn.");
                        }
                        break;
                    }
                    case EffectKind.Mill:
                    {
                        // "Target player mills": you (-1) or the player across the table; "Each player / each opponent mills".
                        var millSeats = e.What == "each" ? Seats.ToList()
                                      : e.What == "others" || e.On == EffectOn.Player && target != -1 ? Others(seat).ToList()
                                      : new List<Seat> { seat };
                        if (millSeats.Count == 0) { Write($"{e.Text.Replace("~", name)}: no opponent at the table."); break; }
                        foreach (var millSeat in millSeats) MoveTop(millSeat, n, Zone.Graveyard);
                        break;
                    }
                    case EffectKind.Token:
                        if (tokenFor?.Invoke(e.Token!) is { } t)
                        {
                            var made = CreateToken(seat, t.Card, t.X, t.Y, t.Layout, Math.Max(1, n));
                            if (e.Token!.Tapped)
                                foreach (var o in seat.Battlefield.Where(o => o.IsToken && o.Card == t.Card && !o.Tapped).TakeLast(Math.Max(1, n))) o.Tapped = true;
                        }
                        else left.Add(new AbilityFollowUp(EffectKind.Token, Math.Max(1, n), e.Text, e.Token));
                        break;
                    case EffectKind.Damage when e.On == EffectOn.EachCreature:
                        foreach (var s in Seats)
                            foreach (var o in s.Battlefield.Where(o => o.MergedInto == 0 && (o.IsCreatureNow ||
                                         e.What.Contains("+walkers") && o.FaceType(o.BackUp ? 1 : 0).Contains("Planeswalker", StringComparison.OrdinalIgnoreCase))).ToList())
                            {
                                bool flies = Keywords.For(o, s, Seats).Contains("Flying");
                                string which = e.What.Replace("+players", "").Replace("+walkers", "");
                                if (which == "flying" && !flies || which == "-flying" && flies) continue;
                                MarkDamage(o, n);
                            }
                        if (e.What.EndsWith("+players"))
                        {
                            ChangeLife(seat, -n);
                            HurtOthers(seat, n, $"Each other player takes {n} damage");
                        }
                        break;
                    case EffectKind.Damage when e.On == EffectOn.Everything:
                        foreach (var s in Seats)
                            foreach (var o in s.Battlefield.Where(o => o.IsCreatureNow && o.MergedInto == 0).ToList()) MarkDamage(o, n);
                        ChangeLife(seat, -n);
                        HurtOthers(seat, n, $"Each other player takes {n} damage");
                        break;
                    case EffectKind.Damage when e.What.StartsWith("divided:") || e.What.StartsWith("each:"):
                        if (spread == null || spread.Count == 0) { Write($"For you to do: {e.Text.Replace("~", name)}."); break; }
                        foreach (var (dt, da) in spread) DamageTo(seat, dt, e.What.StartsWith("each:") ? n : da, name);
                        break;
                    case EffectKind.Damage when e.What is "power" or "sacrificed":
                    {
                        int dmg = e.What == "power" ? _selfPower : _sacPower;
                        int whom = e.On == EffectOn.You ? -1 : e.On == EffectOn.Opponents ? -2 : target;
                        if (whom == 0) Write($"{name} deals {dmg} damage: yours to mark."); else DamageTo(seat, whom, dmg, name);
                        break;
                    }
                    case EffectKind.Damage when e.What == "own":
                        if (on != null && Seats[on.Seat].ZoneOf(on) == Zone.Battlefield) MarkDamage(on, on.PowerNow ?? 0);
                        break;
                    case EffectKind.Damage when e.What == "back":
                        if (picked != null && Seats[picked.Seat].ZoneOf(picked) == Zone.Battlefield && seat.Find(c.Id) is { } hitBack && seat.ZoneOf(hitBack) == Zone.Battlefield)
                            MarkDamage(hitBack, picked.PowerNow ?? 0);
                        break;
                    case EffectKind.Fight:
                    {
                        var fA = picked;
                        var fB = target2 > 0 ? FindAny(target2) : null;
                        if (fA == null || fB == null || Seats[fA.Seat].ZoneOf(fA) != Zone.Battlefield || Seats[fB.Seat].ZoneOf(fB) != Zone.Battlefield)
                        {
                            Write($"For you to do: {e.Text.Replace("~", name)} (one of them isn't on the battlefield).");
                            break;
                        }
                        int pA = fA.PowerNow ?? 0, pB = fB.PowerNow ?? 0;
                        Write(e.What == "fight" ? $"{fA.ShownName} ({pA}) and {fB.ShownName} ({pB}) fight."
                                                : $"{fA.ShownName} deals {pA} damage to {fB.ShownName}.");
                        MarkDamage(fB, pA);
                        if (e.What == "fight") MarkDamage(fA, pB);
                        break;
                    }
                    case EffectKind.DrawThatMany:
                        left.Add(new AbilityFollowUp(EffectKind.DrawThatMany, n, e.Text));
                        break;
                    case EffectKind.DiscardRandom:
                    {
                        int drop = Math.Min(Math.Max(1, n), seat.Hand.Count);
                        for (int i = 0; i < drop; i++)
                        {
                            var gone = seat.Hand[RandomNumberGenerator.GetInt32(seat.Hand.Count)];
                            Move(gone, Zone.Graveyard);
                        }
                        if (drop == 0) Write("No card in hand to discard.");
                        break;
                    }
                    case EffectKind.Discover: DiscoverCore(seat, n); break;
                    case EffectKind.Amass:
                    {
                        var army = seat.Battlefield.FirstOrDefault(o => o.MergedInto == 0 && o.FaceType(o.BackUp ? 1 : 0).Contains("Army", StringComparison.OrdinalIgnoreCase));
                        if (army == null && tokenFor?.Invoke(new TokenSpec("0", "0", "black", $"{e.What} Army creature", false, "")) is { } armyToken)
                            army = CreateToken(seat, armyToken.Card, armyToken.X, armyToken.Y, armyToken.Layout);
                        if (army != null) AddCounters(army, "+1/+1", n);
                        else Write($"For you to do: {e.Text} (make a 0/0 black {e.What} Army token: Tokens ▾ → Any Token…, then put {n} +1/+1 counters on it).");
                        break;
                    }
                    case EffectKind.Incubate:
                    {
                        if (tokenFor?.Invoke(new TokenSpec("", "", "", "Incubator artifact", false, "")) is { } incToken)
                        {
                            var incubator = CreateToken(seat, incToken.Card, incToken.X, incToken.Y, incToken.Layout);
                            AddCounters(incubator, "+1/+1", n);
                        }
                        else Write($"For you to do: {e.Text} (an Incubator artifact token with {n} +1/+1 counters: Tokens ▾ → Any Token…).");
                        break;
                    }
                    case EffectKind.Earthbend:
                        if (on != null && Seats[on.Seat].ZoneOf(on) == Zone.Battlefield)
                        {
                            AddCounters(on, "+1/+1", n);
                            if (!on.TempKeywords.Contains("Haste")) on.TempKeywords.Add("Haste");
                            Write($"{on.ShownName} is now a 0/0 creature with haste that's still a land, with {n} +1/+1 counters.");
                        }
                        break;
                    case EffectKind.OpponentSacrifices:
                    {
                        var sacParts = e.What.Split('|');
                        string sacFilter = sacParts[0];
                        bool sacGreatest = sacParts.Contains("greatest");
                        var sacSeats = sacParts.Contains("all") ? Seats.ToList()
                                     : e.On == EffectOn.Player && target == -1 ? new List<Seat> { seat }
                                     : Others(seat).ToList();
                        if (sacSeats.Count == 0) { Write($"{e.Text.Replace("~", name)}: no opponent at the table."); break; }
                        foreach (var sacSeat in sacSeats)
                        {
                            var sacCan = sacSeat.Battlefield.Where(o => o.MergedInto == 0 && Abilities.Fits(o, sacFilter)).ToList();
                            // A power the table couldn't count (a * it doesn't read): always one of the choices, the player decides.
                            var sacUnknown = sacCan.Where(o => o.PowerNow == null).ToList();
                            if (sacGreatest && sacCan.Count > sacUnknown.Count)
                            {
                                int most = sacCan.Where(o => o.PowerNow != null).Max(o => o.PowerNow!.Value);
                                sacCan = sacCan.Where(o => o.PowerNow == most || o.PowerNow == null).ToList();
                            }
                            int sacNeed = Math.Max(1, n);
                            if (sacCan.Count == 0) Write($"{sacSeat.Name} {sacSeat.Has} no {sacFilter} to sacrifice.");
                            else if (sacCan.Count <= sacNeed && (sacUnknown.Count == 0 || !sacGreatest)) foreach (var o in sacCan) Move(o, Zone.Graveyard);
                            else left.Add(new AbilityFollowUp(EffectKind.OpponentSacrifices, sacNeed, e.Text.Replace("~", name), Seat: sacSeat.Index, Choices: sacCan.Select(o => o.Id).ToList()));
                        }
                        break;
                    }
                    case EffectKind.ExileGraveyard:
                    {
                        var whose = target == -1 ? new List<Seat> { seat } : Others(seat).ToList();
                        if (whose.Count == 0) { Write($"{e.Text.Replace("~", name)}: no opponent at the table."); break; }
                        foreach (var ws in whose)
                            foreach (var gc in ws.Graveyard.ToList()) Move(gc, Zone.Exile);
                        break;
                    }
                    case EffectKind.Damage:
                        lastDealt = n;
                        if (target == -1 || e.On == EffectOn.You) ChangeLife(seat, -n);
                        else if (on != null && Seats[on.Seat].ZoneOf(on) == Zone.Battlefield) MarkDamage(on, n);
                        else if (target == -2 || e.On == EffectOn.Opponents) HurtOthers(seat, n, $"{name} deals {n} damage to an opponent");
                        else Write($"{name} deals {n} damage to the target: yours to mark.");
                        break;
                    case EffectKind.Tap when e.What == "toggle":
                        foreach (var o in each.ToList()) SetTapped(Seats[o.Seat], new[] { o }, !o.Tapped);
                        break;
                    case EffectKind.Tap: foreach (var g in each.GroupBy(o => o.Seat).ToList()) SetTapped(Seats[g.Key], g, true); break;
                    case EffectKind.Bolster:
                    {
                        var least = seat.Battlefield.Where(o => o.IsCreatureNow && o.MergedInto == 0 && o.ToughnessNow != null)
                                                    .OrderBy(o => o.ToughnessNow).FirstOrDefault();
                        if (least != null) AddCounters(least, "+1/+1", n);
                        else Write("Bolster: no creature to put the counters on.");
                        break;
                    }
                    case EffectKind.Manifest:
                        if (seat.Library.FirstOrDefault() is { } top)
                        {
                            var at = spotFor?.Invoke(top) ?? (0.3, 0.1);
                            Move(top, Zone.Battlefield, Place.Top, at.X, at.Y, faceDown: true);
                            Write("Manifested: a face-down 2/2 creature (turn it face up for its mana cost if it's a creature card).");
                        }
                        break;
                    case EffectKind.SetBase:
                        foreach (var o in each.ToList())
                        {
                            int bp = (o.PowerNow ?? 0) - o.PowerMod, bt = (o.ToughnessNow ?? 0) - o.ToughnessMod;      // with its counters
                            int counters = o.Counters.GetValueOrDefault("+1/+1") - o.Counters.GetValueOrDefault("-1/-1");
                            Pump(o, e.P + counters - bp - o.PowerMod, e.T + counters - bt - o.ToughnessMod);
                        }
                        break;
                    case EffectKind.ShuffleIn:
                        if (seat.Find(c.Id) is { } self2 && seat.ZoneOf(self2) != Zone.Library) { Move(self2, Zone.Library); Shuffle(seat); }
                        break;
                    case EffectKind.ExileGraveyards:
                        foreach (var s in Seats)
                            foreach (var dead in s.Graveyard.ToList()) Move(dead, Zone.Exile);
                        break;
                    case EffectKind.Untap: foreach (var g in each.GroupBy(o => o.Seat).ToList()) SetTapped(Seats[g.Key], g, false); break;
                    case EffectKind.ToHand: foreach (var o in each.ToList()) Move(o, Zone.Hand); break;
                    case EffectKind.Sacrifice:
                    case EffectKind.Destroy: foreach (var o in each.ToList()) Move(o, Zone.Graveyard); break;
                    case EffectKind.Exile: foreach (var o in each.ToList()) Move(o, Zone.Exile); break;
                    case EffectKind.Transform: if (on != null) TurnOver(on); break;
                    case EffectKind.Attach: if (on != null && seat.Find(c.Id) is { } eq) Attach(eq, on); break;
                    case EffectKind.Shuffle: Shuffle(seat); break;
                    case EffectKind.DestroyAll:
                    case EffectKind.ExileAll:
                    case EffectKind.BounceAll:
                    {
                        var allParts = e.What.Split('|');
                        string filter = allParts[0];
                        bool yours = allParts.Contains("yours");
                        // "with power 2 or less": a creature whose power the table can't count is left for you.
                        bool Sized(GameCard o)
                        {
                            foreach (var part in allParts.Skip(1))
                            {
                                var lim = Regex.Match(part, @"^(power|toughness)(<=|>=)(\d+)$");
                                if (!lim.Success) continue;
                                int? v = lim.Groups[1].Value == "power" ? o.PowerNow : o.ToughnessNow;
                                int k = int.Parse(lim.Groups[3].Value);
                                if (v == null || (lim.Groups[2].Value == "<=" ? v > k : v < k)) return false;
                            }
                            return true;
                        }
                        bool theirs = allParts.Contains("theirs");
                        var hit = Seats.Where(s => (!yours || s == seat) && (!theirs || s != seat))
                                       .SelectMany(s => s.Battlefield.Where(o => o.MergedInto == 0 && Abilities.Fits(o, filter) && Sized(o))).ToList();
                        var to = e.Kind == EffectKind.DestroyAll ? Zone.Graveyard : e.Kind == EffectKind.ExileAll ? Zone.Exile : Zone.Hand;
                        foreach (var o in hit) if (Seats[o.Seat].Find(o.Id) is { } live && Seats[o.Seat].ZoneOf(live) == Zone.Battlefield) Move(live, to);
                        lastDestroyed = hit.Count;
                        Write($"{e.Text.Replace("~", name)}: {hit.Count} {(hit.Count == 1 ? "permanent" : "permanents")}.");
                        break;
                    }
                    case EffectKind.Regenerate:
                        foreach (var o in each)
                        {
                            if (!o.TempKeywords.Contains("Regenerate")) o.TempKeywords.Add("Regenerate");
                            Write($"{o.ShownName} has a regeneration shield this turn (the next time it would be destroyed: tap it, remove it from combat, clear its damage).");
                        }
                        break;
                    case EffectKind.ExileTop: MoveTop(seat, n, Zone.Exile); break;
                    case EffectKind.Coin:
                        won = RandomNumberGenerator.GetInt32(2) == 0;
                        Write($"{seat.Name} flipped a coin: {(won.Value ? "won the flip" : "lost the flip")}.");
                        break;
                    case EffectKind.Die:
                        Write($"{seat.Name} rolled a {Math.Max(2, n)}-sided die: {RandomNumberGenerator.GetInt32(Math.Max(2, n)) + 1}.");
                        break;
                    case EffectKind.AtNextUpkeep:
                        seat.AtNextUpkeep.Add(e.Text.Replace("~", name) + $" ({name})");
                        Write($"At the next upkeep: {e.Text.Replace("~", name)} (the log will remind you).");
                        break;
                    case EffectKind.UntapAll:
                    {
                        string filter = e.What.Split('|')[0];
                        var them = seat.Battlefield.Where(o => o.Tapped && o.MergedInto == 0 && Abilities.Fits(o, filter)).ToList();
                        SetTapped(seat, them, false);
                        break;
                    }
                    case EffectKind.ToLibraryTop:
                        if (on != null)
                        {
                            Move(on, Zone.Library, e.What == "bottom" ? Place.Bottom : Place.Top);
                            var lib = Seats[on.Seat].Library;
                            if (e.What == "second" && lib.Count > 1 && lib[0].Id == on.Id) { lib.RemoveAt(0); lib.Insert(1, on); Write($"{on.ShownName} is second from the top."); }
                        }
                        break;
                    case EffectKind.ReturnAllFromGraveyard:
                    {
                        var rParts = e.What.Split('|');
                        foreach (var gc in seat.Graveyard.Where(o => Abilities.Fits(o, rParts[0])).ToList())
                        {
                            var rAt = spotFor?.Invoke(gc) ?? (0.3, 0.1);
                            Move(gc, Zone.Battlefield, Place.Top, rAt.X, rAt.Y, tapped: rParts.Contains("tapped"));
                        }
                        break;
                    }
                    case EffectKind.CopyToken:
                        if (on != null)
                        {
                            var copyAt = spotFor?.Invoke(on) ?? (Math.Min(1, on.X + 0.04), on.Y);
                            CreateToken(seat, on.Card, copyAt.X, copyAt.Y, on.Layout, 1, on.Loyalty);
                        }
                        break;
                    case EffectKind.ReturnToBattlefield:
                    {
                        var returning = e.On == EffectOn.Self ? seat.Find(c.Id) : picked != null ? FindAny(picked.Id) : null;
                        if (returning == null || Seats[returning.Seat].ZoneOf(returning) is not (Zone.Exile or Zone.Graveyard)) break;
                        var backAt = spotFor?.Invoke(returning) ?? (0.3, 0.1);
                        Move(returning, Zone.Battlefield, Place.Top, backAt.X, backAt.Y, backUp: e.What == "transformed" ? !returning.BackUp && returning.HasBack : false);
                        break;
                    }
                    case EffectKind.AtNextEndStep:
                        seat.AtNextEndStep.Add(e.Text.Replace("~", name) + $" ({name})");
                        Write($"At the next end step: {e.Text.Replace("~", name)} (the log will remind you).");
                        break;
                    case EffectKind.Energy:
                        seat.Energy += n;
                        Write($"{seat.Name} {seat.Has} {seat.Energy} energy.");
                        break;
                    case EffectKind.DiscardHand:
                        foreach (var h in seat.Hand.ToList()) Move(h, Zone.Graveyard);
                        break;
                    case EffectKind.Monstrosity:
                        if (on == null) break;
                        if (on.Counters.ContainsKey("Monstrous")) { Write($"{on.ShownName} is already monstrous: nothing happens."); break; }
                        AddCounters(on, "+1/+1", n);
                        AddCounters(on, "Monstrous", 1);
                        break;
                    case EffectKind.Adapt:
                        if (on == null) break;
                        if (on.Counters.GetValueOrDefault("+1/+1") > 0) { Write($"{on.ShownName} already has +1/+1 counters: adapt does nothing."); break; }
                        AddCounters(on, "+1/+1", n);
                        break;
                    case EffectKind.SetCounter:
                        if (on != null) AddCounters(on, e.What, n - on.Counters.GetValueOrDefault(e.What));
                        break;
                    case EffectKind.ReturnSelf:
                        if (seat.Find(c.Id) is { } back && seat.ZoneOf(back) == Zone.Graveyard)
                        {
                            if (e.What.StartsWith("Hand")) Move(back, Zone.Hand);
                            else
                            {
                                var spot = spotFor?.Invoke(back) ?? (0.3, 0.1);
                                Move(back, Zone.Battlefield, Place.Top, spot.X, spot.Y);
                                var rsParts = e.What.Split('|');
                                if (rsParts[0].EndsWith("tapped")) back.Tapped = true;
                                if (rsParts.Length > 1) AddCounters(back, rsParts[1], 1);           // "with a finality counter on it"
                            }
                        }
                        break;
                    case EffectKind.Note: Write($"Note: {e.Text.Replace("~", name)}."); break;
                    case EffectKind.Opponents: Write($"For the other players: {e.Text.Replace("~", name)}."); break;
                    case EffectKind.FromGraveyard:
                        left.Add(new AbilityFollowUp(e.Kind, n, e.Text));
                        break;
                    case EffectKind.Proliferate:
                        foreach (var o in seat.Battlefield.Where(o => o.Counters.Count > 0).ToList())
                            foreach (var kind in o.Counters.Keys.ToList()) AddCounters(o, kind, 1);
                        Write("Proliferated: one more of each counter on your permanents (an opponent's poison and the rest are yours to add).");
                        break;
                    case EffectKind.LookTop when e.On == EffectOn.Player:
                    {
                        // "Look at the top N cards of target player's library": whose library goes with it.
                        var lookSeat = target == -1 ? seat : Others(seat).FirstOrDefault();
                        if (lookSeat == null) { Write($"{e.Text.Replace("~", name)}: no opponent at the table."); break; }
                        left.Add(new AbilityFollowUp(e.Kind, n, e.Text, Seat: lookSeat.Index));
                        break;
                    }
                    case EffectKind.Scry:
                    case EffectKind.Surveil:
                    case EffectKind.LookTop:
                    case EffectKind.Search:
                    case EffectKind.Discard:
                    case EffectKind.Mana:
                        left.Add(new AbilityFollowUp(e.Kind, n, e.Text));
                        break;
                    default:
                        Write($"For you to do: {e.Text.Replace("~", name)}.");
                        break;
                }
            }
        }

        /// <summary>
        /// Discover N: cards come off the top of the library until a nonland card with
        /// mana value N or less; it goes to the hand (cast it without paying its mana
        /// cost, or keep it), the rest to the bottom in a random order.
        /// </summary>
        private void DiscoverCore(Seat seat, int n)
        {
            var passed = new List<GameCard>();
            GameCard? found = null;
            while (seat.Library.Count > 0)
            {
                var top = seat.Library[0];
                seat.Library.RemoveAt(0);
                if (!top.FaceType(0).Contains("Land", StringComparison.OrdinalIgnoreCase) && top.Card.ManaValue <= n) { found = top; break; }
                passed.Add(top);
            }
            ShuffleList(passed);
            seat.Library.AddRange(passed);
            if (found != null)
            {
                seat.Hand.Add(found);
                Write($"{seat.Name} discovered {found.ShownName} (mana value {found.Card.ManaValue}): it's in the hand, to cast without paying its mana cost or keep. " +
                      $"{passed.Count} card{(passed.Count == 1 ? "" : "s")} went to the bottom in a random order.");
            }
            else Write($"Discover {n}: no nonland card with mana value {n} or less; {passed.Count} card{(passed.Count == 1 ? "" : "s")} went back to the bottom.");
        }

        // ── Combat ───────────────────────────────────────────────────────
        /// <summary>Combat's "whenever … attacks / blocks / dies" reminders, as they come (the table shows them; your call).</summary>
        public event Action<List<Reminder>>? Reminded;

        /// <summary>Log and show combat's reminders (off: neither).</summary>
        public bool ShowReminders { get; set; } = true;

        private void Remind(List<Reminder> list)
        {
            if (!ShowReminders || list.Count == 0) return;
            foreach (var r in list) Write($"Reminder: {r.Card.ShownName}: {r.Text}");
            Reminded?.Invoke(list);
        }

        /// <summary>Printed (or given) "~ can't be blocked" / "~ can't block" on a creature.</summary>
        private static bool Says(GameCard c, string what) =>
            c.TempKeywords.Any(k => k.Equals(what, StringComparison.OrdinalIgnoreCase)) ||
            (c.ShownText ?? "").Replace("\r", "").Split('\n').Any(l =>
                System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), $@"^(?:{System.Text.RegularExpressions.Regex.Escape(c.FaceName(c.BackUp ? 1 : 0))}|This creature|~) {System.Text.RegularExpressions.Regex.Escape(what)}\.?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        /// <summary>The players who've lost, and why: 0 life, 10 poison, 21 combat damage from one commander, drawing from an empty library.</summary>
        public List<(Seat Seat, string Why)> Losers()
        {
            var list = new List<(Seat, string)>();
            foreach (var s in Seats)
            {
                if (s.Life <= 0) list.Add((s, $"{s.Life} life"));
                else if (s.Poison >= 10) list.Add((s, $"{s.Poison} poison counters"));
                else if (s.CommanderDamage.FirstOrDefault(kv => kv.Value >= 21) is { Value: >= 21 } cd)
                    list.Add((s, $"{cd.Value} damage from {(FindAny(cd.Key)?.ShownName ?? "a commander")}"));
                else if (s.DrewFromEmpty) list.Add((s, "drew from an empty library"));
            }
            return list;
        }
        /// <summary>Someone is attacking (attackers declared this combat).</summary>
        public bool InCombat => Seats.Any(s => s.Battlefield.Any(c => c.Attacking));

        /// <summary>Combat damage has been dealt this combat.</summary>
        public bool CombatDealt => Seats.Any(s => s.Battlefield.Any(c => c.Attacking && c.DealtCombat));

        private bool HasKeyword(GameCard c, string keyword) =>
            Keywords.For(c, Seats[c.Seat], Seats).Any(k => k.StartsWith(keyword, StringComparison.OrdinalIgnoreCase));

        /// <summary>The player being attacked: the first other player (or none, on your own).</summary>
        private Seat? DefenderOf(Seat attacker) => Others(attacker).FirstOrDefault();

        /// <summary>
        /// Declare attackers (one move): each creature attacks the defending player or one of their
        /// planeswalkers (<paramref name="attackers"/>: id and target, 0 = the player). It taps,
        /// unless it has vigilance; a summoning-sick one is noted (your call).
        /// </summary>
        public void DeclareAttackers(Seat seat, IReadOnlyList<(int Id, int Target)> attackers)
        {
            var list = attackers.Select(a => (Card: seat.Find(a.Id), a.Target)).Where(a => a.Card != null && seat.ZoneOf(a.Card) == Zone.Battlefield).ToList();
            if (list.Count == 0) return;
            Remember($"Attack with {list.Count}");
            var defender = DefenderOf(seat);
            foreach (var (card, target) in list)
            {
                var c = card!;
                c.Attacking = true;
                c.AttackTarget = target;
                c.Blocking = 0;
                c.DealtCombat = false;
                bool vigilance = HasKeyword(c, "Vigilance");
                if (!vigilance) c.Tapped = true;
                string whom = target > 0 && FindAny(target) is { } walker ? walker.ShownName : defender?.Name ?? "the defending player";
                Write($"{c.ShownName} attacks {whom}" + (vigilance ? " (vigilance: it stays untapped)." : "."));
                if (c.EnteredTurn == Turn && !HasKeyword(c, "Haste"))
                    Write($"Note: {c.ShownName} came in this turn (summoning sick): it can't attack unless something gives it haste.");
                if (HasKeyword(c, "Defender")) Write($"Note: {c.ShownName} has defender: it can't attack.");
            }
            Remind(Reminders.Attacks(seat, Seats, list.Select(a => a.Card!).ToList()));
            Done();
        }

        /// <summary>A creature blocks an attacker (or, <paramref name="attacker"/> null, stops blocking).</summary>
        public void Block(GameCard blocker, GameCard? attacker)
        {
            var s = Seats[blocker.Seat];
            var b = s.Find(blocker.Id);
            if (b == null || s.ZoneOf(b) != Zone.Battlefield) return;
            if ((attacker?.Id ?? 0) == b.Blocking) return;
            Remember(attacker == null ? $"{b.ShownName} doesn't block" : $"{b.ShownName} blocks {attacker.ShownName}");
            bool firstBlock = attacker != null && !Seats.Any(o => o.Battlefield.Any(x => x.Blocking == attacker.Id));
            b.Blocking = attacker?.Id ?? 0;
            b.BlockOrder = attacker == null ? 0 : Seats.SelectMany(o => o.Battlefield).Where(x => x.Blocking == attacker.Id).Max(x => x.BlockOrder) + 1;
            if (attacker == null) Write($"{b.ShownName} isn't blocking.");
            else
            {
                Write($"{b.ShownName} blocks {attacker.ShownName}.");
                if (Says(attacker, "can't be blocked")) Write($"Note: {attacker.ShownName} can't be blocked.");
                if (Says(b, "can't block")) Write($"Note: {b.ShownName} can't block.");
                if (HasKeyword(attacker, "Flying") && !HasKeyword(b, "Flying") && !HasKeyword(b, "Reach"))
                    Write($"Note: {attacker.ShownName} has flying: only a creature with flying or reach can block it.");
                if (b.Tapped) Write($"Note: {b.ShownName} is tapped: a tapped creature can't block.");
                Remind(Reminders.Blocks(b, attacker, firstBlock));
            }
            Done();
        }

        /// <summary>An attacker's damage order: this blocker takes its damage first.</summary>
        public void FirstInDamageOrder(GameCard blocker)
        {
            var s = Seats[blocker.Seat];
            var b = s.Find(blocker.Id);
            if (b == null || b.Blocking == 0) return;
            var others = Seats.SelectMany(o => o.Battlefield).Where(x => x.Blocking == b.Blocking && x.Id != b.Id).ToList();
            if (others.Count == 0) return;
            Remember($"{b.ShownName} first in the damage order");
            b.BlockOrder = others.Min(x => x.BlockOrder) - 1;
            Write($"{b.ShownName} takes {(FindAny(b.Blocking)?.ShownName ?? "the attacker")}'s damage first.");
            Done();
        }

        /// <summary>Blocks are set: menace blocked by just one creature is noted (your call).</summary>
        public void BlocksDone()
        {
            foreach (var a in Seats.SelectMany(s => s.Battlefield.Where(c => c.Attacking)))
            {
                int count = Seats.Sum(s => s.Battlefield.Count(b => b.Blocking == a.Id));
                if (count == 1 && HasKeyword(a, "Menace")) Write($"Note: {a.ShownName} has menace: it can't be blocked except by two or more creatures.");
            }
        }

        /// <summary>
        /// Combat damage (one move, one Undo): first strike and double strike first, then the rest.
        /// An unblocked attacker hits the defending player (or the planeswalker it attacks); a blocked
        /// one gives each blocker lethal damage in the order they blocked, the rest to the last (trample:
        /// the rest to the player). Blockers hit back. Deathtouch, lifelink and infect count; creatures
        /// with lethal damage go to the graveyard (indestructible ones stay; a regeneration shield is used).
        /// Then combat ends: on to the second main phase.
        /// </summary>
        public void CombatDamage()
        {
            var attackers = Seats.SelectMany(s => s.Battlefield.Where(c => c.Attacking && c.MergedInto == 0)).ToList();
            if (attackers.Count == 0 || CombatDealt) return;
            Remember("Combat damage");
            _batch++;
            try
            {
                var deathtouched = new HashSet<int>();
                var died = new List<GameCard>();
                var blocked = attackers.Where(a => Seats.Any(s => s.Battlefield.Any(b => b.Blocking == a.Id))).Select(a => a.Id).ToHashSet();
                bool OnField(GameCard c) => Seats[c.Seat].Find(c.Id) is { } live && Seats[c.Seat].ZoneOf(live) == Zone.Battlefield;
                void Hit(GameCard from, GameCard to, int amount)
                {
                    if (amount <= 0) return;
                    MarkDamage(to, amount);
                    if (HasKeyword(from, "Deathtouch")) deathtouched.Add(to.Id);
                    if (HasKeyword(from, "Lifelink")) ChangeLife(Seats[from.Seat], amount);
                }
                void HitDefender(GameCard a, int amount)
                {
                    if (amount <= 0) return;
                    if (a.AttackTarget > 0 && FindAny(a.AttackTarget) is { } target && OnField(target))
                    {
                        string kind = target.Counters.ContainsKey("Defense") ? "Defense" : "Loyalty";
                        AddCounters(target, kind, -Math.Min(amount, target.Counters.GetValueOrDefault(kind)));
                        Write($"{a.ShownName} deals {amount} damage to {target.ShownName}.");
                    }
                    else if (DefenderOf(Seats[a.Seat]) is { } defender)
                    {
                        if (HasKeyword(a, "Infect")) ChangePlayerCounter(defender, "Poison", amount);
                        else ChangeLife(defender, -amount);
                        Write($"{a.ShownName} deals {amount} combat damage to {defender.Name}.");
                        if (a.IsCommander)
                        {
                            int had = defender.CommanderDamage.GetValueOrDefault(a.Id);
                            defender.CommanderDamage[a.Id] = had + amount;
                            Write($"Commander damage: {defender.Name} {defender.Has} taken {had + amount} from {a.ShownName}" + (had + amount >= 21 ? " (21 or more loses the game)." : "."));
                        }
                    }
                    else Write($"{a.ShownName} deals {amount} combat damage to the defending player (no opponent at the table: count it).");
                    if (HasKeyword(a, "Lifelink")) ChangeLife(Seats[a.Seat], amount);
                }
                bool anyFirst = attackers.Concat(Seats.SelectMany(s => s.Battlefield.Where(b => b.Blocking != 0)))
                                         .Any(c => HasKeyword(c, "First strike") || HasKeyword(c, "Double strike"));
                foreach (bool first in anyFirst ? new[] { true, false } : new[] { false })
                {
                    bool Strikes(GameCard c) => first ? HasKeyword(c, "First strike") || HasKeyword(c, "Double strike")
                                                      : !anyFirst || !HasKeyword(c, "First strike") || HasKeyword(c, "Double strike");
                    foreach (var a0 in attackers)
                    {
                        if (!OnField(a0)) continue;
                        var a = Seats[a0.Seat].Find(a0.Id)!;
                        var blockers = Seats.SelectMany(s => s.Battlefield.Where(b => b.Blocking == a.Id && b.MergedInto == 0)).Where(OnField).OrderBy(b => b.BlockOrder).ToList();
                        if (Strikes(a))
                        {
                            int power = Math.Max(0, a.PowerNow ?? 0);
                            bool trample = HasKeyword(a, "Trample");
                            if (blockers.Count == 0)
                            {
                                if (!blocked.Contains(a.Id) || trample) HitDefender(a, power);    // blocked, and the blockers are gone: no damage (trample: all of it)
                            }
                            else
                            {
                                int left = power;
                                for (int i = 0; i < blockers.Count && left > 0; i++)
                                {
                                    var b = blockers[i];
                                    int lethal = HasKeyword(a, "Deathtouch") ? 1 : Math.Max(0, (b.ToughnessNow ?? 0) - b.Damage);
                                    int give = i == blockers.Count - 1 && !trample ? left : Math.Min(left, lethal);
                                    Hit(a, b, give);
                                    left -= give;
                                }
                                if (left > 0 && trample) HitDefender(a, left);
                            }
                        }
                        foreach (var b in blockers.Where(Strikes)) Hit(b, a, Math.Max(0, b.PowerNow ?? 0));
                    }
                    // Lethal damage: to the graveyard (between first strike and the rest, too).
                    foreach (var s in Seats)
                        foreach (var c in s.Battlefield.Where(c => c.MergedInto == 0 && c.IsCreatureNow).ToList())
                        {
                            bool lethal = c.ToughnessNow is int t && c.Damage >= t || deathtouched.Contains(c.Id) && c.Damage > 0;
                            if (!lethal) continue;
                            if (HasKeyword(c, "Indestructible")) { Write($"{c.ShownName} has lethal damage but is indestructible."); continue; }
                            if (c.TempKeywords.Remove("Regenerate"))
                            {
                                c.Tapped = true; c.Damage = 0; c.Attacking = false; c.Blocking = 0;
                                Write($"{c.ShownName} regenerates: tapped, its damage cleared, out of combat.");
                                continue;
                            }
                            Write($"{c.ShownName} dies.");
                            died.Add(c);
                            Move(c, Zone.Graveyard);
                        }
                    foreach (var s in Seats)
                        foreach (var w in s.Battlefield.Where(w => w.FaceType(w.BackUp ? 1 : 0).Contains("Planeswalker", StringComparison.OrdinalIgnoreCase) &&
                                                                   w.Counters.ContainsKey("Loyalty") && w.Counters["Loyalty"] <= 0).ToList())
                        {
                            Write($"{w.ShownName} has no loyalty left: it goes to the graveyard.");
                            Move(w, Zone.Graveyard);
                        }
                }
                foreach (var s in Seats) foreach (var a in s.Battlefield.Where(c => c.Attacking)) a.DealtCombat = true;
                Remind(Reminders.Deaths(Seats, died));
                // Combat is over: on to the second main phase (part of the same move, so Undo takes both back).
                StepCore(Active, false);
                Write($"End of combat: on to {PhaseName(Phase)}.");
            }
            finally
            {
                _batch--;
            }
            Done();
        }

        /// <summary>The end of combat: nothing is attacking or blocking any more.</summary>
        private void EndCombatCore()
        {
            foreach (var s in Seats)
                foreach (var c in s.Battlefield)
                {
                    c.Attacking = false;
                    c.AttackTarget = 0;
                    c.Blocking = 0;
                    c.DealtCombat = false;
                }
        }

        /// <summary>Log what an ability says without doing it (the table's "just remind me").</summary>
        public void Remind(Seat seat, GameCard card, Ability ability) =>
            Note($"{card.ShownName}: {ability.Text.Replace("~", card.ShownName)}");

        // ── Until end of turn ────────────────────────────────────────────
        /// <summary>Change a creature's power and toughness until end of turn (+3/+3, -2/-0 …). It wears off at the cleanup.</summary>
        public void Pump(GameCard card, int power, int toughness)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || (power == 0 && toughness == 0)) return;
            string what = $"{(power >= 0 ? "+" : "")}{power}/{(toughness >= 0 ? "+" : "")}{toughness}";
            Remember($"{what} on {c.ShownName}");
            c.PowerMod += power;
            c.ToughnessMod += toughness;
            Write($"{c.ShownName} gets {what} until end of turn" +
                  (c.PowerNow is int p && c.ToughnessNow is int t ? $" ({p}/{t})." : "."));
            Done();
        }

        /// <summary>Take off every until-end-of-turn change on a creature.</summary>
        public void ClearPump(GameCard card)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || (c.PowerMod == 0 && c.ToughnessMod == 0)) return;
            Remember($"Clear changes on {c.ShownName}");
            c.PowerMod = c.ToughnessMod = 0;
            Write($"{c.ShownName} is back to its own power and toughness.");
            Done();
        }

        // ── Damage, counters on you, the stack, notes ────────────────────
        /// <summary>Mark damage on a card (a negative number takes some off). It wears off at the end of the turn.</summary>
        public void MarkDamage(GameCard card, int n)
        {
            var c = Seats[card.Seat].Find(card.Id);
            if (c == null || n == 0) return;
            int next = Math.Max(0, c.Damage + n);
            if (next == c.Damage) return;
            Remember($"Damage on {c.ShownName}");
            c.Damage = next;
            Write(next == 0 ? $"{c.ShownName} has no damage marked."
                            : $"{c.ShownName} has {next} damage marked" +
                              (c.ToughnessNow is int t && next >= t ? $" (its toughness is {t}: that's lethal)." : "."));
            Done();
        }

        /// <summary>Poison, Energy or Experience on a player.</summary>
        public void ChangePlayerCounter(Seat seat, string kind, int delta)
        {
            int now = kind switch { "Poison" => seat.Poison, "Energy" => seat.Energy, _ => seat.Experience };
            int next = Math.Max(0, now + delta);
            if (next == now) return;
            Remember($"{kind} {next}");
            switch (kind)
            {
                case "Poison": seat.Poison = next; break;
                case "Energy": seat.Energy = next; break;
                default: seat.Experience = next; break;
            }
            Write($"{seat.Name} {seat.Has} {next} {kind.ToLowerInvariant()}" +
                  (kind == "Poison" && next >= 10 ? " counters: ten or more loses the game." : kind == "Poison" ? $" counter{(next == 1 ? "" : "s")}." : "."));
            Done();
        }

        /// <summary>Everything on the stack, every seat's, the one that resolves first (the latest) first.</summary>
        public List<GameCard> StackTopFirst() =>
            Seats.SelectMany(s => s.Stack).OrderByDescending(c => c.StackOrder).ToList();

        /// <summary>A stack card resolves: an instant or sorcery to the graveyard, anything else onto the battlefield at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public void Resolve(GameCard card, double x, double y)
        {
            var seat = Seats[card.Seat];
            var c = seat.Find(card.Id);
            if (c == null || seat.ZoneOf(c) != Zone.Stack) return;
            // Cast with a target (an Aura, or mutating): still there? Attach or merge. Gone? An Aura goes to the
            // graveyard (it has nothing to enchant); a mutating creature just enters on its own.
            if (c.PendingTarget != 0)
            {
                var host = seat.Find(c.PendingTarget);
                bool there = host != null && seat.ZoneOf(host) == Zone.Battlefield && host.MergedInto == 0;
                if (there && c.PendingMutate) { Merge(c, host!, c.PendingOnTop); return; }
                if (there) { Attach(c, host!); return; }
                if (!c.PendingMutate)
                {
                    Move(c, Zone.Graveyard);
                    Write($"{c.ShownName} had nothing left to enchant, so it went to the graveyard.");
                    Done();
                    return;
                }
            }
            if (c.IsInstantOrSorceryNow) Move(c, Zone.Graveyard);
            else Move(c, Zone.Battlefield, Place.Top, x, y);
        }

        // ── Attaching and mutate ─────────────────────────────────────────
        /// <summary>
        /// A card leaving the battlefield takes the cards merged under it
        /// (mutate) to the same place; what's attached to it stays behind:
        /// Auras go to the graveyard, Equipment and Fortifications come off.
        /// Part of the same move (one Undo).
        /// </summary>
        private void LeaveTogether(Seat seat, GameCard c, Zone to, Place place)
        {
            var merged = seat.Battlefield.Where(o => o.MergedInto == c.Id).ToList();
            var ids = merged.Select(m => m.Id).Append(c.Id).ToHashSet();
            foreach (var m in merged)
            {
                seat.Battlefield.Remove(m);
                m.Reset();
                if (m.IsToken) continue;
                var list = seat.ZoneList(to);
                if (to == Zone.Hand || (to == Zone.Library && place == Place.Bottom)) list.Add(m); else list.Insert(0, m);
            }
            if (merged.Count > 0)
                Write($"{string.Join(", ", merged.Select(m => m.FaceName(0)))} went along with it (merged by mutate).");
            foreach (var a in seat.Battlefield.Where(o => ids.Contains(o.AttachedTo)).ToList())
            {
                if (a.IsAura)
                {
                    seat.Battlefield.Remove(a);
                    a.Reset();
                    if (!a.IsToken) seat.Graveyard.Insert(0, a);
                    Write($"{a.ShownName} went to the graveyard (what it enchanted left).");
                }
                else
                {
                    a.AttachedTo = 0;
                    a.X = Math.Clamp(c.X + 0.04, 0, 1);
                    a.Y = c.Y;
                    Write($"{a.ShownName} came off and stays on the battlefield.");
                }
            }
        }

        /// <summary>Attach a card (from anywhere: the battlefield, the stack, the hand) to a card on the battlefield.</summary>
        public void Attach(GameCard card, GameCard host)
        {
            var seat = Seats[card.Seat];
            var c = seat.Find(card.Id);
            var h = Seats[host.Seat].Find(host.Id);
            if (c == null || h == null || c.Id == h.Id) return;
            while (h.MergedInto != 0 && Seats[host.Seat].Find(h.MergedInto) is { } top) h = top;      // onto the merged permanent's top
            Remember($"Attach {c.ShownName}");
            var from = seat.ZoneOf(c);
            if (from != Zone.Battlefield)
            {
                if (from != null) seat.ZoneList(from.Value).Remove(c);
                c.PendingTarget = 0;
                c.PendingMutate = false;
                c.Tapped = false;
                seat.Battlefield.Add(c);
            }
            c.AttachedTo = h.Id;
            c.X = h.X;
            c.Y = h.Y;
            Write($"{seat.Name} attached {c.ShownName} to {h.ShownName}.");
            Done();
        }

        /// <summary>Take an attached card off (it stays on the battlefield, at x, y).</summary>
        public void Unattach(GameCard card, double x, double y)
        {
            var seat = Seats[card.Seat];
            var c = seat.Find(card.Id);
            if (c == null || c.AttachedTo == 0) return;
            string host = seat.Find(c.AttachedTo)?.ShownName ?? "its card";
            Remember($"Unattach {c.ShownName}");
            c.AttachedTo = 0;
            c.X = Math.Clamp(x, 0, 1);
            c.Y = Math.Clamp(y, 0, 1);
            Write($"{seat.Name} took {c.ShownName} off {host}.");
            Done();
        }

        /// <summary>
        /// Mutate: merge a card with a creature on the battlefield, on top (it
        /// becomes the face, keeping the creature's place, tapped state,
        /// counters and attachments) or underneath (adds its abilities).
        /// </summary>
        public void Merge(GameCard card, GameCard host, bool onTop)
        {
            var seat = Seats[card.Seat];
            var c = seat.Find(card.Id);
            var h = seat.Find(host.Id);
            if (c == null || h == null || c.Id == h.Id) return;
            while (h.MergedInto != 0 && seat.Find(h.MergedInto) is { } top) h = top;
            Remember($"Mutate {c.ShownName}");
            var from = seat.ZoneOf(c);
            if (from != null) seat.ZoneList(from.Value).Remove(c);
            c.PendingTarget = 0;
            c.PendingMutate = false;
            seat.Battlefield.Add(c);
            if (onTop)
            {
                c.X = h.X; c.Y = h.Y; c.Tapped = h.Tapped; c.Damage = h.Damage;
                c.PowerMod = h.PowerMod; c.ToughnessMod = h.ToughnessMod;
                c.EnteredTurn = h.EnteredTurn;                            // the same creature: no new summoning sickness
                h.PowerMod = h.ToughnessMod = 0;
                c.Counters = new Dictionary<string, int>(h.Counters, StringComparer.OrdinalIgnoreCase);
                h.Counters.Clear();
                h.Damage = 0;
                foreach (var o in seat.Battlefield.Where(o => o.MergedInto == h.Id || o.AttachedTo == h.Id).ToList())
                {
                    if (o.MergedInto == h.Id) o.MergedInto = c.Id;
                    if (o.AttachedTo == h.Id) o.AttachedTo = c.Id;
                }
                h.MergedInto = c.Id;
                Write($"{seat.Name} mutated {c.ShownName} on top of {h.ShownName}.");
            }
            else
            {
                c.MergedInto = h.Id;
                Write($"{seat.Name} mutated {c.ShownName} underneath {h.ShownName}.");
            }
            Done();
        }

        /// <summary>The cards merged into a permanent (its top first, then the rest), for showing what it is.</summary>
        public List<GameCard> MergedCards(GameCard top)
        {
            var seat = Seats[top.Seat];
            return new[] { top }.Concat(seat.Battlefield.Where(o => o.MergedInto == top.Id)).ToList();
        }

        /// <summary>Cast a spell with a target waiting (an Aura's card, or the creature to mutate onto): onto the stack.</summary>
        public void CastWithTarget(GameCard card, GameCard target, bool mutate, bool onTop, bool backUp = false)
        {
            var seat = Seats[card.Seat];
            if (seat.Find(card.Id) == null) return;
            Move(card, Zone.Stack, backUp: backUp);
            var c = seat.Find(card.Id);                      // after the move (Undo keeps the copy from before)
            if (c == null) return;
            c.PendingTarget = target.Id;
            c.PendingMutate = mutate;
            c.PendingOnTop = onTop;
            Write(mutate ? $"(mutating {(onTop ? "on top of" : "underneath")} {target.ShownName})" : $"(targeting {target.ShownName})");
        }

        /// <summary>A line in the log that isn't a move (a combo spotted, a reminder): no Undo step.</summary>
        public void Note(string line)
        {
            Write(line);
        }

        // ── Saving a game to pick up later ───────────────────────────────
        /// <summary>Everything needed to carry on later (not the Undo history).</summary>
        public sealed class State
        {
            public List<Seat> Seats { get; set; } = new();
            public int Turn { get; set; } = 1;
            public Phase Phase { get; set; } = Phase.Main1;
            public List<string> Log { get; set; } = new();
            public int NextId { get; set; } = 1;
            public int StackCounter { get; set; }
            public int ActiveSeat { get; set; }
            public DateTime Saved { get; set; }
        }

        public State Save() => new()
        {
            Seats = Seats.Select(s => s.Clone()).ToList(), Turn = Turn, Phase = Phase,
            Log = Log.ToList(), NextId = _nextId, StackCounter = _stackCounter, ActiveSeat = ActiveSeat, Saved = DateTime.Now,
        };

        /// <summary>A game carried on from where it was saved.</summary>
        public static Game Load(State s)
        {
            var g = new Game
            {
                Seats = s.Seats, Turn = s.Turn, Phase = s.Phase, Log = s.Log,
                _nextId = Math.Max(s.NextId, s.Seats.SelectMany(x => Seat.AllZones.SelectMany(z => x.ZoneList(z))).Select(c => c.Id + 1).DefaultIfEmpty(1).Max()),
                _stackCounter = s.StackCounter,
                ActiveSeat = Math.Clamp(s.ActiveSeat, 0, Math.Max(0, s.Seats.Count - 1)),
            };
            g.RefreshStars();
            return g;
        }

        /// <summary>Something has happened since the game started (worth saving when the table closes).</summary>
        public bool HasMoves => _undo.Count > 0 || Turn > 1;

        // ── The opening hand at a glance ─────────────────────────────────
        /// <summary>
        /// For the keep-or-mulligan choice: lands (and spell // land cards),
        /// the colors the lands make, the colors the spells need, and the
        /// spells' mana values.
        /// </summary>
        public static HandSummary Summarize(IReadOnlyList<GameCard> hand)
        {
            var lands = hand.Where(c => c.IsLandFront).ToList();
            var either = hand.Where(c => !c.IsLandFront && c.IsLandEitherFace).ToList();
            var spells = hand.Where(c => !c.IsLandFront).ToList();
            string Colors(IEnumerable<string> s) =>
                string.Concat("WUBRG".Where(ch => s.Any(x => x.Contains(ch))));
            return new HandSummary
            {
                Lands = lands.Count,
                ModalLands = either.Count,
                Spells = spells.Count,
                LandColors = Colors(lands.Concat(either).Select(c => c.Card.ColorIdentity ?? "")),
                SpellColors = Colors(spells.Select(c => CardFaces.Sides(c.Card.ManaCost)[0])),
                ManaValues = spells.Select(c => (int)Math.Round(c.Card.ManaValue)).OrderBy(v => v).ToList(),
            };
        }
    }

    public sealed class HandSummary
    {
        public int Lands { get; init; }
        /// <summary>Spells with a land on the back (counted as spells, and noted).</summary>
        public int ModalLands { get; init; }
        public int Spells { get; init; }
        public string LandColors { get; init; } = "";
        public string SpellColors { get; init; } = "";
        public List<int> ManaValues { get; init; } = new();

        /// <summary>Colors the spells need that no land in hand makes.</summary>
        public string MissingColors => string.Concat(SpellColors.Where(c => !LandColors.Contains(c)));
    }
}
