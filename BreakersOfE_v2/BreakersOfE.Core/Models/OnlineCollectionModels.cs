using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace BreakersOfE.Models
{
    /// <summary>The two online games (Scryfall's "games" values).</summary>
    public static class OnlineGame
    {
        public const string Mtgo = "mtgo";
        public const string Arena = "arena";

        public static string Display(string game) => game == Arena ? "Arena" : "MTGO";
    }

    /// <summary>
    /// One row of an ONLINE collection (Magic Online or MTG Arena), in
    /// collection.db's own table OnlineCollectionEntries — never mixed with
    /// the paper collection. <see cref="Game"/> says which game.
    ///
    /// A row is one printing + finish. Digital cards have no condition or
    /// language. MTGO has foils ("Premium") and prices in event tickets;
    /// Arena has neither (non-foil only, no price).
    /// </summary>
    public class OnlineCollectionEntry
    {
        [Key]
        public int OnlineCollectionEntryId { get; set; }

        /// <summary>"mtgo" or "arena" (<see cref="OnlineGame"/>).</summary>
        public string Game { get; set; } = OnlineGame.Mtgo;

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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
        public bool IsDigital { get; set; }
        public int? MtgoId { get; set; }
        public int? MtgoFoilId { get; set; }
        public int? ArenaId { get; set; }

        /// <summary>Finishes this printing has in this game (set when added).</summary>
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }

        /// <summary>"nonfoil" or "foil" (MTGO Premium). Arena: always non-foil.</summary>
        public string Finish { get; set; } = CardFinish.NonFoil;
        public int Quantity { get; set; }
        /// <summary>MTGO: this finish's price in event tickets. Arena: none.</summary>
        public decimal? Price { get; set; }
        public string Notes { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;

        // ── Display-only: lets the shared grid, gallery and detail panel show
        //    online rows like any other collection row. ─────────────────────
        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public bool IsFoil => IsFoilAvailable;
        [NotMapped] public bool IsNonFoil => IsNonFoilAvailable;
        [NotMapped] public bool IsOnMtgo => Game == OnlineGame.Mtgo;
        [NotMapped] public bool IsOnArena => Game == OnlineGame.Arena;
        /// <summary>For the detail panel: the MTGO price of this row, in tickets.</summary>
        [NotMapped] public decimal? PriceTix => IsOnMtgo ? Price : null;

        [NotMapped] public string FinishPill => CardFinish.Pill(Finish);
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;

        /// <summary>"0.25 tix" (MTGO) or "—".</summary>
        [NotMapped] public string PriceDisplay =>
            Price.HasValue ? $"{Price.Value:F2} tix" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay =>
            Price.HasValue ? $"{RowValue:F2} tix" : "—";

        [NotMapped] public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;

        [NotMapped] public double CollectorNumberSort
        {
            get
            {
                if (double.TryParse(CollectorNumber, out var v)) return v;
                int end = 0;
                while (end < CollectorNumber.Length && char.IsDigit(CollectorNumber[end])) end++;
                return end > 0 && double.TryParse(CollectorNumber[..end], out var v2) ? v2 : 9999;
            }
        }

        [NotMapped] public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C", "uncommon" => "U", "rare" => "R",
            "mythic" => "M", "special" => "S", "bonus" => "B", _ => "?"
        };

        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");

        [NotMapped] public string ColorDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Colors)) return "N";
                var distinct = Colors.Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }

        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped] public System.Windows.Media.Brush RowForegroundBrush =>
            Services.CardColorService.GetForeground(ColorIdentity, TypeLine, Finish != CardFinish.NonFoil);
        [NotMapped] public System.Windows.Media.Brush RowBackgroundBrush =>
            Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, Services.TableType.Collection);
    }
}
