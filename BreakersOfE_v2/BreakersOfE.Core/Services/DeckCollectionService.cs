using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Deck vs. collection (read-only): for each card in a deck, how many
    /// copies you own, how many are free (not used by your OTHER decks or the
    /// Trade Binder), how many you'd still need, and how many are already on
    /// the Want List.
    ///
    /// "Missing" is counted by card name: free copies of other printings of
    /// the same card fill in before a copy counts as missing (any printing
    /// plays the same). Exact printings are used first.
    /// </summary>
    public static class DeckCollectionService
    {
        /// <summary>Fill CollectionOwned / Free / Missing and WantedCount on every deck card.</summary>
        public static void Annotate(Deck deck)
        {
            foreach (var c in deck.Cards)
            {
                c.CollectionOwned = 0;
                c.CollectionFree = 0;
                c.CollectionMissing = c.TotalQuantity;
                c.WantedCount = 0;
            }
            if (deck.Cards.Count == 0) return;

            Dictionary<string, int> ownedById;          // ScryfallId → copies owned (all finishes)
            Dictionary<string, string> nameById;        // ScryfallId → card name
            Dictionary<string, int> usedElsewhereById;  // ScryfallId → copies entered for OTHER decks / binder
            Dictionary<string, int> wantedByName;       // card name → copies on the Want List (any printing)

            try
            {
                using var db = new CollectionDbContext();

                var names = deck.Cards.Select(c => c.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // Every printing of the deck's card names that you own.
                var owned = db.CollectionEntries
                    .Where(e => e.Quantity > 0 && names.Contains(e.Name))
                    .Select(e => new { e.ScryfallId, e.Name, e.Quantity })
                    .ToList();

                ownedById = owned.GroupBy(e => e.ScryfallId, StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.OrdinalIgnoreCase);
                nameById = owned.GroupBy(e => e.ScryfallId, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

                var ids = ownedById.Keys.ToList();
                string thisDeck = deck.DeckId ?? string.Empty;
                usedElsewhereById = db.DeckUsages
                    .Where(u => ids.Contains(u.ScryfallId) && u.DeckId != thisDeck)
                    .Select(u => new { u.ScryfallId, Used = u.EnteredNonFoil + u.EnteredFoil })
                    .ToList()
                    .GroupBy(u => u.ScryfallId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(u => u.Used), StringComparer.OrdinalIgnoreCase);

                // Want List: separate from what you own; only shown alongside.
                wantedByName = db.WantListEntries
                    .Where(w => names.Contains(w.Name))
                    .Select(w => new { w.Name, w.Quantity })
                    .ToList()
                    .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(w => w.Quantity), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // No collection (or unreadable): everything counts as missing.
                System.Diagnostics.Debug.WriteLine($"Deck vs collection: {ex.Message}");
                return;
            }

            int FreeOf(string sid) =>
                Math.Max(0, ownedById.GetValueOrDefault(sid) - usedElsewhereById.GetValueOrDefault(sid));

            // Per card line: this exact printing.
            foreach (var c in deck.Cards)
            {
                c.CollectionOwned = ownedById.GetValueOrDefault(c.ScryfallId);
                c.CollectionFree = FreeOf(c.ScryfallId);
                c.WantedCount = wantedByName.GetValueOrDefault(c.Name);
            }

            // Per card name: exact printings first, then other free printings fill the gaps.
            foreach (var group in deck.Cards.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var lines = group.ToList();

                // Free copies per printing, shared by the lines using that printing.
                var freeLeft = nameById
                    .Where(kv => string.Equals(kv.Value, group.Key, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => FreeOf(kv.Key), StringComparer.OrdinalIgnoreCase);

                var short_ = new Dictionary<DeckCard, int>();
                foreach (var line in lines)
                {
                    int need = line.TotalQuantity;
                    int take = Math.Min(need, freeLeft.GetValueOrDefault(line.ScryfallId));
                    if (take > 0) freeLeft[line.ScryfallId] -= take;
                    short_[line] = need - take;
                }

                // Remaining free copies of any printing of this name.
                int spare = freeLeft.Values.Sum();
                foreach (var line in lines)
                {
                    int fill = Math.Min(short_[line], spare);
                    spare -= fill;
                    line.CollectionMissing = short_[line] - fill;
                }
            }
        }
    }
}