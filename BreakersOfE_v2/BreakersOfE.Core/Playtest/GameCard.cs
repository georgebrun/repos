using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;

namespace BreakersOfE.Playtest
{
    /// <summary>The battlefield's rows: creatures and the like at the top, artifacts and enchantments above the lands, lands at the bottom.</summary>
    public enum Lane { Top, Support, Land }

    /// <summary>Where a card is during a game.</summary>
    public enum Zone
    {
        Library,
        Hand,
        Battlefield,
        Graveyard,
        Exile,
        Command,
        Sideboard,
        /// <summary>Cast and waiting to resolve (spells go here first when the table's "stack" setting is on).</summary>
        Stack,
    }

    /// <summary>
    /// One physical card in a game (a deck line with 4 copies makes 4 of
    /// these). The printing's data stays on <see cref="Card"/>; everything
    /// that changes during play (tapped, turned over, counters, where it sits)
    /// is here. No screen code: the playtest table and, later, other tables
    /// (the Raspberry Pi app) show the same game.
    /// </summary>
    public sealed class GameCard
    {
        public int Id { get; init; }
        public DeckCard Card { get; init; } = null!;

        /// <summary>Scryfall's layout: "normal", "transform", "modal_dfc", "flip", "meld", "double_faced_token" …</summary>
        public string Layout { get; init; } = "";

        public int Seat { get; init; }
        public bool IsToken { get; init; }
        public bool IsCommander { get; init; }

        /// <summary>Printed loyalty (planeswalkers) or defense (battles), per side like "4" or "— // 3": the counters it enters with.</summary>
        public string Loyalty { get; init; } = "";

        // ── Changes during play ──────────────────────────────────────────
        public bool Tapped { get; set; }

        /// <summary>Showing its back face (transformed, or a modal card played on its back).</summary>
        public bool BackUp { get; set; }

        /// <summary>Face down (morph, manifest, disguise): shows the sleeve; only its owner may look.</summary>
        public bool FaceDown { get; set; }

        /// <summary>A Kamigawa flip card turned upside down (its picture turned 180°).</summary>
        public bool Flipped { get; set; }

        /// <summary>Counters by kind ("+1/+1", "-1/-1", "Loyalty", or the user's own): never 0 or less.</summary>
        public Dictionary<string, int> Counters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Damage marked on it this turn (cleared at the end of the turn, or when it leaves).</summary>
        public int Damage { get; set; }

        /// <summary>The turn it came onto the battlefield (summoning sickness: a creature can't attack or tap until your next turn). 0 = not known.</summary>
        public int EnteredTurn { get; set; }

        /// <summary>Keywords gained until end of turn ("gains flying until end of turn"): gone at the cleanup, or when it leaves.</summary>
        public List<string> TempKeywords { get; set; } = new();

        /// <summary>The turn a loyalty ability was last activated (one per turn).</summary>
        public int LoyaltyUsedTurn { get; set; }

        /// <summary>"Until end of turn" changes to power and toughness (Giant Growth: +3/+3): gone at the cleanup, or when it leaves.</summary>
        public int PowerMod { get; set; }
        public int ToughnessMod { get; set; }

        /// <summary>The card it's attached to (an Aura, Equipment, Fortification …); 0 = none.</summary>
        public int AttachedTo { get; set; }

        /// <summary>Mutate: part of the merged permanent whose top card has this id (drawn as that one); 0 = not merged under anything.</summary>
        public int MergedInto { get; set; }

        /// <summary>
        /// Cast with a target waiting on the stack: an Aura's card to attach to
        /// (<see cref="PendingMutate"/> false) or a creature to mutate onto. 0 = none.
        /// </summary>
        public int PendingTarget { get; set; }
        public bool PendingMutate { get; set; }
        /// <summary>Mutating onto the top (true) or underneath.</summary>
        public bool PendingOnTop { get; set; }

        /// <summary>An Aura (the face that's up).</summary>
        public bool IsAura => FaceType(BackUp ? 1 : 0).Contains("Aura", StringComparison.OrdinalIgnoreCase);

