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
        public DeckCardCategory Category { get; set; } =
            DeckCardCategory.Mainboard;
        public bool IsCommander { get; set; } = false;
        public bool IsFooter { get; set; } = false;
        public bool IsToken { get; set; } = false;

        // ── Computed display ──────────────────────────────────────────────────
        [JsonIgnore]
        public string PowerToughness => CardFaces.PowerToughness(Power, Toughness);
        // ── Numeric sort helper (collector numbers like "123a") ──
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
                    : Services.CardColorService.GetForegroundFromCost(
                        ManaCost, ColorIdentity, TypeLine);

        private static System.Windows.Media.Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
        private static readonly System.Windows.Media.Brush FooterBack = FrozenBrush(0xD6, 0xE8, 0xD6);
        private static readonly System.Windows.Media.Brush CommanderBack = FrozenBrush(0x00, 0x78, 0xD4);

        /// <summary>The footer's and the commander's own row fill (other rows alternate; see CardColorService.RowBackground).</summary>
        [JsonIgnore]
        public System.Windows.Media.Brush? SpecialRowBackground =>
            IsFooter ? FooterBack : IsCommander ? CommanderBack : null;
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
        public bool IsModified { get; set; } = false;

        /// <summary>Deck lines that are part of the deck (not the Tokens part).</summary>
        [JsonIgnore]
        public List<DeckCard> PlayCards => Cards.Where(c => !c.IsTokenLine).ToList();

        [JsonIgnore]
        public int LandCount =>
            PlayCards.Where(c => c.IsLand &&
                c.Category != DeckCardCategory.Sideboard)
                .Sum(c => c.TotalQuantity);

        // ── Collection value ──────────────────────────────────────────────────
        [JsonIgnore]
        public decimal TotalValue => Cards.Sum(c => c.RowValue);
    }
}