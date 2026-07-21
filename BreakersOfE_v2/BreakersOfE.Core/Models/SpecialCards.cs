using BreakersOfE.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    // ── Shared helper — keeps each class DRY ────────────────────────────────
    // All special card types share the same display property pattern

    // ── Planechase ───────────────────────────────────────────────────────────
    public class PlanarCard
    {
        [Key] public int PlanarId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public string ManaCost => string.Empty;
        [NotMapped] public double ManaValue => 0;
        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public string ColorIdentity => string.Empty;
        [NotMapped] public string Colors => string.Empty;
        [NotMapped] public string PriceUsdDisplay => string.Empty;
        [NotMapped] public string PriceUsdFoilDisplay => string.Empty;

        [NotMapped]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            _ => "?"
        };

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath
        {
            get
            {
                string f = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string p = Path.Combine(f, $"{SetCode.ToLower()}.png");
                return File.Exists(p) ? p : string.Empty;
            }
        }

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(string.Empty, TypeLine, IsFoil);

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex, TableType.Pool);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Archenemy Schemes ────────────────────────────────────────────────────
    public class SchemeCard
    {
        [Key] public int SchemeId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public string ManaCost => string.Empty;
        [NotMapped] public double ManaValue => 0;
        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public string ColorIdentity => string.Empty;
        [NotMapped] public string Colors => string.Empty;
        [NotMapped] public string PriceUsdDisplay => string.Empty;
        [NotMapped] public string PriceUsdFoilDisplay => string.Empty;

        [NotMapped]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            _ => "?"
        };

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath
        {
            get
            {
                string f = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string p = Path.Combine(f, $"{SetCode.ToLower()}.png");
                return File.Exists(p) ? p : string.Empty;
            }
        }

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(string.Empty, TypeLine, IsFoil);

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex, TableType.Pool);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Vanguard ─────────────────────────────────────────────────────────────
    public class VanguardCard
    {
        [Key] public int VanguardId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string HandModifier { get; set; } = string.Empty;
        public string LifeModifier { get; set; } = string.Empty;

        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public string ManaCost => string.Empty;
        [NotMapped] public double ManaValue => 0;
        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public string ColorIdentity => string.Empty;
        [NotMapped] public string Colors => string.Empty;
        [NotMapped] public string PriceUsdDisplay => string.Empty;
        [NotMapped] public string PriceUsdFoilDisplay => string.Empty;

        [NotMapped]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            _ => "?"
        };

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath
        {
            get
            {
                string f = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string p = Path.Combine(f, $"{SetCode.ToLower()}.png");
                return File.Exists(p) ? p : string.Empty;
            }
        }

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(string.Empty, TypeLine, IsFoil);

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex, TableType.Pool);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Art Series ───────────────────────────────────────────────────────────
    public class ArtSeriesCard
    {
        [Key] public int ArtSeriesId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public string ManaCost => string.Empty;
        [NotMapped] public double ManaValue => 0;
        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public string ColorIdentity => string.Empty;
        [NotMapped] public string Colors => string.Empty;
        [NotMapped] public string OracleText => string.Empty;
        [NotMapped] public string PriceUsdDisplay => string.Empty;
        [NotMapped] public string PriceUsdFoilDisplay => string.Empty;

        [NotMapped]
        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            _ => "?"
        };

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath
        {
            get
            {
                string f = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string p = Path.Combine(f, $"{SetCode.ToLower()}.png");
                return File.Exists(p) ? p : string.Empty;
            }
        }

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(string.Empty, TypeLine, IsFoil);

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex, TableType.Pool);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Conspiracy Card ────────────────────────────────────────────────────────
    // Conspiracy cards are stored separately (not in PoolCards) because they
    // are only playable in Conspiracy draft formats.
    public class ConspiracyCard
    {
        [Key] public int ConspiracyId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string ColorIdentity { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped] public int RowIndex { get; set; }
        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public bool IsLand => false;
        [NotMapped] public bool IsCreature => false;

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex, TableType.Pool);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }
}