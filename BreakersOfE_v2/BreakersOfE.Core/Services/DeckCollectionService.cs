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
    /// "Missing" is counted by EXACT PRINTING (ScryfallId): only free copies
    /// of the printing the deck lists count. Other printings never fill in —
    /// this is a collection with monetary value (an LEA Sol Ring is not a
    /// stand-in for an FRC one).
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
                c.ClaimedCount = 0;
            }
            if (deck.Cards.Count == 0) return;

            Dictionary<string, int> ownedById;          // ScryfallId → copies owned (all finishes)
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

                // The deck's tokens: owned from the token collection (same printing rule).
                var tokenIds = deck.Cards.Where(c => c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId))
                    .Select(c => c.ScryfallId).Distinct().ToList();
                if (tokenIds.Count > 0)
                    foreach (var t in db.TokenCollectionEntries
                                 .Where(e => e.Quantity > 0 && tokenIds.Contains(e.ScryfallId))
                                 .Select(e => new { e.ScryfallId, e.Quantity }).ToList())
                        ownedById[t.ScryfallId] = ownedById.GetValueOrDefault(t.ScryfallId) + t.Quantity;

                var ids = ownedById.Keys.ToList();
                string thisDeck = deck.DeckId ?? string.Empty;
                usedElsewhereById = db.DeckUsages
                    .Where(u => ids.Contains(u.ScryfallId) && u.DeckId != thisDeck)
                    .Select(u => new { u.ScryfallId, Used = u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched })
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

            // Copies claimed from the collection for THIS deck (Edit → Decks), per printing.
            Dictionary<string, int> claimedHere;
            try
            {
                using var cdb = new CollectionDbContext();
                string id = deck.DeckId ?? string.Empty;
                claimedHere = id.Length == 0 ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : cdb.DeckUsages.Where(u => u.DeckId == id)
                         .Select(u => new { u.ScryfallId, N = u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched })
                         .ToList()
                         .GroupBy(u => u.ScryfallId, StringComparer.OrdinalIgnoreCase)
                         .ToDictionary(g => g.Key, g => g.Sum(u => u.N), StringComparer.OrdinalIgnoreCase);
            }
            catch { claimedHere = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }

            // Shared out over the lines of that printing (command zone, main, sideboard, tokens).
            foreach (var group in deck.Cards.GroupBy(c => c.ScryfallId ?? "", StringComparer.OrdinalIgnoreCase))
            {
                int left = claimedHere.GetValueOrDefault(group.Key);
                foreach (var line in group.OrderBy(c => c.Category))
                {
                    int take = Math.Min(line.TotalQuantity, left);
                    line.ClaimedCount = take;
                    left -= take;
                }
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

            // Per printing: lines that list the same printing share its free copies.
            foreach (var group in deck.Cards.GroupBy(c => c.ScryfallId ?? "", StringComparer.OrdinalIgnoreCase))
            {
                int freeLeft = FreeOf(group.Key);
                foreach (var line in group)
                {
                    int need = line.TotalQuantity;
                    int take = Math.Min(need, freeLeft);
                    freeLeft -= take;
                    line.CollectionMissing = need - take;
                }
            }
        }
    }
}
