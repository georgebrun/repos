using BreakersOfE.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    // ── Condition price multipliers ───────────────────────────────────────────
    public static class ConditionMultiplier
    {
        public static decimal Get(string condition) =>
            condition?.ToLower() switch
            {
                "mint" => 1.00m,
                "near mint" => 1.00m,
                "nm" => 1.00m,
                "excellent" => 0.85m,
                "lightly played" => 0.85m,
                "lp" => 0.85m,
                "good" => 0.70m,
                "moderately played" => 0.70m,
                "mp" => 0.70m,
                "played" => 0.50m,
                "heavily played" => 0.50m,
                "hp" => 0.50m,
                "poor" => 0.25m,
                "damaged" => 0.25m,
                "d" => 0.25m,
                _ => 1.00m
            };
    }

    // ── Collection display row ────────────────────────────────────────────────
    public class CollectionDisplayRow
    {
        public List<DeckUsageRow> DeckUsageRows { get; set; } = new();
        public int CollectionEntryId { get; set; }
        public int PoolId { get; set; }
        public string ScryfallId { get; set; } = string.Empty;
        public Services.TableType RowTableType { get; set; } = Services.TableType.Collection;
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public string ImageBackUrl { get; set; } = string.Empty;
        public string LocalImageBackPath { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string FoilBadge => FoilQuantity > 0 ? "★" : string.Empty;
        public int UsedCount { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── New trading/inventory fields ──────────────────────────────────────
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }

        // ── Non-nullable sort helpers (nulls sort as 0) ───────────────────────
        public decimal BuyAtSort => BuyAt ?? 0m;
        public decimal SellAtSort => SellAt ?? 0m;
        public decimal SellAtValueSort => SellAtValue ?? 0m;
        public decimal PriceHighSort => PriceHigh ?? 0m;
        public decimal MarketValueSort => MarketValue ?? 0m;
        public decimal PriceLowSort => PriceLow ?? 0m;
        public decimal PriceUsdSort => PriceUsd ?? 0m;
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        public string PriceHighDisplay => PriceHigh.HasValue ? $"${PriceHigh.Value:F2}" : string.Empty;
        public string MarketValueDisplay => MarketValue.HasValue ? $"${MarketValue.Value:F2}" : string.Empty;
        public string PriceLowDisplay => PriceLow.HasValue ? $"${PriceLow.Value:F2}" : string.Empty;

        // Legality — stored as bools populated from PoolCard during load
        public bool IsLegalStandard { get; set; }
        public bool IsLegalModern { get; set; }
        public bool IsLegalPioneer { get; set; }
        public bool IsLegalLegacy { get; set; }
        public bool IsLegalVintage { get; set; }
        public string LegalitiesJson { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;

        public bool IsExpanded { get; set; } = false;
        public bool IsFooter { get; set; } = false;

        public string ExpandGlyph => IsExpanded ? "−" : "+";

        public Visibility ExpandButtonVisibility =>
            UsedCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        public DateTime DateAdded { get; set; }
        public DateTime DateModified { get; set; }

        // ── Pricing from pool ─────────────────────────────────────────────────
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }

        // ── Row index for alternating colors ──────────────────────────────────
        public int RowIndex { get; set; }

        // ── Computed quantities ───────────────────────────────────────────────
        public int AvailableCount =>
            Math.Max(0, Quantity + FoilQuantity - UsedCount);

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

        // ── Computed pricing with condition multiplier ────────────────────────
        private decimal ConditionMult =>
            ConditionMultiplier.Get(Condition);

        public string PriceUsdDisplay =>
            PriceUsd.HasValue
                ? $"${PriceUsd.Value:F2}"
                : "—";

        public string PriceUsdFoilDisplay =>
            PriceUsdFoil.HasValue
                ? $"${PriceUsdFoil.Value:F2}"
                : "—";

        public decimal Value =>
            PriceUsd.HasValue
                ? Math.Round(Quantity * PriceUsd.Value * ConditionMult, 2)
                : 0m;

        public decimal FoilValue =>
            PriceUsdFoil.HasValue
                ? Math.Round(FoilQuantity * PriceUsdFoil.Value * ConditionMult, 2)
                : 0m;

        public decimal TotalValue => Value + FoilValue;

        public string ValueDisplay =>
            Value > 0 ? $"${Value:F2}" : "—";

        public string FoilValueDisplay =>
            FoilValue > 0 ? $"${FoilValue:F2}" : "—";

        public string TotalValueDisplay =>
            TotalValue > 0 ? $"${TotalValue:F2}" : "—";

        // ── Display helpers ───────────────────────────────────────────────────
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

        public string ColorDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Colors))
                    return "N";
                var distinct = Colors
                    .Where(c => "WUBRG".Contains(c))
                    .Distinct()
                    .ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }

        public string FavoriteGlyph => IsFavorite ? "F*" : string.Empty;

        public string SetSymbolPath
        {
            get
            {
                string folder = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string path = Path.Combine(
                    folder, $"{SetCode.ToLower()}.png");
                return File.Exists(path) ? path : string.Empty;
            }
        }

        // ── Theme-aware colors ────────────────────────────────────────────────
        public Brush RowForegroundBrush =>
            IsFooter
                ? Brushes.Black
                : CardColorService.GetForeground(ColorIdentity, TypeLine, IsFoil);

        public Brush RowBackgroundBrush =>
            IsFooter
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD6, 0xE8, 0xD6))
                : CardColorService.GetBackground(IsFoil, RowIndex, RowTableType);

        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Summary row for frozen bottom strip ───────────────────────────────────
    public class CollectionSummary
    {
        public int TotalRows { get; set; }
        public int TotalCards { get; set; }
        public int TotalFoils { get; set; }
        public decimal TotalValue { get; set; }

        public string Display =>
            $"Rows: {TotalRows:N0}   " +
            $"Cards: {TotalCards:N0}   " +
            $"Foils: {TotalFoils:N0}   " +
            $"Total Value: ${TotalValue:F2}";
    }

    // ── Deck usage row ────────────────────────────────────────────────────────
    public class DeckUsageRow
    {
        public string DeckName { get; set; } = string.Empty;
        public string DeckType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Category { get; set; } = string.Empty;
        public string IsFoil { get; set; } = string.Empty;
    }

    // ── Deck Import Report ────────────────────────────────────────────────────
    public class DeckImportReportRow
    {
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int NonFoilAdded { get; set; }
        public int FoilAdded { get; set; }
        public int PrevNonFoil { get; set; }
        public int PrevFoil { get; set; }
        public int NewNonFoil { get; set; }
        public int NewFoil { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // ── Footer totals row ─────────────────────────────────────────────────────
    public class FooterRow
    {
        public string Label { get; set; } = string.Empty;
        public string Qty { get; set; } = string.Empty;
        public string FoilQty { get; set; } = string.Empty;
        public string Used { get; set; } = string.Empty;
        public string Available { get; set; } = string.Empty;
        public string PriceUsd { get; set; } = string.Empty;
        public string PriceUsdFoil { get; set; } = string.Empty;
        public string TotalValue { get; set; } = string.Empty;
        public string MarketValue { get; set; } = string.Empty;
        public string BuyAt { get; set; } = string.Empty;
        public string SellAt { get; set; } = string.Empty;
        public string SellAtValue { get; set; } = string.Empty;
        public string PriceHigh { get; set; } = string.Empty;
        public string PriceLow { get; set; } = string.Empty;
        public string Needed { get; set; } = string.Empty;
        public string Excess { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Col1 { get; set; } = string.Empty;
        public string Col2 { get; set; } = string.Empty;
        public string Col3 { get; set; } = string.Empty;
    }

    // ── Want List display row ─────────────────────────────────────────────────
    public class WantListDisplayRow
    {
        public int EntryId { get; set; }
        public int PoolId { get; set; }
        public int RowIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string ColorIdentity { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public bool IsFoil { get; set; }
        public decimal? OfferPrice { get; set; }
        public decimal? MarketPrice { get; set; }
        public string LocalImagePath { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; }

        public string PriceDisplay =>
            OfferPrice.HasValue
                ? $"${OfferPrice:F2}"
                : MarketPrice.HasValue ? $"${MarketPrice:F2} (mkt)" : "—";
    }
}