        /// <summary>Equipment or a Fortification.</summary>
        public bool IsEquipment => FaceType(BackUp ? 1 : 0) is var t &&
                                   (t.Contains("Equipment", StringComparison.OrdinalIgnoreCase) || t.Contains("Fortification", StringComparison.OrdinalIgnoreCase));

        /// <summary>Has mutate (read from its rules text).</summary>
        public bool HasMutate => Card.OracleText.Replace("\r", "").Split('\n')
                                     .Any(l => l.TrimStart().StartsWith("Mutate", StringComparison.OrdinalIgnoreCase));

        /// <summary>When it went on the stack (higher = later = resolves first).</summary>
        public int StackOrder { get; set; }

        /// <summary>Position on the battlefield, 0–1 across and down (so it holds when the window is resized).</summary>
        public double X { get; set; }
        public double Y { get; set; }

        // ── Read from the printing ───────────────────────────────────────
        public string ScryfallId => Card.ScryfallId;

        /// <summary>A second picture to turn over to (double-faced cards and tokens).</summary>
        public bool HasBack => !string.IsNullOrEmpty(Card.ImageBackUrl);

        /// <summary>A modal double-faced card (spell // land and the like): played on either face.</summary>
        public bool IsModal => Layout.Equals("modal_dfc", StringComparison.OrdinalIgnoreCase);

        /// <summary>A Kamigawa flip card (the other half printed upside down).</summary>
        public bool CanFlip => Layout.Equals("flip", StringComparison.OrdinalIgnoreCase);

        /// <summary>The name of one face (0 = front, 1 = back).</summary>
        public string FaceName(int side) => FaceValue(Card.Name, side);

        /// <summary>The type line of one face.</summary>
        public string FaceType(int side) => FaceValue(Card.TypeLine, side);

        private static string FaceValue(string value, int side)
        {
            var sides = CardFaces.Sides(value);
            return side < sides.Length && sides[side] != CardFaces.None ? sides[side] : sides[0];
        }

        /// <summary>The face showing now (on the battlefield): "Delver of Secrets", "Insectile Aberration".</summary>
        public string ShownName => FaceName(BackUp ? 1 : 0);

        /// <summary>A land (the face that's up; a land // spell card counts by the face it's played on).</summary>
        public bool IsLandNow => FaceType(BackUp ? 1 : 0).Contains("Land", StringComparison.OrdinalIgnoreCase);

        /// <summary>A land on its front: for the opening-hand count.</summary>
        public bool IsLandFront => FaceType(0).Contains("Land", StringComparison.OrdinalIgnoreCase);

        /// <summary>A land on either face (a modal spell // land card counts as both).</summary>
        public bool IsLandEitherFace => Card.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);

        /// <summary>"+1/+1 ×3 · Loyalty ×4".</summary>
        public string CountersText =>
            string.Join(" · ", Counters.Where(c => c.Value > 0).Select(c => $"{c.Key} ×{c.Value}"));

        /// <summary>Everything that changed during play goes back to how a card is printed (it left the battlefield).</summary>
        public void Reset()
        {
            Tapped = false;
            BackUp = false;
            FaceDown = false;
            Flipped = false;
            Damage = 0;
            PowerMod = 0;
            ToughnessMod = 0;
            TempKeywords.Clear();
            LoyaltyUsedTurn = 0;
            AttachedTo = 0;
            MergedInto = 0;
            PendingTarget = 0;
            PendingMutate = false;
            Counters.Clear();
        }

        /// <summary>The rules text of the face that's up.</summary>
        public string ShownText
        {
            get
            {
                var sides = CardFaces.TextSides(Card.OracleText);
                int s = BackUp ? 1 : 0;
                return s < sides.Length ? sides[s] : sides[0];
            }
        }

