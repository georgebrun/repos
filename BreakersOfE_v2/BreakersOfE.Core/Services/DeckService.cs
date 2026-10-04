using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BreakersOfE.Services
{
    public static class DeckService
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new() { WriteIndented = true };

        // ════════════════════════════════════════════════════════════════════
        // SAVE
        // ════════════════════════════════════════════════════════════════════
        public static void Save(Deck deck)
        {
            if (string.IsNullOrEmpty(deck.FilePath))
                deck.FilePath = AppFolderService.DeckFilePath(deck.Name);

            // Collection deck usage is keyed on the deck's GUID: every saved deck has one.
            if (string.IsNullOrWhiteSpace(deck.DeckId))
                deck.DeckId = Guid.NewGuid().ToString();

            deck.Modified = DateTime.Now;
            string json = JsonSerializer.Serialize(deck, _jsonOptions);
            // Write a temporary file, then swap it in: a crash mid-save can't
            // leave a half-written deck (decks are saved after every edit).
            string tmp = deck.FilePath + ".saving";
            File.WriteAllText(tmp, json);
            File.Move(tmp, deck.FilePath, overwrite: true);
            deck.IsModified = false;
        }

        /// <summary>The deck as it would be written to its file (for Undo snapshots).</summary>
        public static string ToJson(Deck deck) => JsonSerializer.Serialize(deck, _jsonOptions);

        /// <summary>
        /// A new deck's file: Decks\<name>.deck, or "<name> (2).deck", … when
        /// that file already exists (a new deck never overwrites another).
        /// </summary>
        public static string NewDeckPath(string deckName)
        {
            string path = AppFolderService.DeckFilePath(deckName);
            string dir = Path.GetDirectoryName(path) ?? AppFolderService.DecksFolder;
            string stem = Path.GetFileNameWithoutExtension(path);
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(dir, $"{stem} ({i}).deck");
            return path;
        }

        // ════════════════════════════════════════════════════════════════════
        // LOAD
        // ════════════════════════════════════════════════════════════════════
        public static Deck? Load(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var deck = JsonSerializer.Deserialize<Deck>(json);
                if (deck != null)
                {
                    deck.FilePath = filePath;
                    deck.IsModified = false;
                    RelinkDeckToPool(deck);
                }
                return deck;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Could not load deck: {ex.Message}", ex);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // RELINK TO POOL
        // ════════════════════════════════════════════════════════════════════
        // After a pool database rebuild, PoolId values change. A deck stores a full
        // card snapshot so its display data is unaffected, but the PoolId used to
        // cross-reference the collection (Used/Available counts) can drift. This
        // re-resolves each card's PoolId from the current pool via its stable
        // ScryfallId. Cards with no ScryfallId (older decks) are left untouched.
        private static void RelinkDeckToPool(Deck deck)
        {
            try
            {
                if (deck.Cards == null || deck.Cards.Count == 0) return;

                var scryfallIds = deck.Cards
                    .Where(c => !string.IsNullOrEmpty(c.ScryfallId))
                    .Select(c => c.ScryfallId)
                    .Distinct()
                    .ToHashSet();
                if (scryfallIds.Count == 0) return;

                using var pdb = new Data.AppDbContext();
                var map = pdb.PoolCards.AsNoTracking()
                    .Where(p => scryfallIds.Contains(p.ScryfallId))
                    .Select(p => new { p.ScryfallId, p.PoolId, p.LegalitiesJson, p.IsGameChanger,
                                       p.IsNonFoil, p.IsFoil, p.IsEtched, p.PriceUsd, p.PriceUsdFoil, p.PriceUsdEtched })
                    .ToList()
                    .GroupBy(p => p.ScryfallId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var card in deck.Cards)
                {
                    if (string.IsNullOrEmpty(card.ScryfallId) ||
                        !map.TryGetValue(card.ScryfallId, out var pc))
                        continue;
                    card.PoolId = pc.PoolId;
                    // Deck files don't carry legality: take it from the pool
                    // (in memory — the deck file isn't changed by loading).
                    if (string.IsNullOrWhiteSpace(card.LegalitiesJson))
                        card.LegalitiesJson = pc.LegalitiesJson ?? string.Empty;
                    card.IsGameChanger = pc.IsGameChanger;   // for Commander brackets
                    // Finishes: the pool says which the printing has (deck
                    // files from imports or older versions may not).
                    card.IsNonFoil = pc.IsNonFoil;
                    card.IsFoil = pc.IsFoil;
                    card.IsEtched = pc.IsEtched;
                    card.PriceUsdEtched = pc.PriceUsdEtched;
                    // Today's prices (the deck file keeps the ones from when the card was
                    // added, often blank): the deck's value and its exports stay current.
                    // A price the pool doesn't have keeps the saved one.
                    if (pc.PriceUsd.HasValue) card.PriceUsd = pc.PriceUsd;
                    if (pc.PriceUsdFoil.HasValue) card.PriceUsdFoil = pc.PriceUsdFoil;
                    if (pc.IsEtched && !pc.IsFoil)
                    {
                        card.IsFoil = false;                               // etched-only printing
                        // Older deck files had no etched count: their "foil"
                        // copies of an etched-only printing ARE etched copies.
                        // Moved here (in memory); the file changes when saved.
                        if (card.FoilQuantity > 0)
                        {
                            card.EtchedQuantity += card.FoilQuantity;
                            card.FoilQuantity = 0;
                        }
                    }
                }

                // The deck's tokens: finishes from the token pool.
                var tokenIds = deck.Cards.Where(c => c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId))
                    .Select(c => c.ScryfallId).Distinct().ToList();
                if (tokenIds.Count > 0)
                {
                    var tokens = pdb.TokenCards.AsNoTracking()
                        .Where(t => tokenIds.Contains(t.ScryfallId))
                        .Select(t => new { t.ScryfallId, t.IsNonFoil, t.IsFoil, t.IsEtched })
                        .ToList()
                        .GroupBy(t => t.ScryfallId)
                        .ToDictionary(g => g.Key, g => g.First());
                    foreach (var card in deck.Cards.Where(c => c.IsTokenLine))
                    {
                        if (!tokens.TryGetValue(card.ScryfallId, out var t)) continue;
                        card.IsNonFoil = t.IsNonFoil;
                        card.IsFoil = t.IsFoil;
                        card.IsEtched = t.IsEtched;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RelinkDeckToPool error: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CREATE NEW
        // ════════════════════════════════════════════════════════════════════
        public static Deck CreateNew(
            string name, DeckType deckType)
        {
            return new Deck
            {
                Name = name,
                DeckType = deckType,
                Created = DateTime.Now,
                Modified = DateTime.Now,
                IsModified = false,
                FilePath = AppFolderService.DeckFilePath(name)
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════
        public static DeckCard CloneCard(DeckCard source)
        {
            return new DeckCard
            {
                PoolId = source.PoolId,
                ScryfallId = source.ScryfallId,
                Name = source.Name,
                SetCode = source.SetCode,
                SetName = source.SetName,
                CollectorNumber = source.CollectorNumber,
                ColorIdentity = source.ColorIdentity,
                TypeLine = source.TypeLine,
                ManaCost = source.ManaCost,
                ManaValue = source.ManaValue,
                Power = source.Power,
                Toughness = source.Toughness,
                OracleText = source.OracleText,
                Rarity = source.Rarity,
                Artist = source.Artist,
                IsFoil = source.IsFoil,
                IsNonFoil = source.IsNonFoil,
                ImageNormalUrl = source.ImageNormalUrl,
                LocalImagePath = source.LocalImagePath,
                ImageBackUrl = source.ImageBackUrl,
                LocalImageBackPath = source.LocalImageBackPath,
                PriceUsd = source.PriceUsd,
                PriceUsdFoil = source.PriceUsdFoil
            };
        }

        /// <summary>A token from the token pool, as a deck line template (Edit → Decks → Tokens).</summary>
        public static DeckCard FromTokenCard(TokenCard t) => new()
        {
            ScryfallId = t.ScryfallId,
            Name = t.Name,
            SetCode = t.SetCode,
            SetName = t.SetName,
            CollectorNumber = t.CollectorNumber,
            ColorIdentity = t.ColorIdentity,
            TypeLine = t.TypeLine,
            Power = t.Power,
            Toughness = t.Toughness,
            OracleText = t.OracleText,
            FlavorText = t.FlavorText,
            Rarity = t.Rarity,
            Artist = t.Artist,
            IsFoil = t.IsFoil,
            IsNonFoil = t.IsNonFoil,
            IsEtched = t.IsEtched,
            IsToken = true,
            ImageNormalUrl = t.ImageNormalUrl,
            LocalImagePath = t.LocalImagePath,
            Category = DeckCardCategory.Tokens,
        };

        /// <summary>
        /// Creates DeckCard from a PoolCard
        /// </summary>
        public static DeckCard FromPoolCard(
            BreakersOfE.Models.PoolCard pool)
        {
            return new DeckCard
            {
                PoolId = pool.PoolId,
                ScryfallId = pool.ScryfallId,
                Name = pool.Name,
                SetCode = pool.SetCode,
                SetName = pool.SetName,
                CollectorNumber = pool.CollectorNumber,
                ColorIdentity = pool.ColorIdentity,
                TypeLine = pool.TypeLine,
                ManaCost = pool.ManaCost,
                ManaValue = pool.ManaValue,
                Power = pool.Power,
                Toughness = pool.Toughness,
                OracleText = pool.OracleText,
                FlavorText = pool.FlavorText,
                LegalitiesJson = pool.LegalitiesJson,
                Rarity = pool.Rarity,
                Artist = pool.Artist,
                IsFoil = pool.IsFoil,
                IsNonFoil = pool.IsNonFoil,
                IsEtched = pool.IsEtched,
                IsGameChanger = pool.IsGameChanger,
                ImageNormalUrl = pool.ImageNormalUrl,
                LocalImagePath = pool.LocalImagePath,
                ImageBackUrl = pool.ImageBackUrl,
                LocalImageBackPath = pool.LocalImageBackPath,
                PriceUsd = pool.PriceUsd,
                PriceUsdFoil = pool.PriceUsdFoil,
                PriceUsdEtched = pool.PriceUsdEtched,
            };
        }
    }
}