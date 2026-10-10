using BreakersOfE.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    /// <summary>
    /// Everything a pool printing has. Not a table of its own: EF maps the
    /// concrete types below, each to its own table (an unmapped base class
    /// gives each its own table, not one shared one).
    /// </summary>
    public abstract class PoolCardBase : IOwnedCard, ILegalityRow, System.ComponentModel.INotifyPropertyChanged
    {
        [Key]
        public int PoolId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string LoyaltyOrDefense { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string ImageBackUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public string LocalImageBackPath { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        /// <summary>Exists as an etched foil (Scryfall "finishes" contains "etched").</summary>
        public bool IsEtched { get; set; }
        /// <summary>Gold pill when there is no non-foil version: "F" foil, "E" etched-only.</summary>
        [NotMapped] public string FinishPill => CardFinish.PoolPill(IsNonFoil, IsFoil, IsEtched);

        // ── Owned (from the matching collection table; filled when the pool loads) ──
        [NotMapped] public int OwnedNonFoil { get => _ownedNonFoil; set { if (_ownedNonFoil == value) return; _ownedNonFoil = value; OwnedChanged(); } }
        [NotMapped] public int OwnedFoil { get => _ownedFoil; set { if (_ownedFoil == value) return; _ownedFoil = value; OwnedChanged(); } }
        [NotMapped] public int OwnedEtched { get => _ownedEtched; set { if (_ownedEtched == value) return; _ownedEtched = value; OwnedChanged(); } }
        private int _ownedNonFoil, _ownedFoil, _ownedEtched;

        /// <summary>Owned counts change live while editing (Edit → Pool → Collection).</summary>
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OwnedChanged()
        {
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(OwnedTotal)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(OwnedDisplay)));
        }
        /// <summary>Copies of this printing you own, any finish (sort / filter value).</summary>
        [NotMapped] public int OwnedTotal => OwnedNonFoil + OwnedFoil + OwnedEtched;
        /// <summary>"3", "3 (1F)" or blank when not owned.</summary>
        [NotMapped] public string OwnedDisplay =>
            CardFinish.OwnedText(OwnedTotal, OwnedFoil, OwnedEtched);
        public bool IsToken { get; set; }
        public bool IsMeld { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string Keywords { get; set; } = string.Empty;

        /// <summary>On the Commander Game Changers list (Scryfall's game_changer flag).</summary>
        public bool IsGameChanger { get; set; }

        /// <summary>
        /// The tokens this card makes, as Scryfall links them ("all_parts",
        /// component "token"): token Scryfall IDs, space-separated. Empty when
        /// none are linked (often older sets). Filled by a Full Database Update.
        /// </summary>
        public string TokenIds { get; set; } = string.Empty;

        // ── Online play (MTGO / Arena) ───────────────────────────────────
        // Stored ONLY in the online pool (OnlineCards, OnlineDbContext). The
        // paper pool (PoolCards) ignores these, so paper stays exactly as it was.
        /// <summary>Available on Magic Online (Scryfall "games" contains "mtgo").</summary>
        public bool IsOnMtgo { get; set; }
        /// <summary>Available on MTG Arena (Scryfall "games" contains "arena").</summary>
        public bool IsOnArena { get; set; }
        /// <summary>Only released in a video game (no paper version).</summary>
        public bool IsDigital { get; set; }
        public int? MtgoId { get; set; }
        public int? MtgoFoilId { get; set; }
        public int? ArenaId { get; set; }

        /// <summary>MTGO price in event tickets: "0.25 tix".</summary>
        [NotMapped] public string PriceTixDisplay =>
            PriceTix.HasValue ? $"{PriceTix.Value:F2} tix" : "—";

        // ── Pricing fields ───────────────────────────────────────────────────
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }
        public decimal? PriceUsdEtched { get; set; }
        public decimal? PriceEur { get; set; }
        public decimal? PriceEurFoil { get; set; }
        public decimal? PriceTix { get; set; }

        // Keep raw JSON as backup
        public string PricesJson { get; set; } = string.Empty;

        // ── Computed display ─────────────────────────────────────────────────
        [NotMapped]
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

        [NotMapped]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            "special" => "S",
            "bonus" => "B",
            _ => "?"
        };

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [System.Text.Json.Serialization.JsonIgnore]
        public string ColorDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Colors))
                    return "N";

                // Count distinct WUBRG colors
                var distinct = Colors
                    .Where(c => "WUBRG".Contains(c))
                    .Distinct()
                    .ToList();

                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }

        // ── Price display ────────────────────────────────────────────────────
        [NotMapped]
        public string PriceUsdDisplay =>
            PriceUsd.HasValue ? $"${PriceUsd.Value:F2}" : "—";

        [NotMapped]
        public string PriceUsdFoilDisplay =>
            PriceUsdFoil.HasValue ? $"${PriceUsdFoil.Value:F2}" : "—";

        [NotMapped]
        public string PriceUsdEtchedDisplay =>
            PriceUsdEtched.HasValue ? $"${PriceUsdEtched.Value:F2}" : "—";

        // ── Legality columns: {Binding Legality[commander].Text} etc. (Pool's Legality button) ──
        private LegalityAccessor? _legality;
        [NotMapped]
        public LegalityAccessor Legality =>
            _legality ??= new LegalityAccessor(() => LegalitiesJson);

        // ── Theme-aware colors ───────────────────────────────────────────────
        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(
                Colors, ColorIdentity, TypeLine);
    }

    /// <summary>A card in the card pool (Card Pool → Cards), and the online pool's rows.</summary>
    public class PoolCard : PoolCardBase { }

    /// <summary>
    /// An oversized card (Card Pool → Oversized): Scryfall marks it oversized —
    /// display commanders, league prizes, oversized promos. A full card, just
    /// not one for decks (planes, schemes and vanguards keep their own tables).
    /// </summary>
    public class OversizedCard : PoolCardBase { }
}