        /// <summary>An instant or sorcery (on the face that's up): goes to the graveyard when it resolves.</summary>
        public bool IsInstantOrSorceryNow
        {
            get
            {
                string t = FaceType(BackUp ? 1 : 0);
                return t.Contains("Instant", StringComparison.OrdinalIgnoreCase) || t.Contains("Sorcery", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>A basic land (Forest, Plains, Wastes, Snow-Covered Island …): piled together with others of its name on the table.</summary>
        public bool IsBasicLand => Card.TypeLine.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                                   Card.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Where it sits when played: lands along the bottom; non-creature
        /// artifacts and enchantments (Auras too, until they can be attached) in the row above the lands;
        /// everything else (creatures, planeswalkers, battles) along the top.
        /// </summary>
        public Lane Lane
        {
            get
            {
                string t = FaceType(BackUp ? 1 : 0);
                bool Has(string w) => t.Contains(w, StringComparison.OrdinalIgnoreCase);
                if (Has("Land") && !Has("Creature")) return Lane.Land;
                if ((Has("Artifact") || Has("Enchantment")) && !Has("Creature")) return Lane.Support;
                return Lane.Top;
            }
        }

        /// <summary>A creature (the face that's up).</summary>
        public bool IsCreatureNow => FaceType(BackUp ? 1 : 0).Contains("Creature", StringComparison.OrdinalIgnoreCase);

        /// <summary>Toughness now, with +1/+1 and -1/-1 counters and until-end-of-turn changes (null when it has none printed, like */*).</summary>
        public int? ToughnessNow => Printed(Card.Toughness) is int n ? n + CounterBoost + ToughnessMod : null;

        /// <summary>Power now, the same way.</summary>
        public int? PowerNow => Printed(Card.Power) is int n ? n + CounterBoost + PowerMod : null;

        /// <summary>The printed number on the face that's up (null for * or none).</summary>
        private int? Printed(string value)
        {
            var t = CardFaces.Sides(value);
            int s = BackUp ? 1 : 0;
            string v = s < t.Length ? t[s] : t[0];
            return int.TryParse(v, out int n) ? n : null;
        }

        private int CounterBoost => Counters.GetValueOrDefault("+1/+1") - Counters.GetValueOrDefault("-1/-1");

        /// <summary>Power and toughness differ from what's printed (counters or until-end-of-turn changes).</summary>
        public bool PowerToughnessChanged => IsCreatureNow && (CounterBoost != 0 || PowerMod != 0 || ToughnessMod != 0);

        /// <summary>
        /// The counters it enters the battlefield with, from the face that's up:
        /// ("Loyalty", 4) for a planeswalker, ("Defense", 5) for a battle; null for anything else.
        /// </summary>
        public (string Kind, int Count)? EntersWith
        {
            get
            {
                string t = FaceType(BackUp ? 1 : 0);
                string kind = t.Contains("Planeswalker", StringComparison.OrdinalIgnoreCase) ? "Loyalty"
                            : t.Contains("Battle", StringComparison.OrdinalIgnoreCase) ? "Defense" : "";
                if (kind.Length == 0) return null;
                var sides = CardFaces.Sides(Loyalty);
                int s = BackUp ? 1 : 0;
                string v = s < sides.Length ? sides[s] : sides[0];
                if ((!int.TryParse(v, out int n) || n <= 0) && sides.Length > 1)
                    int.TryParse(sides.FirstOrDefault(x => int.TryParse(x, out _)) ?? "", out n);   // a one-sided value printed for the other face
                return n > 0 ? (kind, n) : null;
            }
        }

        public GameCard Clone() => new()
        {
            Id = Id, Card = Card, Layout = Layout, Seat = Seat, IsToken = IsToken, IsCommander = IsCommander, Loyalty = Loyalty,
            PowerMod = PowerMod, ToughnessMod = ToughnessMod, EnteredTurn = EnteredTurn,
            TempKeywords = new List<string>(TempKeywords), LoyaltyUsedTurn = LoyaltyUsedTurn,
            Tapped = Tapped, BackUp = BackUp, FaceDown = FaceDown, Flipped = Flipped, Damage = Damage, StackOrder = StackOrder,
            AttachedTo = AttachedTo, MergedInto = MergedInto, PendingTarget = PendingTarget, PendingMutate = PendingMutate, PendingOnTop = PendingOnTop,
            Counters = new Dictionary<string, int>(Counters, StringComparer.OrdinalIgnoreCase),
            X = X, Y = Y,
        };

        public override string ToString() => ShownName;
    }
}
