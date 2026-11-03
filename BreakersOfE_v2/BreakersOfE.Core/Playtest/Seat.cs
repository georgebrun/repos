using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// One player at the table: their deck's cards in each zone, life, and
    /// the mulligan. Lists run top first: Library[0] is the top card,
    /// Graveyard[0] the last card put there.
    /// </summary>
    public sealed class Seat
    {
        public int Index { get; init; }
        public string Name { get; set; } = "You";
        public string DeckName { get; set; } = "";
        /// <summary>The deck's id (its sleeves and playmat follow it).</summary>
        public string DeckId { get; set; } = "";
        public bool IsCommanderGame { get; set; }

        public int Life { get; set; } = 20;

        // The log's grammar: "You have 3 poison" but "George has 3 poison".
        public bool IsYou => Name.Equals("You", StringComparison.OrdinalIgnoreCase);
        /// <summary>"have" for You, else "has".</summary>
        public string Has => IsYou ? "have" : "has";
        /// <summary>"play" for You, else "plays".</summary>
        public string Plays => IsYou ? "play" : "plays";
        /// <summary>"Your" for You, else "George's".</summary>
        public string Whose => IsYou ? "Your" : Name + "'s";
        public int StartingLife { get; set; } = 20;

        public List<GameCard> Library { get; set; } = new();
        public List<GameCard> Hand { get; set; } = new();
        public List<GameCard> Battlefield { get; set; } = new();
        public List<GameCard> Graveyard { get; set; } = new();
        public List<GameCard> Exile { get; set; } = new();
        public List<GameCard> Command { get; set; } = new();
        public List<GameCard> Sideboard { get; set; } = new();
        public List<GameCard> Stack { get; set; } = new();

        // ── Counters on you ──────────────────────────────────────────────
        public int Poison { get; set; }
        public int Energy { get; set; }
        public int Experience { get; set; }

        // ── Mana pool ────────────────────────────────────────────────────
        /// <summary>Unspent mana by kind ("W", "U", "B", "R", "G", "C"). Empties as steps end (unless something keeps it).</summary>
        public Dictionary<string, int> Mana { get; set; } = new();

        /// <summary>Keep this turn's mana as steps end (a card's "this mana doesn't empty until end of turn"): off again at the cleanup.</summary>
        public bool KeepManaThisTurn { get; set; }

        public int ManaTotal => Mana.Values.Sum();

        /// <summary>"Sacrifice it at the beginning of the next end step" and the like: logged when the end step comes.</summary>
        public List<string> AtNextEndStep { get; set; } = new();

        /// <summary>"Draw a card at the beginning of the next turn's upkeep": logged when the upkeep comes.</summary>
        public List<string> AtNextUpkeep { get; set; } = new();

        /// <summary>"{G}{G}{W}" (nothing: "empty").</summary>
        public string ManaText => ManaTotal == 0 ? "empty"
            : string.Concat(ManaSources.Colors.SelectMany(m => Enumerable.Repeat("{" + m + "}", Mana.GetValueOrDefault(m))));

        /// <summary>The deck's Tokens part: what the Tokens button offers.</summary>
        public List<DeckCard> Tokens { get; set; } = new();

        // ── The opening hand ─────────────────────────────────────────────
        public int Mulligans { get; set; }

        /// <summary>Playing with the top card of the library revealed (Courser of Kruphix, Future Sight …).</summary>
        public bool TopRevealed { get; set; }

        /// <summary>The opening hand is kept (no more mulligans).</summary>
        public bool Kept { get; set; }

        /// <summary>After keeping a mulliganed hand: cards still to put on the bottom.</summary>
        public int BottomPending { get; set; }

        /// <summary>Times each commander (by card id) has been cast from the command zone: tax = 2 each.</summary>
        public Dictionary<int, int> CommanderCasts { get; set; } = new();

        public int CommanderTax(GameCard c) => 2 * CommanderCasts.GetValueOrDefault(c.Id);

        public List<GameCard> ZoneList(Zone z) => z switch
        {
            Zone.Library => Library,
            Zone.Hand => Hand,
            Zone.Battlefield => Battlefield,
            Zone.Graveyard => Graveyard,
            Zone.Exile => Exile,
            Zone.Command => Command,
            Zone.Sideboard => Sideboard,
            Zone.Stack => Stack,
            _ => throw new ArgumentOutOfRangeException(nameof(z)),
        };

        public static readonly Zone[] AllZones =
            { Zone.Library, Zone.Hand, Zone.Battlefield, Zone.Graveyard, Zone.Exile, Zone.Command, Zone.Sideboard, Zone.Stack };

        /// <summary>Where a card is now (null when it isn't this seat's any more: a token that left the battlefield).</summary>
        public Zone? ZoneOf(GameCard c)
        {
            foreach (var z in AllZones)
                if (ZoneList(z).Any(x => x.Id == c.Id)) return z;
            return null;
        }

        /// <summary>The live card with this id (cards are copied for Undo, so match by id).</summary>
        public GameCard? Find(int id)
        {
            foreach (var z in AllZones)
                foreach (var c in ZoneList(z))
                    if (c.Id == id) return c;
            return null;
        }

        public Seat Clone()
        {
            List<GameCard> Copy(List<GameCard> l) => l.Select(c => c.Clone()).ToList();
            return new Seat
            {
                Index = Index, Name = Name, DeckName = DeckName, DeckId = DeckId, IsCommanderGame = IsCommanderGame,
                Life = Life, StartingLife = StartingLife,
                Library = Copy(Library), Hand = Copy(Hand), Battlefield = Copy(Battlefield),
                Graveyard = Copy(Graveyard), Exile = Copy(Exile), Command = Copy(Command), Sideboard = Copy(Sideboard),
                Stack = Copy(Stack), Poison = Poison, Energy = Energy, Experience = Experience,
                Mana = new Dictionary<string, int>(Mana), KeepManaThisTurn = KeepManaThisTurn,
                AtNextEndStep = new List<string>(AtNextEndStep), AtNextUpkeep = new List<string>(AtNextUpkeep),
                Tokens = Tokens,
                Mulligans = Mulligans, Kept = Kept, BottomPending = BottomPending, TopRevealed = TopRevealed,
                CommanderCasts = new Dictionary<int, int>(CommanderCasts),
            };
        }
    }
}
