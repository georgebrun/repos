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

            deck.Modified = DateTime.Now;
            string json = JsonSerializer.Serialize(deck, _jsonOptions);
            File.WriteAllText(deck.FilePath, json);
            deck.IsModified = false;
        }

        public static void SaveAs(Deck deck, string filePath)
        {
            deck.FilePath = filePath;
            Save(deck);
        }

        public static void SaveAll(IEnumerable<Deck> decks)
        {
            foreach (var deck in decks.Where(d => d.IsModified))
                Save(deck);
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
                    .Select(p => new { p.ScryfallId, p.PoolId })
                    .ToList()
                    .GroupBy(p => p.ScryfallId)
                    .ToDictionary(g => g.Key, g => g.First().PoolId);

                foreach (var card in deck.Cards)
                {
                    if (!string.IsNullOrEmpty(card.ScryfallId) &&
                        map.TryGetValue(card.ScryfallId, out int newPoolId))
                        card.PoolId = newPoolId;
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
        // ADD CARD
        // ════════════════════════════════════════════════════════════════════
        public static bool AddCard(
            Deck deck,
            DeckCard card,
            DeckCardCategory category,
            bool foil,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            // Find existing row for this card (one row per card regardless of foil)
            var existing = deck.Cards.FirstOrDefault(c =>
                c.Category == category &&
                ((!string.IsNullOrEmpty(card.ScryfallId) && c.ScryfallId == card.ScryfallId) ||
                 c.PoolId == card.PoolId));

            // Total copies across foil AND non-foil for copy-limit rules
            int totalQty = existing?.TotalQuantity ?? 0;

            // Check deck rules
            if (!CanAddCard(deck, card, category, totalQty, out errorMessage))
                return false;

            if (existing != null)
            {
                if (foil) existing.FoilQuantity++;
                else existing.Quantity++;
            }
            else
            {
                var newCard = CloneCard(card);
                newCard.Category = category;
                newCard.IsCommander = category == DeckCardCategory.Commander;
                newCard.Quantity = foil ? 0 : 1;
                newCard.FoilQuantity = foil ? 1 : 0;
                deck.Cards.Add(newCard);
            }

            deck.IsModified = true;
            return true;
        }

        // Overload for callers that don't specify foil
        public static bool AddCard(
            Deck deck, DeckCard card,
            DeckCardCategory category,
            out string errorMessage)
            => AddCard(deck, card, category, foil: false, out errorMessage);

        // ── Check if a card can be added ──────────────────────────────────────
        private static bool CanAddCard(
            Deck deck, DeckCard card,
            DeckCardCategory category,
            int currentQty,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            // Basic land and "any number" cards — always allowed
            if (card.IsBasicLand || card.IsAnyNumber)
                return true;

            if (deck.DeckType == DeckType.Commander)
            {
                // Commander rules
                if (category == DeckCardCategory.Commander)
                {
                    if (deck.CommanderCards.Count >= 2)
                    {
                        errorMessage =
                            "Commander decks can only have up to 2 commanders (Partner).";
                        return false;
                    }
                }

                // Max 1 of each card in commander
                if (currentQty >= 1)
                {
                    errorMessage =
                        $"Commander decks can only have 1 copy of {card.Name}.";
                    return false;
                }

                // Color identity check — uses combined identity for Partner commanders
                if (category != DeckCardCategory.Sideboard &&
                    deck.CommanderCards.Count > 0)
                {
                    string cmdIdentity = GetCombinedColorIdentity(
                        deck.CommanderCards);
                    foreach (char c in card.ColorIdentity)
                    {
                        if ("WUBRG".Contains(c) &&
                            !cmdIdentity.Contains(c))
                        {
                            errorMessage =
                                $"{card.Name} is outside your commander's " +
                                $"color identity ({cmdIdentity}).";
                            return false;
                        }
                    }
                }
            }
            else
            {
                // Standard rules — max 4 copies
                if (currentQty >= 4)
                {
                    errorMessage =
                        $"Standard decks can only have 4 copies of {card.Name}.";
                    return false;
                }
            }

            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // REMOVE CARD
        // ════════════════════════════════════════════════════════════════════
        public static void RemoveCard(
            Deck deck, DeckCard card, bool removeAll = false)
        {
            var existing = deck.Cards.FirstOrDefault(c =>
                c.Category == card.Category &&
                ((!string.IsNullOrEmpty(card.ScryfallId) && c.ScryfallId == card.ScryfallId) ||
                 c.PoolId == card.PoolId));

            if (existing == null) return;

            if (removeAll)
            {
                deck.Cards.Remove(existing);
            }
            else
            {
                // Decrement non-foil first, then foil
                if (existing.Quantity > 0)
                    existing.Quantity--;
                else if (existing.FoilQuantity > 0)
                    existing.FoilQuantity--;

                if (existing.TotalQuantity <= 0)
                    deck.Cards.Remove(existing);
            }

            deck.IsModified = true;
        }

        public static void RemoveCard(
            Deck deck, DeckCard card, bool foil, bool removeAll)
        {
            var existing = deck.Cards.FirstOrDefault(c =>
                c.Category == card.Category &&
                ((!string.IsNullOrEmpty(card.ScryfallId) && c.ScryfallId == card.ScryfallId) ||
                 c.PoolId == card.PoolId));

            if (existing == null) return;

            if (removeAll)
            {
                deck.Cards.Remove(existing);
            }
            else
            {
                if (foil && existing.FoilQuantity > 0)
                    existing.FoilQuantity--;
                else if (!foil && existing.Quantity > 0)
                    existing.Quantity--;

                if (existing.TotalQuantity <= 0)
                    deck.Cards.Remove(existing);
            }

            deck.IsModified = true;
        }

        // ════════════════════════════════════════════════════════════════════
        // VALIDATE
        // ════════════════════════════════════════════════════════════════════
        public static DeckValidationResult Validate(Deck deck)
        {
            var result = new DeckValidationResult();

            if (deck.DeckType == DeckType.Commander)
                ValidateCommander(deck, result);
            else
                ValidateStandard(deck, result);

            return result;
        }

        private static void ValidateCommander(
            Deck deck, DeckValidationResult result)
        {
            // Must have exactly 1 commander (or 2 with partner)
            if (deck.CommanderCards.Count == 0)
                result.AddError(
                    "Commander deck must have a commander.");

            // Must be exactly 100 cards
            if (deck.MainboardCount != 100)
                result.AddError(
                    $"Commander deck must have exactly 100 cards " +
                    $"(currently {deck.MainboardCount}).");

            // No duplicates (already enforced on add, but double-check)
            var dupes = deck.Cards
                .Where(c => !c.IsBasicLand && !c.IsAnyNumber &&
                    c.Category != DeckCardCategory.Sideboard &&
                    c.TotalQuantity > 1)
                .ToList();
            foreach (var dupe in dupes)
                result.AddError(
                    $"{dupe.Name} appears {dupe.TotalQuantity} times " +
                    $"(Commander allows only 1).");

            // Color identity
            if (deck.CommanderCards.Count > 0)
            {
                string cmdIdentity = GetCombinedColorIdentity(
                    deck.CommanderCards);
                var invalidCards = deck.MainboardCards
                    .Where(c => !c.IsBasicLand &&
                        HasColorOutsideIdentity(c, cmdIdentity))
                    .ToList();
                foreach (var invalid in invalidCards)
                    result.AddError(
                        $"{invalid.Name} is outside commander " +
                        $"color identity ({cmdIdentity}).");
            }
        }

        private static void ValidateStandard(
            Deck deck, DeckValidationResult result)
        {
            // Max 4 of each non-basic, non-any-number card
            var overLimit = deck.Cards
                .Where(c => !c.IsBasicLand && !c.IsAnyNumber &&
                    c.Category != DeckCardCategory.Sideboard &&
                    c.TotalQuantity > 4)
                .ToList();
            foreach (var card in overLimit)
                result.AddError(
                    $"{card.Name} appears {card.TotalQuantity} times " +
                    $"(Standard allows only 4).");

            // Sideboard max 15
            if (deck.SideboardCount > 15)
                result.AddError(
                    $"Sideboard cannot exceed 15 cards " +
                    $"(currently {deck.SideboardCount}).");
        }

        // ════════════════════════════════════════════════════════════════════
        // MANA CURVE SUGGESTIONS
        // ════════════════════════════════════════════════════════════════════
        public static List<ManaCurveSuggestion> GetManaCurveSuggestions(
            Deck deck)
        {
            var suggestions = new List<ManaCurveSuggestion>();
            var nonLands = deck.MainboardCards
                .Where(c => !c.IsLand).ToList();

            if (nonLands.Count == 0) return suggestions;

            // Build curve
            var curve = new int[11]; // 0-10+
            foreach (var card in nonLands)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                curve[cmc] += card.TotalQuantity;
            }

            int total = nonLands.Sum(c => c.TotalQuantity);
            double avg = deck.AverageCmc;

            // Check average CMC
            if (avg <= 2.5)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "✅",
                    Message = $"Good low average CMC ({avg:F1}) — fast curve.",
                    IsGood = true
                });
            else if (avg >= 4.0)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "⚠️",
                    Message = $"High average CMC ({avg:F1}) — consider " +
                              $"cutting some expensive cards.",
                    IsWarn = true
                });
            else
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "✅",
                    Message = $"Average CMC of {avg:F1} looks balanced.",
                    IsGood = true
                });

            // Check for early plays
            int oneDrops = curve[1];
            if (deck.DeckType == DeckType.Standard && oneDrops < 4)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "⚠️",
                    Message = $"Only {oneDrops} one-drop card(s) — " +
                              $"consider adding more early plays.",
                    IsWarn = true
                });

            // Check for too many high CMC cards
            int highCmc = curve[6] + curve[7] +
                          curve[8] + curve[9] + curve[10];
            if (highCmc > total * 0.2)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "⚠️",
                    Message = $"{highCmc} cards at CMC 6+ ({highCmc * 100 / total}%) " +
                              $"— consider cutting some.",
                    IsWarn = true
                });

            // Check peak of curve
            int peakCmc = Array.IndexOf(curve, curve.Max());
            suggestions.Add(new ManaCurveSuggestion
            {
                Icon = "💡",
                Message = $"Curve peaks at CMC {peakCmc} " +
                          $"({curve[peakCmc]} cards).",
                IsGood = false
            });

            // Recommend land count
            int recommendedLands = deck.DeckType == DeckType.Commander
                ? (int)Math.Round(35 + (avg - 2.5) * 2)
                : (int)Math.Round(20 + (avg - 2.0) * 2);

            recommendedLands = Math.Max(
                deck.DeckType == DeckType.Commander ? 33 : 18,
                Math.Min(
                    deck.DeckType == DeckType.Commander ? 40 : 28,
                    recommendedLands));

            int actualLands = deck.LandCount;
            if (Math.Abs(actualLands - recommendedLands) > 2)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "💡",
                    Message = $"Recommended land count for this curve: " +
                              $"{recommendedLands} (you have {actualLands}).",
                    IsWarn = actualLands < recommendedLands - 2
                });
            else
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "✅",
                    Message = $"Land count ({actualLands}) looks good " +
                              $"for this curve.",
                    IsGood = true
                });

            // Color balance
            string deckColors = deck.DeckColorIdentity;
            if (deckColors.Length > 1)
                suggestions.Add(new ManaCurveSuggestion
                {
                    Icon = "💡",
                    Message = $"Multicolor deck ({deckColors}) — ensure " +
                              $"mana sources support all colors.",
                    IsWarn = false
                });

            return suggestions;
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

        private static string GetCombinedColorIdentity(
            List<DeckCard> commanders)
        {
            var colors = new System.Collections.Generic.HashSet<char>();
            foreach (var cmd in commanders)
                foreach (char c in cmd.ColorIdentity)
                    if ("WUBRG".Contains(c))
                        colors.Add(c);

            string result = string.Empty;
            if (colors.Contains('W')) result += "W";
            if (colors.Contains('U')) result += "U";
            if (colors.Contains('B')) result += "B";
            if (colors.Contains('R')) result += "R";
            if (colors.Contains('G')) result += "G";
            return result;
        }

        private static bool HasColorOutsideIdentity(
            DeckCard card, string identity)
        {
            foreach (char c in card.ColorIdentity)
                if ("WUBRG".Contains(c) && !identity.Contains(c))
                    return true;
            return false;
        }

        // ── Format legality for deck ──────────────────────────────────────────
        public static Dictionary<string, bool> GetDeckLegality(Deck deck)
        {
            var formats = new[]
            {
                "standard", "pioneer", "modern",
                "legacy", "vintage", "commander", "pauper"
            };

            var result = new Dictionary<string, bool>();

            foreach (var fmt in formats)
            {
                // All cards must be legal in the format
                bool legal = true;
                // We check card legality from the pool data
                // For now mark all as unknown — will be populated
                // when cards are loaded from pool with legality data
                result[fmt] = legal;
            }

            return result;
        }

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
                ImageNormalUrl = pool.ImageNormalUrl,
                LocalImagePath = pool.LocalImagePath,
                ImageBackUrl = pool.ImageBackUrl,
                LocalImageBackPath = pool.LocalImageBackPath,
                PriceUsd = pool.PriceUsd,
                PriceUsdFoil = pool.PriceUsdFoil
            };
        }

        /// <summary>
        /// Creates DeckCard from a CollectionDisplayRow
        /// </summary>
        public static DeckCard FromCollectionRow(
            BreakersOfE.Models.CollectionDisplayRow row)
        {
            return new DeckCard
            {
                PoolId = row.PoolId,
                ScryfallId = row.ScryfallId,
                Name = row.Name,
                SetCode = row.SetCode,
                SetName = row.SetName,
                CollectorNumber = row.CollectorNumber,
                ColorIdentity = row.ColorIdentity,
                TypeLine = row.TypeLine,
                ManaCost = row.ManaCost,
                ManaValue = row.ManaValue,
                Power = row.Power,
                Toughness = row.Toughness,
                OracleText = row.OracleText,
                FlavorText = row.FlavorText,
                LegalitiesJson = row.LegalitiesJson,
                Rarity = row.Rarity,
                Artist = row.Artist,
                IsFoil = row.IsFoil,
                IsNonFoil = row.IsNonFoil,
                ImageNormalUrl = row.ImageNormalUrl,
                LocalImagePath = row.LocalImagePath,
                ImageBackUrl = row.ImageBackUrl,
                LocalImageBackPath = row.LocalImageBackPath,
                PriceUsd = row.PriceUsd,
                PriceUsdFoil = row.PriceUsdFoil
            };
        }
    }
}