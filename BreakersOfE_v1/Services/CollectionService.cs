using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Owns the per-finish collection row model (Phase 2). A collection row is
    /// identified by ScryfallId + Finish (nonfoil / foil / etched). Adding a
    /// finish finds that finish's row and increments it, or creates it if it
    /// doesn't exist yet.
    ///
    /// This is the SINGLE place row create-or-update happens, so the logic can't
    /// drift across the many add gestures (Pool→Collection for main + special
    /// types, and Deck→Collection). Robust to interleaving (non-foil, then foil,
    /// then non-foil again all land on the correct rows) because identity is
    /// purely ScryfallId + Finish — never order-dependent.
    /// </summary>
    public static class CollectionService
    {
        /// <summary>
        /// Adds <paramref name="qty"/> copies of a card at the given finish to
        /// the collection. If a row for that ScryfallId + finish exists, its
        /// quantity is incremented; otherwise a new row is created from the
        /// supplied <paramref name="template"/> (which carries all embedded card
        /// data and the per-finish price already set by the caller).
        ///
        /// Returns the resulting entry's CollectionEntryId.
        /// </summary>
        public static int UpsertEntry(string scryfallId, string finish, int qty,
            CollectionEntry template)
        {
            finish = CardFinish.Normalize(finish);
            if (qty <= 0) qty = 1;

            using var db = new CollectionDbContext();

            // Find the row for THIS finish of THIS card. ScryfallId is the
            // stable key; Finish disambiguates nonfoil/foil/etched.
            CollectionEntry? existing = null;
            if (!string.IsNullOrWhiteSpace(scryfallId))
            {
                existing = db.CollectionEntries.FirstOrDefault(
                    c => c.ScryfallId == scryfallId && c.Finish == finish);
            }
            else if (template.PoolId > 0)
            {
                // Fallback for very old data with no ScryfallId.
                existing = db.CollectionEntries.FirstOrDefault(
                    c => c.PoolId == template.PoolId && c.Finish == finish);
            }

            if (existing != null)
            {
                existing.Quantity += qty;
                existing.DateModified = DateTime.Now;
                db.SaveChanges();
                return existing.CollectionEntryId;
            }

            // Create a new per-finish row from the template.
            template.Finish = finish;
            template.Quantity = qty;
            template.FoilQuantity = 0;        // legacy column unused per-finish
            template.DateAdded = template.DateAdded == default
                ? DateTime.Now : template.DateAdded;
            template.DateModified = DateTime.Now;

            db.CollectionEntries.Add(template);
            db.SaveChanges();
            return template.CollectionEntryId;
        }

        /// <summary>
        /// Picks the correct Scryfall price for a finish from a pool card's
        /// per-finish prices. Used when creating/refreshing a per-finish row.
        /// </summary>
        public static decimal? PriceForFinish(string finish,
            decimal? nonFoil, decimal? foil, decimal? etched)
        {
            return CardFinish.Normalize(finish) switch
            {
                CardFinish.Foil => foil,
                CardFinish.Etched => etched,
                _ => nonFoil
            };
        }
    }
}