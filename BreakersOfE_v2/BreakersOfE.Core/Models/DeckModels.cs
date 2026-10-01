using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BreakersOfE.Models
{
    // ── Deck type ─────────────────────────────────────────────────────────────
    // Stored in deck files as a number, so values are only ever ADDED at the
    // end. "Standard" is Constructed (any 60-card format; the name stays for
    // the files) — Deck.ConstructedFormat says which. Rules: DeckFormats.
    public enum DeckType
    {
        Standard,          // Constructed (60+): Standard, Pioneer, Modern, …
        Commander,
        Brawl,
        StandardBrawl,
        PauperCommander,
        DuelCommander,
        Oathbreaker,
        Limited,
    }

    // ── Deck card category ────────────────────────────────────────────────────
    // Stored in deck files as a number: values are only ever ADDED at the end.
    public enum DeckCardCategory
    {
        Commander,
        Mainboard,
        Sideboard,
        /// <summary>Tokens the deck uses (play aids): never part of the deck's
        /// size, copies, legality or colors.</summary>
        Tokens,
    }

    // ── Archetype ─────────────────────────────────────────────────────────────
    public enum DeckArchetype
    {
        Unspecified,
        Aggro,
        Control,
        Combo,
        Midrange,
        Tempo,
        Ramp,
        Tokens,
        Prison,
        Burn,
        Mill,
        Reanimator
    }

    // ── Individual card in a deck ─────────────────────────────────────────────
    public class DeckCard : ILegalityRow
    {
        public int PoolId { get; set; }
        public string ScryfallId { get; set; } = string.Empty;  // stable key for collection relink
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;

        // ── Legality ──────────────────────────────────────────────────────────
        public string LegalitiesJson { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsLegalStandard => GetLegalityRaw("standard") == "legal";
        [JsonIgnore]
        public bool IsLegalModern => GetLegalityRaw("modern") == "legal";
        [JsonIgnore]
        public bool IsLegalPioneer => GetLegalityRaw("pioneer") == "legal";
        [JsonIgnore]
        public bool IsLegalLegacy => GetLegalityRaw("legacy") == "legal";
        [JsonIgnore]
        public bool IsLegalVintage => GetLegalityRaw("vintage") == "legal";

        private string GetLegalityRaw(string format)
        {
            if (string.IsNullOrWhiteSpace(LegalitiesJson)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(LegalitiesJson);
                if (doc.RootElement.TryGetProperty(format, out var v))
                    return v.GetString()?.ToLower() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        // ── Deck vs. collection (filled when the deck opens; not saved) ──────
        /// <summary>Copies of this exact printing in the collection (all finishes).</summary>
        [JsonIgnore] public int CollectionOwned { get; set; }
        /// <summary>Of those, copies not used by other decks or the Trade Binder.</summary>
        [JsonIgnore] public int CollectionFree { get; set; }
        /// <summary>Copies still needed after free copies of any printing of this card.</summary>
        [JsonIgnore] public int CollectionMissing { get; set; }
        /// <summary>Owned column (shared with the Pool): this printing's copies in the collection.</summary>
        [JsonIgnore] public int OwnedTotal => CollectionOwned;
        [JsonIgnore] public string OwnedDisplay => CollectionOwned.ToString();
        /// <summary>Copies of this card (any printing) on the Want List.</summary>
        [JsonIgnore] public int WantedCount { get; set; }
        /// <summary>Copies of this line claimed from the collection for this deck (Edit → Decks).</summary>
        [JsonIgnore] public int ClaimedCount { get; set; }

        // ── Cards shared between decks (filled when the deck opens; not saved) ──
        /// <summary>How many of your OTHER decks use this card (any printing).</summary>
        [JsonIgnore] public int OtherDecksCount { get; set; }
        /// <summary>Tooltip: the other decks and their copies.</summary>
        [JsonIgnore] public string OtherDecksTip { get; set; } = string.Empty;

        /// <summary>On the Commander Game Changers list. Filled from the pool when the deck opens; not saved.</summary>
        [JsonIgnore]
        public bool IsGameChanger { get; set; }

        /// <summary>
        /// The Scryfall format this card is checked against — the deck's own
        /// ("commander" or "standard"). Set when the deck opens; not saved.
        /// </summary>
        [JsonIgnore]
        public string DeckFormat { get; set; } = "standard";

        private LegalityAccessor? _legality;
        /// <summary>{Binding Legality[deck].Text} = legality in the deck's format.</summary>
        [JsonIgnore]
        public LegalityAccessor Legality =>
            _legality ??= new LegalityAccessor(() => LegalitiesJson, () => DeckFormat);

        // ── Sideboard ─────────────────────────────────────────────────────────
        [JsonIgnore]
        public string SideboardDisplay =>
            Category == DeckCardCategory.Sideboard ? "SB"
            : Category == DeckCardCategory.Tokens ? "Token" : string.Empty;

        /// <summary>A token line (the deck's Tokens part): not part of the deck itself.</summary>
        [JsonIgnore]
        public bool IsTokenLine => Category == DeckCardCategory.Tokens;

        // ── Row index ─────────────────────────────────────────────────────────
        public int RowIndex { get; set; }
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string ImageBackUrl { get; set; } = string.Empty; // DFC back face
        public string LocalImagePath { get; set; } = string.Empty;
        public string LocalImageBackPath { get; set; } = string.Empty; // DFC back face cached
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }

        // ── Etched (filled from the pool when the deck opens; not saved) ──────
        // Etched copies have their own count (EtchedQuantity, saved). Older
        // deck files had none: there, the "foil" copies of an ETCHED-ONLY
        // printing were its etched copies — they're moved to the etched count
        // when the deck opens (DeckService.Load) and saved that way next time.
        /// <summary>The printing exists as etched.</summary>
        [JsonIgnore] public bool IsEtched { get; set; }
        /// <summary>Etched price (usd_etched).</summary>
        [JsonIgnore] public decimal? PriceUsdEtched { get; set; }
        /// <summary>Etched-only printing: the foil count means etched copies.</summary>
        [JsonIgnore] public bool IsEtchedOnly => IsEtched && !IsFoil;
        /// <summary>Price of the deck's "foil" copies: etched price for etched-only printings.</summary>
        [JsonIgnore] public decimal? FoilCopyPrice => IsEtchedOnly ? PriceUsdEtched : PriceUsdFoil;
        /// <summary>Price of the deck's etched copies (the etched price only — never another finish's).</summary>
        [JsonIgnore] public decimal? EtchedCopyPrice => PriceUsdEtched;

        // ── Deck-specific ─────────────────────────────────────────────────────
        public int Quantity { get; set; } = 0;
        public int FoilQuantity { get; set; } = 0;
        /// <summary>Etched copies (0 in deck files written before v2 deck editing).</summary>
        public int EtchedQuantity { get; set; } = 0;
        public string FoilBadge => FoilQuantity > 0 ? "★" : string.Empty;
        public DeckCardCategory Category { get; set; } =
            DeckCardCategory.Mainboard;
        public bool IsCommander { get; set; } = false;
        public bool IsFooter { get; set; } = false;
        public bool IsToken { get; set; } = false;

        // ── Computed display ──────────────────────────────────────────────────
        [JsonIgnore]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) &&
            !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;
        // ── Numeric sort helpers (extracts number from strings like "3", "X", "123a") ──
        public double PowerSort => double.TryParse(Power, out var v) ? v : -1;
        public double ToughnessSort => double.TryParse(Toughness, out var v) ? v : -1;
        public double PowerToughnessSort => PowerSort;
        public double CollectorNumberSort
        {
            get
            {
                if (double.TryParse(CollectorNumber, out var v)) return v;
                int end = 0;
                while (end < CollectorNumber.Length && char.IsDigit(CollectorNumber[end])) end++;
                return end > 0 && double.TryParse(CollectorNumber[..end], out var v2) ? v2 : 9999;
            }
        }

        [JsonIgnore]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            "special" => "S",
            _ => "?"
        };

        [JsonIgnore]
        public string CategoryDisplay => Category switch
        {
            DeckCardCategory.Commander => "Commander",
            DeckCardCategory.Mainboard => "Mainboard",
            DeckCardCategory.Sideboard => "Sideboard",
            DeckCardCategory.Tokens => "Tokens",
            _ => "Mainboard"
        };

        [JsonIgnore]
        public string PriceUsdDisplay =>
            PriceUsd.HasValue ? $"${PriceUsd.Value:F2}" : "—";

        [JsonIgnore]
        public string PriceUsdFoilDisplay =>
            PriceUsdFoil.HasValue ? $"${PriceUsdFoil.Value:F2}" : "—";

        [JsonIgnore]
        public string PriceUsdEtchedDisplay =>
            PriceUsdEtched.HasValue ? $"${PriceUsdEtched.Value:F2}" : "—";

        [JsonIgnore]
        public int TotalQuantity => Quantity + FoilQuantity + EtchedQuantity;

        /// <summary>Gold pill for the special finishes in this line: "F", "E" or "F E".</summary>
        [JsonIgnore]
        public string FinishPill =>
            FoilQuantity > 0 && IsEtchedOnly ? "E"                       // older file, not converted
            : FoilQuantity > 0 && EtchedQuantity > 0 ? "F E"
            : FoilQuantity > 0 ? "F"
            : EtchedQuantity > 0 ? "E" : string.Empty;

        /// <summary>
        /// Deck value of this line: each finish's copies × that finish's price.
        /// Non-foil and foil borrow each other's when missing (older deck files
        /// carry old price snapshots); etched uses the etched price only.
        /// </summary>
        [JsonIgnore]
        public decimal RowValue =>
            (PriceUsd ?? FoilCopyPrice ?? 0m) * Quantity +
            (FoilCopyPrice ?? PriceUsd ?? 0m) * FoilQuantity +
            (EtchedCopyPrice ?? 0m) * EtchedQuantity;

        [JsonIgnore]
        public string RowValueDisplay =>
            PriceUsd.HasValue || FoilCopyPrice.HasValue || PriceUsdEtched.HasValue ? $"${RowValue:F2}" : "—";

        /// <summary>Copies of one finish ("nonfoil", "foil", "etched").</summary>
        public int CountOf(string finish) => finish switch
        {
            CardFinish.Foil => FoilQuantity,
            CardFinish.Etched => EtchedQuantity,
            _ => Quantity,
        };

        /// <summary>Set the copies of one finish.</summary>
        public void SetCount(string finish, int n)
        {
            n = Math.Max(0, n);
            switch (finish)
            {
                case CardFinish.Foil: FoilQuantity = n; break;
                case CardFinish.Etched: EtchedQuantity = n; break;
                default: Quantity = n; break;
            }
        }

        [JsonIgnore]
        public string ValueDisplay =>
            PriceUsd.HasValue
                ? $"${PriceUsd.Value * Quantity:F2}"
                : "—";

        [JsonIgnore]
        public string FoilValueDisplay =>
            FoilCopyPrice.HasValue
                ? $"${FoilCopyPrice.Value * FoilQuantity:F2}"
                : "—";

        [JsonIgnore]
        public decimal ValueSort => PriceUsd.HasValue ? PriceUsd.Value * Quantity : 0m;

        [JsonIgnore]
        public decimal FoilValueSort => PriceUsdFoil.HasValue ? PriceUsdFoil.Value * FoilQuantity : 0m;

        [JsonIgnore]
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        // ── Color display ─────────────────────────────────────────────────────
        [JsonIgnore]
        public string ColorDisplay
        {
            get
            {
                var distinct = ColorIdentity
                    .Where(c => "WUBRG".Contains(c))
                    .Distinct()
                    .ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }

        [JsonIgnore]
        public bool IsLand =>
            TypeLine.Contains("Land",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsCreature =>
            TypeLine.Contains("Creature",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsArtifact =>
            TypeLine.Contains("Artifact",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsEnchantment =>
            TypeLine.Contains("Enchantment",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsPlaneswalker =>
            TypeLine.Contains("Planeswalker",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsBattle =>
            TypeLine.Contains("Battle",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsInstant =>
            TypeLine.Contains("Instant",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsSorcery =>
            TypeLine.Contains("Sorcery",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsPermanent =>
            IsCreature || IsArtifact || IsEnchantment
            || IsPlaneswalker || IsLand || IsBattle;

        [JsonIgnore]
        public bool IsBasicLand =>
            TypeLine.Contains("Basic Land",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsAnyNumber =>
            OracleText.Contains("A deck can have any number",
                StringComparison.OrdinalIgnoreCase) ||
            OracleText.Contains("any number of cards named",
                StringComparison.OrdinalIgnoreCase);

        // ── Color brushes ─────────────────────────────────────────────────────
        [JsonIgnore]
        public System.Windows.Media.Brush RowForegroundBrush =>
            IsFooter
                ? System.Windows.Media.Brushes.Black
                : IsCommander
                    ? System.Windows.Media.Brushes.White
                    : Services.CardColorService.GetForeground(
                        ColorIdentity, TypeLine, false);

        [JsonIgnore]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            IsFooter
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD6, 0xE8, 0xD6))
                : IsCommander
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))
                    : Services.CardColorService.GetBackground(false, RowIndex,
                        Services.TableType.Deck);

        [JsonIgnore]
        public System.Windows.Media.Brush CellBorderBrush =>
            Services.CardColorService.GetCellBorderBrush();
    }

    // ── Complete deck ─────────────────────────────────────────────────────────
    public class Deck
    {
        // ── Identity ──────────────────────────────────────────────────────────
        /// <summary>Stable deck GUID (collection deck usage is keyed on it).
        /// v1 wrote it; v2 gives one to any deck it saves that has none.</summary>
        public string DeckId { get; set; } = string.Empty;
        /// <summary>Legacy v1 flag, kept only so the file round-trips. Nothing reads it.</summary>
        public bool CollectionLinked { get; set; }
        public string Name { get; set; } = "New Deck";
        public string Description { get; set; } = string.Empty;
        public DeckType DeckType { get; set; } = DeckType.Standard;
        /// <summary>Constructed decks: the format they're built for (Scryfall key, e.g. "modern").</summary>
        public string ConstructedFormat { get; set; } = "standard";
        public DeckArchetype Archetype { get; set; } =
            DeckArchetype.Unspecified;
        public string FilePath { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.Now;
        public DateTime Modified { get; set; } = DateTime.Now;

        // ── Power level ───────────────────────────────────────────────────────
        public int? UserPowerLevel { get; set; }   // user override 1-10
        public bool UseCalculatedPower { get; set; } = true;

        // ── Cards ─────────────────────────────────────────────────────────────
        public List<DeckCard> Cards { get; set; } = new();

        // ── Computed properties ───────────────────────────────────────────────
        [JsonIgnore]
        public string FileType => "Breakers of E Deck";

        [JsonIgnore]
        public string FileName =>
            string.IsNullOrEmpty(FilePath)
                ? "(not saved)"
                : FilePath;

        [JsonIgnore]
        public bool IsModified { get; set; } = false;

        [JsonIgnore]
        public string TabTitle =>
            IsModified ? $"{Name} *" : Name;

        // ── Card groupings ────────────────────────────────────────────────────
        [JsonIgnore]
        public List<DeckCard> CommanderCards =>
            Cards.Where(c => c.Category ==
                DeckCardCategory.Commander).ToList();

        [JsonIgnore]
        public List<DeckCard> MainboardCards =>
            Cards.Where(c => c.Category ==
                DeckCardCategory.Mainboard).ToList();

        [JsonIgnore]
        public List<DeckCard> SideboardCards =>
            Cards.Where(c => c.Category ==
                DeckCardCategory.Sideboard).ToList();

        // ── Counts ────────────────────────────────────────────────────────────
        [JsonIgnore]
        public int MainboardCount =>
            MainboardCards.Sum(c => c.TotalQuantity) +
            CommanderCards.Sum(c => c.TotalQuantity);

        [JsonIgnore]
        public int SideboardCount =>
            SideboardCards.Sum(c => c.TotalQuantity);

        [JsonIgnore]
        public int TotalCount => MainboardCount + SideboardCount;

        /// <summary>Deck lines that are part of the deck (not the Tokens part).</summary>
        [JsonIgnore]
        public List<DeckCard> PlayCards => Cards.Where(c => !c.IsTokenLine).ToList();

        [JsonIgnore]
        public int LandCount =>
            PlayCards.Where(c => c.IsLand &&
                c.Category != DeckCardCategory.Sideboard)
                .Sum(c => c.TotalQuantity);

        [JsonIgnore]
        public int CreatureCount =>
            PlayCards.Where(c => c.IsCreature &&
                c.Category != DeckCardCategory.Sideboard)
                .Sum(c => c.TotalQuantity);

        [JsonIgnore]
        public int SpellCount =>
            MainboardCount - LandCount - CreatureCount;

        [JsonIgnore]
        public int FoilCount =>
            PlayCards.Where(c => c.Category != DeckCardCategory.Sideboard)
                .Sum(c => c.FoilQuantity + c.EtchedQuantity);

        [JsonIgnore]
        public int NonFoilCount =>
            PlayCards.Where(c => c.Category != DeckCardCategory.Sideboard)
                .Sum(c => c.Quantity);

        // ── Color identity ────────────────────────────────────────────────────
        [JsonIgnore]
        public string DeckColorIdentity
        {
            get
            {
                var colors = new System.Collections.Generic.HashSet<char>();
                foreach (var card in PlayCards)
                    foreach (char c in card.ColorIdentity)
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
        }

        // ── Average CMC ───────────────────────────────────────────────────────
        [JsonIgnore]
        public double AverageCmc
        {
            get
            {
                var nonLands = PlayCards
                    .Where(c => !c.IsLand &&
                        c.Category != DeckCardCategory.Sideboard)
                    .ToList();
                if (nonLands.Count == 0) return 0;

                double total = nonLands.Sum(c => c.ManaValue * c.TotalQuantity);
                int count = nonLands.Sum(c => c.TotalQuantity);
                return count > 0
                    ? Math.Round(total / count, 2) : 0;
            }
        }

        // ── Calculated power level (1-10) ─────────────────────────────────────
        [JsonIgnore]
        public int CalculatedPowerLevel
        {
            get
            {
                var cards = PlayCards;
                if (cards.Count == 0) return 1;

                double score = 5.0; // start at middle

                // Average CMC — lower = more powerful
                if (AverageCmc <= 1.5) score += 2;
                else if (AverageCmc <= 2.5) score += 1;
                else if (AverageCmc >= 4.0) score -= 1;
                else if (AverageCmc >= 5.0) score -= 2;

                // Average card price
                var priced = cards
                    .Where(c => c.PriceUsd.HasValue).ToList();
                if (priced.Count > 0)
                {
                    double avgPrice = (double)priced
                        .Average(c => c.PriceUsd!.Value);
                    if (avgPrice >= 20) score += 2;
                    else if (avgPrice >= 10) score += 1;
                    else if (avgPrice <= 1) score -= 1;
                }

                // Expensive cards ratio
                int expensiveCount = cards
                    .Count(c => c.PriceUsd >= 10);
                double ratio = cards.Count > 0
                    ? (double)expensiveCount / cards.Count : 0;
                if (ratio >= 0.3) score += 1;

                return Math.Max(1, Math.Min(10, (int)Math.Round(score)));
            }
        }

        [JsonIgnore]
        public int DisplayPowerLevel =>
            UserPowerLevel.HasValue && !UseCalculatedPower
                ? UserPowerLevel.Value
                : CalculatedPowerLevel;

        // ── Collection value ──────────────────────────────────────────────────
        [JsonIgnore]
        public decimal TotalValue => Cards.Sum(c => c.RowValue);

        [JsonIgnore]
        public string TotalValueDisplay =>
            $"${TotalValue:F2}";

        // ── Aggression (0-100, higher = more aggressive) ──────────────────────
        [JsonIgnore]
        public int AggressionScore
        {
            get
            {
                // Based on average CMC of non-land cards
                // Lower CMC = higher aggression
                double cmc = AverageCmc;
                if (cmc <= 1.5) return 90;
                if (cmc <= 2.0) return 75;
                if (cmc <= 2.5) return 60;
                if (cmc <= 3.0) return 50;
                if (cmc <= 3.5) return 40;
                if (cmc <= 4.0) return 30;
                if (cmc <= 4.5) return 20;
                return 10;
            }
        }

        [JsonIgnore]
        public string AggressionLabel =>
            AggressionScore switch
            {
                >= 85 => "Very Aggressive",
                >= 65 => "Aggressive",
                >= 45 => "Balanced",
                >= 25 => "Defensive",
                _ => "Very Defensive"
            };
    }

    // ── Validation result ─────────────────────────────────────────────────────
    public class DeckValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public void AddError(string msg)
        {
            Errors.Add(msg);
            IsValid = false;
        }

        public void AddWarning(string msg) =>
            Warnings.Add(msg);
    }

    // ── Mana curve suggestion ─────────────────────────────────────────────────
    public class ManaCurveSuggestion
    {
        public string Icon { get; set; } = "💡";
        public string Message { get; set; } = string.Empty;
        public bool IsGood { get; set; } = false;
        public bool IsWarn { get; set; } = false;
    }
}