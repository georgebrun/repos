using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// One line of a deck: a printing in one part of the deck (command zone,
    /// main deck or sideboard). The same printing can be a line in each.
    /// </summary>
    public readonly record struct DeckLineKey(string ScryfallId, DeckCardCategory Section)
    {
        public static DeckLineKey Of(DeckCard c) => new(c.ScryfallId ?? "", DeckEditService.SectionOf(c));
    }

    /// <summary>What a deck edit did (for the status line and re-selecting rows).</summary>
    public sealed class DeckEditResult
    {
        /// <summary>Copies (or lines) changed; 0 = nothing happened.</summary>
        public int Changed { get; init; }
        public string Message { get; init; } = "";
        /// <summary>Something was refused or only partly done.</summary>
        public bool Warning { get; init; }
        /// <summary>The lines the edit ended in (selected afterwards).</summary>
        public List<DeckLineKey> Touched { get; init; } = new();

        public static DeckEditResult Refused(string message) => new() { Message = message, Warning = true };
    }

    /// <summary>
    /// Edit → Decks: every change to a deck's card list, in memory. The page
    /// saves the deck afterwards (autosave) and keeps the file as it was
    /// before for Undo. Deck files stay pure card lists — nothing here
    /// touches the collection (claims are CollectionEditService's job).
    ///
    /// Counts are per finish (Non-Foil, Foil, Etched) on each line.
    /// </summary>
    public static class DeckEditService
    {
        /// <summary>Which part of the deck a line is in (a v1 IsCommander flag counts as the command zone).</summary>
        public static DeckCardCategory SectionOf(DeckCard c) =>
            DeckRulesService.IsLeaderCard(c) ? DeckCardCategory.Commander : c.Category;

        public static string SectionName(DeckCardCategory s) => s switch
        {
            DeckCardCategory.Commander => "command zone",
            DeckCardCategory.Sideboard => "sideboard",
            DeckCardCategory.Tokens => "tokens",
            _ => "main deck",
        };

        /// <summary>A line by key. Lines without a ScryfallId (very old files) can't be told apart: never found.</summary>
        public static DeckCard? FindLine(Deck deck, DeckLineKey key) =>
            string.IsNullOrEmpty(key.ScryfallId) ? null : deck.Cards.FirstOrDefault(c => DeckLineKey.Of(c) == key);

        /// <summary>"Lightning Bolt (M10 #146)".</summary>
        public static string CardText(DeckCard c) =>
            $"{c.Name} ({c.SetCode.ToUpperInvariant()} #{c.CollectorNumber})";

        private static string Copies(int n, string finish) =>
            $"{n} {CardFinish.Display(finish)} {(n == 1 ? "copy" : "copies")}";

        /// <summary>Does this printing come in this finish?</summary>
        public static bool HasFinish(DeckCard c, string finish) => finish switch
        {
            CardFinish.Foil => c.IsFoil,
            CardFinish.Etched => c.IsEtched,
            _ => c.IsNonFoil,
        };

        // ── Add ─────────────────────────────────────────────────────────
        /// <summary>
        /// Add <paramref name="qty"/> copies of a printing (<paramref name="card"/>
        /// is a template, e.g. from the pool) to one part of the deck. Joins
        /// the existing line of that printing there, or starts a new one.
        /// </summary>
        public static DeckEditResult Add(Deck deck, DeckCard card, string finish, int qty, DeckCardCategory section)
        {
            if (qty <= 0) return DeckEditResult.Refused("Nothing to add.");
            if (!HasFinish(card, finish))
                return DeckEditResult.Refused($"{CardText(card)} doesn't come in {CardFinish.Display(finish)}.");

            var key = new DeckLineKey(card.ScryfallId, section);
            var line = FindLine(deck, key);
            if (line == null)
            {
                line = DeckService.CloneCard(card);
                CopyExtras(card, line);
                line.Category = section;
                line.IsCommander = section == DeckCardCategory.Commander;
                line.IsToken = section == DeckCardCategory.Tokens;
                deck.Cards.Add(line);
            }
            line.SetCount(finish, line.CountOf(finish) + qty);
            deck.IsModified = true;
            return new DeckEditResult
            {
                Changed = qty,
                Message = $"Added {Copies(qty, finish)} of {CardText(card)} to the {SectionName(section)}.",
                Touched = { key },
            };
        }

        // ── Remove ──────────────────────────────────────────────────────
        /// <summary>Remove up to <paramref name="qty"/> copies of one finish from a line (the line goes at 0).</summary>
        public static DeckEditResult Remove(Deck deck, DeckLineKey key, string finish, int qty)
        {
            var line = FindLine(deck, key);
            if (line == null) return DeckEditResult.Refused("That card isn't in this part of the deck.");
            int have = line.CountOf(finish);
            if (have == 0)
                return DeckEditResult.Refused($"{CardText(line)}: no {CardFinish.Display(finish)} copies in the {SectionName(key.Section)}.");

            int take = Math.Min(have, qty);
            line.SetCount(finish, have - take);
            bool gone = line.TotalQuantity <= 0;
            if (gone) deck.Cards.Remove(line);
            deck.IsModified = true;
            string msg = $"Removed {Copies(take, finish)} of {CardText(line)} from the {SectionName(key.Section)}" +
                         (take < qty ? $" (only {take} there)." : ".");
            return new DeckEditResult
            {
                Changed = take,
                Message = msg,
                Warning = take < qty,
                Touched = gone ? new List<DeckLineKey>() : new List<DeckLineKey> { key },
            };
        }

        /// <summary>Remove a whole line (every finish).</summary>
        public static DeckEditResult RemoveLine(Deck deck, DeckLineKey key)
        {
            var line = FindLine(deck, key);
            if (line == null) return DeckEditResult.Refused("That card isn't in this part of the deck.");
            int n = line.TotalQuantity;
            deck.Cards.Remove(line);
            deck.IsModified = true;
            return new DeckEditResult
            {
                Changed = Math.Max(1, n),
                Message = $"Removed {CardText(line)} ({n} {(n == 1 ? "copy" : "copies")}) from the {SectionName(key.Section)}.",
            };
        }

        /// <summary>Set one finish's copies on a line to exactly <paramref name="n"/> (0 on every finish removes the line).</summary>
        public static DeckEditResult SetCount(Deck deck, DeckLineKey key, string finish, int n)
        {
            var line = FindLine(deck, key);
            if (line == null) return DeckEditResult.Refused("That card isn't in this part of the deck.");
            n = Math.Max(0, n);
            int was = line.CountOf(finish);
            if (was == n) return new DeckEditResult { Message = "No change." };
            if (n > 0 && !HasFinish(line, finish))
                return DeckEditResult.Refused($"{CardText(line)} doesn't come in {CardFinish.Display(finish)}.");

            line.SetCount(finish, n);
            bool gone = line.TotalQuantity <= 0;
            if (gone) deck.Cards.Remove(line);
            deck.IsModified = true;
            return new DeckEditResult
            {
                Changed = Math.Abs(n - was),
                Message = $"{CardText(line)}: {CardFinish.Display(finish)} {was} → {n}" +
                          (gone ? " (removed from the deck)." : "."),
                Touched = gone ? new List<DeckLineKey>() : new List<DeckLineKey> { key },
            };
        }

        // ── Command zone and sideboard ──────────────────────────────────
        /// <summary>
        /// Put one copy of a line in the command zone (commander, Oathbreaker
        /// or Signature Spell). The rest of the line stays where it was.
        /// </summary>
        public static DeckEditResult MakeLeader(Deck deck, DeckLineKey key, string leaderName)
        {
            var line = FindLine(deck, key);
            if (line == null) return DeckEditResult.Refused("That card isn't in the deck.");
            if (key.Section == DeckCardCategory.Commander)
                return new DeckEditResult { Message = $"{CardText(line)} is already the {leaderName}." };

            var leaderKey = new DeckLineKey(key.ScryfallId, DeckCardCategory.Commander);
            if (FindLine(deck, leaderKey) != null)
                return new DeckEditResult { Message = $"{CardText(line)} is already the {leaderName}." };

            // One copy moves: the finish it has most of.
            string finish = new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched }
                .OrderByDescending(line.CountOf).First();
            if (line.TotalQuantity == 1)
            {
                line.Category = DeckCardCategory.Commander;
                line.IsCommander = true;
            }
            else
            {
                line.SetCount(finish, line.CountOf(finish) - 1);
                var leader = DeckService.CloneCard(line);
                CopyExtras(line, leader);
                leader.Category = DeckCardCategory.Commander;
                leader.IsCommander = true;
                leader.SetCount(finish, 1);
                deck.Cards.Add(leader);
            }
            deck.IsModified = true;
            return new DeckEditResult
            {
                Changed = 1,
                Message = $"{CardText(line)} is now the {leaderName}.",
                Touched = { leaderKey },
            };
        }

        /// <summary>Move a whole line to another part of the deck (joins that part's line of the same printing).</summary>
        public static DeckEditResult MoveLine(Deck deck, DeckLineKey key, DeckCardCategory to)
        {
            var line = FindLine(deck, key);
            if (line == null) return DeckEditResult.Refused("That card isn't in the deck.");
            if (key.Section == to) return new DeckEditResult { Message = $"{CardText(line)} is already in the {SectionName(to)}." };

            var toKey = new DeckLineKey(key.ScryfallId, to);
            var target = FindLine(deck, toKey);
            if (target == null)
            {
                line.Category = to;
                line.IsCommander = to == DeckCardCategory.Commander;
            }
            else
            {
                foreach (var f in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
                    target.SetCount(f, target.CountOf(f) + line.CountOf(f));
                deck.Cards.Remove(line);
            }
            deck.IsModified = true;
            string what = key.Section == DeckCardCategory.Commander ? "no longer in the command zone — moved to the " : "moved to the ";
            return new DeckEditResult
            {
                Changed = 1,
                Message = $"{CardText(line)} {what}{SectionName(to)}.",
                Touched = { toKey },
            };
        }

        /// <summary>Fields CloneCard leaves out (legality, etched, …).</summary>
        private static void CopyExtras(DeckCard from, DeckCard to)
        {
            to.LegalitiesJson = from.LegalitiesJson;
            to.FlavorText = from.FlavorText;
            to.IsEtched = from.IsEtched;
            to.PriceUsdEtched = from.PriceUsdEtched;
            to.IsGameChanger = from.IsGameChanger;
            to.DeckFormat = from.DeckFormat;
        }
    }
}
