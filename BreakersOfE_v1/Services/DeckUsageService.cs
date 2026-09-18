using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Records which decks use which collection cards, in the collection
    /// database (DeckUsages table) — replacing the old approach of scanning
    /// deck files on disk.
    ///
    /// Design principles:
    ///   • Keyed on ScryfallId (card) + DeckId (deck GUID). NEVER PoolId —
    ///     PoolId is a local pool rowid that changes on pool rebuild.
    ///   • The deck's current card list is the source of truth. SyncDeck()
    ///     rebuilds that deck's usage rows from its contents, so it's
    ///     idempotent and self-correcting — call it after any deck change or
    ///     on load.
    ///   • Trade Binder is tracked as a pseudo-deck (DeckId = TRADE_BINDER_ID)
    ///     so binder-committed cards count toward Used and appear in the
    ///     nested "Used in Decks" table alongside real decks.
    ///     after entering a deck's cards into the collection.
    /// </summary>
    public static class DeckUsageService
    {
        /// <summary>Pseudo-deck ID for Trade Binder usage tracking.</summary>
        public const string TRADE_BINDER_ID = "__TRADE_BINDER__";
        public const string TRADE_BINDER_NAME = "Trade Binder";

        /// <summary>
        /// Upsert a DeckUsage row for the Trade Binder. Creates or increments
        /// the binder's pseudo-deck row for this card + finish.
        /// </summary>
        public static void IncrementBinderUsage(string scryfallId, bool foil, int qty = 1)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return;
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == TRADE_BINDER_ID && u.ScryfallId == scryfallId);
            if (row == null)
            {
                row = new DeckUsage
                {
                    ScryfallId = scryfallId,
                    DeckId = TRADE_BINDER_ID,
                    DeckName = TRADE_BINDER_NAME,
                    DeckType = "Binder",
                    DateRecorded = DateTime.Now
                };
                db.DeckUsages.Add(row);
            }
            if (foil)
            {
                row.FoilQuantity += qty;
                row.EnteredFoil += qty;
            }
            else
            {
                row.Quantity += qty;
                row.EnteredNonFoil += qty;
            }
            db.SaveChanges();
        }

        /// <summary>
        /// Decrement (or remove) binder usage for this card + finish.
        /// </summary>
        public static void DecrementBinderUsage(string scryfallId, bool foil, int qty = 1)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return;
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == TRADE_BINDER_ID && u.ScryfallId == scryfallId);
            if (row == null) return;
            if (foil)
            {
                row.FoilQuantity = Math.Max(0, row.FoilQuantity - qty);
                row.EnteredFoil = Math.Max(0, row.EnteredFoil - qty);
            }
            else
            {
                row.Quantity = Math.Max(0, row.Quantity - qty);
                row.EnteredNonFoil = Math.Max(0, row.EnteredNonFoil - qty);
            }
            // Clean up if nothing left
            if (row.Quantity == 0 && row.FoilQuantity == 0)
                db.DeckUsages.Remove(row);
            db.SaveChanges();
        }

        /// <summary>
        /// Remove ALL binder usage rows for a card (used on "Remove All").
        /// </summary>
        public static void RemoveAllBinderUsage(string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return;
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == TRADE_BINDER_ID && u.ScryfallId == scryfallId);
            if (row == null) return;
            db.DeckUsages.Remove(row);
            db.SaveChanges();
        }

        /// <summary>
        /// Rewrites all DeckUsage rows for one deck to match its current
        /// contents. Removes rows for cards no longer in the deck, adds/updates
        /// the rest. Grouped by ScryfallId so multiple categories of the same
        /// card in a deck sum together per finish.
        /// </summary>
        public static void SyncDeck(Deck deck)
        {
            if (deck == null) return;
            string deckId = deck.EnsureDeckId();
            if (string.IsNullOrWhiteSpace(deckId)) return;

            // SyncDeck is only ever called from collection-touching flows
            // (Collection→Deck, Deck→Collection), so this deck is now
            // collection-linked. Persist the flag into the .deck file if it
            // wasn't already set, so reconcile can restore usage if the file is
            // later moved/restored.
            if (!deck.CollectionLinked)
            {
                deck.CollectionLinked = true;
                try
                {
                    if (!string.IsNullOrEmpty(deck.FilePath) &&
                        System.IO.File.Exists(deck.FilePath))
                    {
                        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                        System.IO.File.WriteAllText(deck.FilePath,
                            System.Text.Json.JsonSerializer.Serialize(deck, opts));
                    }
                }
                catch { /* flag will still be set in-memory; persisted on next save */ }
            }

            using var db = new CollectionDbContext();

            // Track every card affected by this sync so we can recompute its
            // UsedCount afterward — both cards still in the deck AND cards that
            // were removed (their old rows are about to be deleted).
            var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Remove existing usage rows for this deck (we rewrite from scratch),
            // but remember each card's per-deck entered counts so they survive
            // the rewrite (SyncDeck runs on every deck edit).
            var existing = db.DeckUsages.Where(u => u.DeckId == deckId).ToList();
            var priorEntered = existing.ToDictionary(
                e => e.ScryfallId,
                e => (e.EnteredNonFoil, e.EnteredFoil),
                StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing) affected.Add(e.ScryfallId);
            if (existing.Count > 0)
                db.DeckUsages.RemoveRange(existing);

            // Build fresh rows from the deck's current cards, grouped by
            // ScryfallId (the stable card key). Cards with no ScryfallId are
            // skipped — they can't be reliably tied to a collection entry.
            var groups = deck.Cards
                .Where(c => !string.IsNullOrWhiteSpace(c.ScryfallId))
                .GroupBy(c => c.ScryfallId);

            string deckTypeLabel = deck.DeckType.ToString();
            var now = DateTime.Now;

            foreach (var g in groups)
            {
                int qty = g.Sum(c => c.Quantity);
                int foilQty = g.Sum(c => c.FoilQuantity);
                if (qty <= 0 && foilQty <= 0) continue;

                // Category: if the card appears in multiple categories, prefer
                // the most "important" one for the label (Commander > Mainboard
                // > Sideboard). Usage totals still sum across all categories.
                string category = PickCategory(g.Select(c => c.CategoryDisplay));

                // Carry forward entered counts, clamped to the new demand (if the
                // deck now holds fewer of a finish, you can't have "entered" more).
                int enteredNf = 0, enteredF = 0;
                if (priorEntered.TryGetValue(g.Key, out var prior))
                {
                    enteredNf = Math.Min(prior.EnteredNonFoil, qty);
                    enteredF = Math.Min(prior.EnteredFoil, foilQty);
                }

                db.DeckUsages.Add(new DeckUsage
                {
                    ScryfallId = g.Key,
                    DeckId = deckId,
                    DeckName = deck.Name,
                    DeckType = deckTypeLabel,
                    Quantity = qty,
                    FoilQuantity = foilQty,
                    EnteredNonFoil = enteredNf,
                    EnteredFoil = enteredF,
                    Category = category,
                    DateRecorded = now
                });
                affected.Add(g.Key);
            }

            db.SaveChanges();

            // Recompute UsedCount for every affected collection entry from the
            // now-current DeckUsage totals. This is what makes UsedCount a
            // faithful, drift-free mirror of actual deck usage across ALL decks.
            RecomputeUsedCounts(db, affected);
        }

        /// <summary>
        /// Recomputes UsedCount on the given collection entries (by ScryfallId)
        /// to equal the sum of their DeckUsage across all decks. Uses the passed
        /// db context so it participates in the same connection.
        /// </summary>
        /// <summary>
        /// Public entry point to recompute the capped, per-finish UsedCount for
        /// one card across all its collection rows. Used by callers outside the
        /// sync path (e.g. Deck→Collection). Used is capped at owned per finish.
        /// </summary>
        public static void RecomputeUsedForCard(string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return;
            using var db = new CollectionDbContext();
            RecomputeUsedCounts(db, new[] { scryfallId });
        }

        private static void RecomputeUsedCounts(
            CollectionDbContext db, IEnumerable<string> scryfallIds)
        {
            bool changed = false;
            foreach (var sid in scryfallIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(sid)) continue;

                // Deck ENTRY for this card, split by finish. Used reflects how
                // many copies you've actually ENTERED into the collection for
                // your decks (summed across all decks), NOT the decks' demand.
                var rows = db.DeckUsages.Where(u => u.ScryfallId == sid)
                    .Select(u => new { u.EnteredNonFoil, u.EnteredFoil }).ToList();
                int nonFoilUsed = rows.Sum(r => r.EnteredNonFoil);
                int foilUsed = rows.Sum(r => r.EnteredFoil);

                // Each collection row is one finish. Used for that row is the
                // deck usage of that finish, but never more than you entered
                // (Used can't exceed what you own).
                foreach (var entry in db.CollectionEntries.Where(c => c.ScryfallId == sid))
                {
                    // Decks track only foil vs non-foil usage. Etched isn't
                    // deck-tracked, so an etched row has no deck usage (0).
                    int used = entry.Finish switch
                    {
                        CardFinish.Foil => foilUsed,
                        CardFinish.Etched => 0,
                        _ => nonFoilUsed
                    };
                    if (used > entry.Quantity) used = entry.Quantity;
                    if (entry.UsedCount != used)
                    {
                        entry.UsedCount = used;
                        changed = true;
                    }
                }
            }
            if (changed) db.SaveChanges();
        }

        /// <summary>ScryfallIds of all cards a deck contributes usage for —
        /// captured before RemoveDeck so their UsedCounts can be recomputed.</summary>
        public static List<string> GetUsageForCard_ScryfallIdsForDeck(string deckId)
        {
            if (string.IsNullOrWhiteSpace(deckId)) return new List<string>();
            using var db = new CollectionDbContext();
            return db.DeckUsages
                .Where(u => u.DeckId == deckId)
                .Select(u => u.ScryfallId)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Removes all usage rows for a deck (e.g. the user chose to clear a
        /// deleted deck's usage during reconcile). Does not touch the deck file.
        /// </summary>
        public static void RemoveDeck(string deckId)
        {
            if (string.IsNullOrWhiteSpace(deckId)) return;
            using var db = new CollectionDbContext();
            var rows = db.DeckUsages.Where(u => u.DeckId == deckId).ToList();
            if (rows.Count == 0) return;
            db.DeckUsages.RemoveRange(rows);
            db.SaveChanges();
        }

        /// <summary>
        /// Refreshes the denormalized DeckName on all of a deck's usage rows
        /// (call after a rename so the nested table shows the new name).
        /// </summary>
        public static void RenameDeck(string deckId, string newName)
        {
            if (string.IsNullOrWhiteSpace(deckId)) return;
            using var db = new CollectionDbContext();
            var rows = db.DeckUsages.Where(u => u.DeckId == deckId).ToList();
            if (rows.Count == 0) return;
            foreach (var r in rows) r.DeckName = newName;
            db.SaveChanges();
        }

        /// <summary>True if this deck has any recorded usage (i.e. it was
        /// entered into the collection at some point). Used to decide whether a
        /// save should re-sync — pool-only decks that were never entered return
        /// false and stay untouched.</summary>
        public static bool HasUsage(string deckId)
        {
            if (string.IsNullOrWhiteSpace(deckId)) return false;
            using var db = new CollectionDbContext();
            return db.DeckUsages.Any(u => u.DeckId == deckId);
        }

        /// <summary>
        /// Committed copies of a card across ALL decks, split by finish.
        /// nonFoil = sum of DeckUsage.Quantity, foil = sum of DeckUsage.FoilQuantity.
        /// This is the authoritative "already used in decks" count per finish —
        /// the physical-inventory guard subtracts these from what's owned.
        /// </summary>
        public static (int nonFoil, int foil) GetCommittedByFinish(string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return (0, 0);
            using var db = new CollectionDbContext();
            var rows = db.DeckUsages
                .Where(u => u.ScryfallId == scryfallId)
                .Select(u => new { u.Quantity, u.FoilQuantity })
                .ToList();
            int nf = rows.Sum(r => r.Quantity);
            int f = rows.Sum(r => r.FoilQuantity);
            return (nf, f);
        }

        /// <summary>
        /// Physical-inventory availability for a card, per finish:
        ///   available = owned − committed-across-all-decks.
        /// Never returns negative. Queries the collection DB for the total
        /// owned per finish (summing across all per-finish rows for this
        /// ScryfallId) so it works correctly with the per-finish model where
        /// each row is one finish.
        /// </summary>
        public static (int nonFoilAvail, int foilAvail) GetAvailableByFinish(
            string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return (0, 0);

            // Query actual owned counts from the collection — each row is one
            // finish, so sum Quantity grouped by Finish.
            int nonFoilOwned = 0, foilOwned = 0;
            using (var cdb = new CollectionDbContext())
            {
                var entries = cdb.CollectionEntries
                    .Where(e => e.ScryfallId == scryfallId)
                    .Select(e => new { e.Finish, e.Quantity })
                    .ToList();
                foreach (var e in entries)
                {
                    if (e.Finish == CardFinish.Foil)
                        foilOwned += e.Quantity;
                    else
                        nonFoilOwned += e.Quantity;
                }
            }

            var (nfUsed, fUsed) = GetCommittedByFinish(scryfallId);
            int nfAvail = Math.Max(0, nonFoilOwned - nfUsed);
            int fAvail = Math.Max(0, foilOwned - fUsed);
            return (nfAvail, fAvail);
        }

        /// <summary>
        /// How many copies of a card+finish have been entered into the
        /// collection FOR A SPECIFIC DECK, and how many that deck holds (its
        /// demand). Returns (entered, demand). Used by the per-deck guard.
        /// </summary>
        public static (int entered, int demand) GetDeckEntry(
            string deckId, string scryfallId, bool foil)
        {
            if (string.IsNullOrWhiteSpace(deckId) || string.IsNullOrWhiteSpace(scryfallId))
                return (0, 0);
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == deckId && u.ScryfallId == scryfallId);
            if (row == null) return (0, 0);
            return foil
                ? (row.EnteredFoil, row.FoilQuantity)
                : (row.EnteredNonFoil, row.Quantity);
        }

        /// <summary>
        /// Records that one more copy of a card+finish has been entered into the
        /// collection for a specific deck. Clamped so entered never exceeds the
        /// deck's demand for that finish.
        /// </summary>
        public static void IncrementDeckEntry(
            string deckId, string scryfallId, bool foil)
        {
            if (string.IsNullOrWhiteSpace(deckId) || string.IsNullOrWhiteSpace(scryfallId))
                return;
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == deckId && u.ScryfallId == scryfallId);
            if (row == null) return;
            if (foil)
                row.EnteredFoil = Math.Min(row.EnteredFoil + 1, row.FoilQuantity);
            else
                row.EnteredNonFoil = Math.Min(row.EnteredNonFoil + 1, row.Quantity);
            db.SaveChanges();
        }

        /// <summary>
        /// Records that one copy of a card+finish has been removed (pulled back
        /// out of what was entered) for a specific deck. Floored at 0. Mirrors
        /// IncrementDeckEntry so entered stays in lockstep with the collection.
        /// </summary>
        public static void DecrementDeckEntry(
            string deckId, string scryfallId, bool foil)
        {
            if (string.IsNullOrWhiteSpace(deckId) || string.IsNullOrWhiteSpace(scryfallId))
                return;
            using var db = new CollectionDbContext();
            var row = db.DeckUsages.FirstOrDefault(
                u => u.DeckId == deckId && u.ScryfallId == scryfallId);
            if (row == null) return;
            if (foil)
                row.EnteredFoil = Math.Max(0, row.EnteredFoil - 1);
            else
                row.EnteredNonFoil = Math.Max(0, row.EnteredNonFoil - 1);
            db.SaveChanges();
        }

        /// <summary>All decks that use a given card, by ScryfallId.</summary>
        public static List<DeckUsage> GetUsageForCard(string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId))
                return new List<DeckUsage>();
            using var db = new CollectionDbContext();
            return db.DeckUsages
                .Where(u => u.ScryfallId == scryfallId)
                .OrderBy(u => u.DeckName)
                .ToList();
        }

        /// <summary>
        /// Total copies (foil + non-foil) of a card used across ALL decks.
        /// This is the correct, derived used-count.
        /// </summary>
        public static int GetTotalUsed(string scryfallId)
        {
            if (string.IsNullOrWhiteSpace(scryfallId)) return 0;
            using var db = new CollectionDbContext();
            return db.DeckUsages
                .Where(u => u.ScryfallId == scryfallId)
                .Sum(u => (int?)(u.Quantity + u.FoilQuantity)) ?? 0;
        }

        /// <summary>Distinct deck ids currently recorded in usage.</summary>
        public static List<(string DeckId, string DeckName)> GetKnownDecks()
        {
            using var db = new CollectionDbContext();
            return db.DeckUsages
                .GroupBy(u => u.DeckId)
                .Select(g => new { g.Key, Name = g.Max(x => x.DeckName) })
                .ToList()
                .Select(x => (x.Key, x.Name ?? string.Empty))
                .ToList();
        }

        private static string PickCategory(IEnumerable<string> categories)
        {
            var set = categories
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();
            if (set.Count == 0) return string.Empty;
            if (set.Any(c => c.Equals("Commander", StringComparison.OrdinalIgnoreCase)))
                return "Commander";
            if (set.Any(c => c.Equals("Mainboard", StringComparison.OrdinalIgnoreCase)))
                return "Mainboard";
            if (set.Any(c => c.Equals("Sideboard", StringComparison.OrdinalIgnoreCase)))
                return "Sideboard";
            return set.First();
        }
    }
}