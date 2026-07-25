using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Shared service for collection operations.
    /// Used by CollectionViewModel (app) and eventually by Agent.
    /// Handles add/remove/update for all collection types + "used in" lookups.
    /// </summary>
    public class CollectionService
    {
        // ══════════════════════════════════════════════════════════════════
        // OWNERSHIP LOOKUP — is this ScryfallId in my collection?
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a HashSet of all ScryfallIds the user owns (main collection).
        /// Used to color-code pool rows: green = owned, default = not owned.
        /// </summary>
        public static HashSet<string> GetOwnedScryfallIds()
        {
            try
            {
                using var db = new CollectionDbContext();
                return db.CollectionEntries
                    .Where(c => !string.IsNullOrEmpty(c.ScryfallId))
                    .Select(c => c.ScryfallId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ADD TO COLLECTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds a card from the pool to the main collection.
        /// If the card already exists (by ScryfallId), increments quantity.
        /// </summary>
        public static CollectionEntry AddToCollection(PoolCard poolCard, bool isFoil = false)
        {
            using var db = new CollectionDbContext();

            var existing = db.CollectionEntries
                .FirstOrDefault(c => c.ScryfallId == poolCard.ScryfallId);

            if (existing != null)
            {
                if (isFoil)
                    existing.FoilQuantity++;
                else
                    existing.Quantity++;
                existing.DateModified = DateTime.Now;
                db.SaveChanges();
                return existing;
            }

            var entry = new CollectionEntry
            {
                ScryfallId = poolCard.ScryfallId,
                OracleId = poolCard.OracleId,
                PoolId = poolCard.PoolId,
                Name = poolCard.Name,
                ManaCost = poolCard.ManaCost,
                ManaValue = poolCard.ManaValue,
                TypeLine = poolCard.TypeLine,
                OracleText = poolCard.OracleText,
                FlavorText = poolCard.FlavorText,
                Power = poolCard.Power,
                Toughness = poolCard.Toughness,
                Colors = poolCard.Colors,
                ColorIdentity = poolCard.ColorIdentity,
                SetCode = poolCard.SetCode,
                SetName = poolCard.SetName,
                SetType = poolCard.SetType,
                CollectorNumber = poolCard.CollectorNumber,
                Rarity = poolCard.Rarity,
                Artist = poolCard.Artist,
                ImageSmallUrl = poolCard.ImageSmallUrl,
                ImageNormalUrl = poolCard.ImageNormalUrl,
                ImageBackUrl = poolCard.ImageBackUrl ?? string.Empty,
                LocalImagePath = poolCard.LocalImagePath,
                LocalImageBackPath = poolCard.LocalImageBackPath ?? string.Empty,
                Layout = poolCard.Layout,
                IsFoilAvailable = poolCard.IsFoil,
                IsNonFoilAvailable = poolCard.IsNonFoil,
                ReleasedAt = poolCard.ReleasedAt,
                Keywords = poolCard.Keywords ?? string.Empty,
                PriceUsd = poolCard.PriceUsd,
                PriceUsdFoil = poolCard.PriceUsdFoil,
                PriceUsdEtched = poolCard.PriceUsdEtched,
                PriceEur = poolCard.PriceEur,
                PriceEurFoil = poolCard.PriceEurFoil,
                PriceTix = poolCard.PriceTix,
                Quantity = isFoil ? 0 : 1,
                FoilQuantity = isFoil ? 1 : 0,
                Condition = "Near Mint",
                Language = "English",
                DateAdded = DateTime.Now,
                DateModified = DateTime.Now
            };

            db.CollectionEntries.Add(entry);
            db.SaveChanges();
            return entry;
        }

        // ══════════════════════════════════════════════════════════════════
        // REMOVE FROM COLLECTION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Decrements quantity (or foil quantity) for a collection entry.
        /// If both quantities hit 0, removes the entry entirely.
        /// Returns true if the entry was fully removed.
        /// </summary>
        public static bool RemoveFromCollection(int collectionEntryId, bool isFoil = false)
        {
            using var db = new CollectionDbContext();

            var entry = db.CollectionEntries.Find(collectionEntryId);
            if (entry == null) return false;

            if (isFoil)
                entry.FoilQuantity = Math.Max(0, entry.FoilQuantity - 1);
            else
                entry.Quantity = Math.Max(0, entry.Quantity - 1);

            entry.DateModified = DateTime.Now;

            if (entry.Quantity <= 0 && entry.FoilQuantity <= 0)
            {
                db.CollectionEntries.Remove(entry);
                db.SaveChanges();
                return true;
            }

            db.SaveChanges();
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // ADD TO TRADE BINDER
        // ══════════════════════════════════════════════════════════════════

        public static TradeBinderEntry AddToTradeBinder(PoolCard poolCard, bool isFoil = false)
        {
            using var db = new CollectionDbContext();

            var entry = new TradeBinderEntry
            {
                ScryfallId = poolCard.ScryfallId,
                PoolId = poolCard.PoolId,
                Name = poolCard.Name,
                SetCode = poolCard.SetCode,
                SetName = poolCard.SetName,
                CollectorNumber = poolCard.CollectorNumber,
                TypeLine = poolCard.TypeLine,
                OracleText = poolCard.OracleText,
                FlavorText = poolCard.FlavorText,
                ManaCost = poolCard.ManaCost,
                ManaValue = poolCard.ManaValue,
                ColorIdentity = poolCard.ColorIdentity,
                Colors = poolCard.Colors,
                Rarity = poolCard.Rarity,
                Artist = poolCard.Artist,
                Power = poolCard.Power,
                Toughness = poolCard.Toughness,
                IsFoilAvailable = poolCard.IsFoil,
                IsNonFoilAvailable = poolCard.IsNonFoil,
                PriceUsd = poolCard.PriceUsd,
                PriceUsdFoil = poolCard.PriceUsdFoil,
                ImageNormalUrl = poolCard.ImageNormalUrl,
                LocalImagePath = poolCard.LocalImagePath,
                Quantity = 1,
                IsFoil = isFoil,
                Condition = "Near Mint",
                DateAdded = DateTime.Now
            };

            db.TradeBinderEntries.Add(entry);
            db.SaveChanges();
            return entry;
        }

        // ══════════════════════════════════════════════════════════════════
        // ADD TO WANT LIST
        // ══════════════════════════════════════════════════════════════════

        public static WantListEntry AddToWantList(PoolCard poolCard, bool isFoil = false)
        {
            using var db = new CollectionDbContext();

            var entry = new WantListEntry
            {
                ScryfallId = poolCard.ScryfallId,
                PoolId = poolCard.PoolId,
                Name = poolCard.Name,
                SetCode = poolCard.SetCode,
                SetName = poolCard.SetName,
                CollectorNumber = poolCard.CollectorNumber,
                TypeLine = poolCard.TypeLine,
                OracleText = poolCard.OracleText,
                FlavorText = poolCard.FlavorText,
                ManaCost = poolCard.ManaCost,
                ManaValue = poolCard.ManaValue,
                ColorIdentity = poolCard.ColorIdentity,
                Colors = poolCard.Colors,
                Rarity = poolCard.Rarity,
                Artist = poolCard.Artist,
                Power = poolCard.Power,
                Toughness = poolCard.Toughness,
                IsFoilAvailable = poolCard.IsFoil,
                IsNonFoilAvailable = poolCard.IsNonFoil,
                PriceUsd = poolCard.PriceUsd,
                PriceUsdFoil = poolCard.PriceUsdFoil,
                ImageNormalUrl = poolCard.ImageNormalUrl,
                LocalImagePath = poolCard.LocalImagePath,
                Quantity = 1,
                IsFoil = isFoil,
                DateAdded = DateTime.Now
            };

            db.WantListEntries.Add(entry);
            db.SaveChanges();
            return entry;
        }

        // ══════════════════════════════════════════════════════════════════
        // DECK USAGE — which decks (and Trade Binder) use this card?
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets all usage rows for a card by ScryfallId.
        /// Includes decks AND Trade Binder entries (Trade Binder = "used").
        /// </summary>
        public static List<DeckUsageRow> GetDeckUsage(string scryfallId, string cardName)
        {
            var usage = new List<DeckUsageRow>();

            if (string.IsNullOrEmpty(scryfallId) && string.IsNullOrEmpty(cardName))
                return usage;

            // Check decks
            try
            {
                string decksFolder = AppFolderService.DecksFolder;
                if (Directory.Exists(decksFolder))
                {
                    var deckFiles = Directory.GetFiles(decksFolder, "*.deck",
                        SearchOption.AllDirectories);

                    foreach (var deckFile in deckFiles)
                    {
                        var deck = DeckService.Load(deckFile);
                        if (deck == null) continue;

                        var matches = deck.Cards.Where(c =>
                            // Primary: ScryfallId match (stable across rebuilds)
                            (!string.IsNullOrEmpty(scryfallId) &&
                             !string.IsNullOrEmpty(c.ScryfallId) &&
                             c.ScryfallId.Equals(scryfallId, StringComparison.OrdinalIgnoreCase)) ||
                            // Fallback: name match only when ScryfallId missing
                            (string.IsNullOrEmpty(c.ScryfallId) &&
                             c.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase)));

                        foreach (var match in matches)
                        {
                            usage.Add(new DeckUsageRow
                            {
                                DeckName = deck.Name ?? Path.GetFileNameWithoutExtension(deckFile),
                                DeckType = deck.DeckType.ToString(),
                                Quantity = match.Quantity,
                                Category = match.Category.ToString(),
                                IsFoil = match.IsFoil ? "Yes" : "No"
                            });
                        }
                    }
                }
            }
            catch { }

            // Check Trade Binder (Trade Binder = "used", cards are committed)
            try
            {
                using var db = new CollectionDbContext();
                var tradeMatches = db.TradeBinderEntries
                    .Where(t =>
                        (!string.IsNullOrEmpty(scryfallId) &&
                         t.ScryfallId == scryfallId) ||
                        (string.IsNullOrEmpty(scryfallId) &&
                         t.Name == cardName))
                    .ToList();

                foreach (var trade in tradeMatches)
                {
                    usage.Add(new DeckUsageRow
                    {
                        DeckName = "Trade Binder",
                        DeckType = "Trade",
                        Quantity = trade.Quantity,
                        Category = "—",
                        IsFoil = trade.IsFoil ? "Yes" : "No"
                    });
                }
            }
            catch { }

            return usage;
        }

        /// <summary>
        /// Calculates total used count (decks + Trade Binder) for a card.
        /// </summary>
        public static int GetUsedCount(string scryfallId, string cardName)
        {
            return GetDeckUsage(scryfallId, cardName).Sum(u => u.Quantity);
        }
    }
}