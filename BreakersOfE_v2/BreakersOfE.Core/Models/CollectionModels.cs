using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BreakersOfE.Models
{
    // ── Pool Collection ─────────────────────────────────────────────────────
    public class CollectionEntry : ILegalityRow, IFinishRow
    {
        [Key]
        public int CollectionEntryId { get; set; }
        public int PoolId { get; set; }                          // legacy — kept for backward compat
        public string ScryfallId { get; set; } = string.Empty;   // stable cross-db key

        // ── Embedded card data (self-contained — no pool join needed) ───────
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
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public bool IsToken { get; set; }
        public bool IsMeld { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string Keywords { get; set; } = string.Empty;
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }
        public decimal? PriceUsdEtched { get; set; }
        public decimal? PriceEur { get; set; }
        public decimal? PriceEurFoil { get; set; }
        public decimal? PriceTix { get; set; }
        public string PricesJson { get; set; } = string.Empty;

        // ── Collection-specific metadata ────────────────────────────────────
        // FINISH MODEL (Phase 2): each row now represents ONE finish of a
        // printing. Finish is "nonfoil", "foil", or "etched". Row identity is
        // ScryfallId + Finish. Quantity is the count of THIS finish. Price is
        // the single USD value for THIS finish (stored raw decimal; displayed
        // as currency rounded to cents). The legacy FoilQuantity and the
        // multi-finish price columns above are kept temporarily so old data and
        // un-migrated collections still load; the migration splits combined
        // rows into per-finish rows and populates Finish + Price.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }

        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }   // legacy — migration drains this into per-finish rows
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, and
        //    detail panel show collection rows exactly like pool rows. ──────
        [NotMapped] public int RowIndex { get; set; }

        [NotMapped] public bool IsFoil => IsFoilAvailable;       // finishes the printing exists in
        [NotMapped] public bool IsNonFoil => IsNonFoilAvailable;

        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;

        [NotMapped]
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
        public string PriceUsdDisplay =>
            PriceUsd.HasValue ? $"${PriceUsd.Value:F2}" : "—";
        [NotMapped]
        public string PriceUsdFoilDisplay =>
            PriceUsdFoil.HasValue ? $"${PriceUsdFoil.Value:F2}" : "—";

        // ── Per-finish collection columns (each row is ONE finish) ─────────
        /// <summary>Finish pill: "F" foil, "E" etched, blank for non-foil.</summary>
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;

        /// <summary>Owned minus used, for THIS finish. UsedCount is kept
        /// derived and per-finish (capped at Quantity) by the deck-usage code.</summary>
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);

        /// <summary>This finish's price (not the non-foil USD column).</summary>
        [NotMapped]
        public string PriceDisplay =>
            Price.HasValue ? $"${Price.Value:F2}" : "—";

        /// <summary>This finish's price × this row's quantity.</summary>
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped]
        public string RowValueDisplay =>
            Price.HasValue ? $"${RowValue:F2}" : "—";

        // ── Legality columns: {Binding Legality[commander].Text} etc. ────────
        private LegalityAccessor? _legality;
        [NotMapped]
        public LegalityAccessor Legality =>
            _legality ??= new LegalityAccessor(() => LegalitiesJson);

        // ── Remaining v1 collection columns ─────────────────────────────────
        [NotMapped]
        public string BuyAtDisplay =>
            BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped]
        public string SellAtDisplay =>
            SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped]
        public string SellAtValueDisplay =>
            SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;

        /// <summary>Date added as yyyy-MM-dd so it sorts and filters in date order.</summary>
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");

        /// <summary>W/U/B/R/G for one color, M for multicolor, N for colorless (as v1).</summary>
        [NotMapped]
        public string ColorDisplay
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

        // Collection tint (blue family), foil rows marked like the pool.
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground(
                Colors, ColorIdentity, TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Token Collection ────────────────────────────────────────────────────
    public class TokenCollectionEntry : IFinishRow
    {
        [Key]
        public int TokenCollectionEntryId { get; set; }
        public int TokenId { get; set; }                         // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
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
        public string Layout { get; set; } = string.Empty;
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);
        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;
        [NotMapped] public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        [NotMapped]
        public string ColorDisplay
        {
            get
            {
                var distinct = (Colors ?? "").Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Planar Collection ───────────────────────────────────────────────────
    public class PlanarCollectionEntry : IFinishRow
    {
        [Key]
        public int PlanarCollectionEntryId { get; set; }
        public int PlanarId { get; set; }                        // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
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
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);
        [NotMapped] public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground("", "", TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Scheme Collection ───────────────────────────────────────────────────
    public class SchemeCollectionEntry : IFinishRow
    {
        [Key]
        public int SchemeCollectionEntryId { get; set; }
        public int SchemeId { get; set; }                        // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
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
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);
        [NotMapped] public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground("", "", TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Vanguard Collection ─────────────────────────────────────────────────
    public class VanguardCollectionEntry : IFinishRow
    {
        [Key]
        public int VanguardCollectionEntryId { get; set; }
        public int VanguardId { get; set; }                      // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
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
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string HandModifier { get; set; } = string.Empty;
        public string LifeModifier { get; set; } = string.Empty;

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);
        [NotMapped] public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground("", "", TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Conspiracy Collection ───────────────────────────────────────────────
    public class ConspiracyCollectionEntry : IFinishRow
    {
        [Key]
        public int ConspiracyCollectionEntryId { get; set; }
        public int ConspiracyId { get; set; }                    // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string ColorIdentity { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped]
        public string ColorDisplay
        {
            get
            {
                var distinct = (Colors ?? "").Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Art Series Collection ───────────────────────────────────────────────
    public class ArtSeriesCollectionEntry : IFinishRow
    {
        [Key]
        public int ArtSeriesCollectionEntryId { get; set; }
        public int ArtSeriesId { get; set; }                     // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
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
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        // ── Collection metadata ─────────────────────────────────────────────
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        // Phase 2 per-finish: Finish nonfoil/foil/etched, single Price for this finish.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        /// <summary>Favorite column: a star when this row is a favorite.</summary>
        [NotMapped] public string FavoriteDisplay => IsFavorite ? "★" : string.Empty;
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped] public int AvailableCount => Math.Max(0, Quantity - UsedCount);
        [NotMapped] public string BuyAtDisplay => BuyAt.HasValue ? $"${BuyAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtDisplay => SellAt.HasValue ? $"${SellAt.Value:F2}" : string.Empty;
        [NotMapped] public string SellAtValueDisplay => SellAtValue.HasValue ? $"${SellAtValue.Value:F2}" : string.Empty;
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground("", "", TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.Collection);
    }

    // ── Trade Binder — Have list (cards you own and want to trade away) ────
    public class TradeBinderEntry : IFinishRow
    {
        [Key]
        public int TradeBinderEntryId { get; set; }
        public int PoolId { get; set; }                          // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string ColorIdentity { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;

        // ── Trade-specific metadata ─────────────────────────────────────────
        public int Quantity { get; set; } = 1;
        // FINISH MODEL (Phase 2): Finish is "nonfoil"/"foil"/"etched" — replaces
        // the two-state IsFoil so binders can express etched too. IsFoil kept
        // for migration (Finish populated from it); Price is this finish's value.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public bool IsFoil { get; set; } = false;   // legacy — migrated into Finish
        public string Condition { get; set; } = "Near Mint";
        /// <summary>v2: the binder copies' language (with finish and condition, the collection row they come from).</summary>
        public string Language { get; set; } = CardLanguage.Default;
        public decimal? AskingPrice { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;
        [NotMapped] public string AskingPriceDisplay => AskingPrice.HasValue ? $"${AskingPrice.Value:F2}" : string.Empty;
        /// <summary>Market price × Trade % (Settings): the least a fair shop should give per copy.</summary>
        [NotMapped]
        public decimal? TradeValue =>
            Price.HasValue ? Math.Round(Price.Value * Services.AppSettingsService.TradeFraction, 2) : null;
        [NotMapped] public string TradeValueDisplay => TradeValue.HasValue ? $"${TradeValue.Value:F2}" : "—";
        [NotMapped]
        public string ColorDisplay
        {
            get
            {
                var distinct = (Colors ?? "").Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.TradeBinder);
    }

    // ── Want List — Want list (cards you are looking to acquire) ──────────────
    public class WantListEntry : IFinishRow
    {
        [Key]
        public int WantListEntryId { get; set; }
        public int PoolId { get; set; }                          // legacy
        public string ScryfallId { get; set; } = string.Empty;

        // ── Embedded card data ──────────────────────────────────────────────
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string ColorIdentity { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public bool IsFoilAvailable { get; set; }
        public bool IsNonFoilAvailable { get; set; }
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;

        // ── Want-specific metadata ──────────────────────────────────────────
        public int Quantity { get; set; } = 1;
        // FINISH MODEL (Phase 2): see TradeBinderEntry — Finish replaces IsFoil,
        // adds etched; IsFoil kept for migration; Price is this finish's value.
        public string Finish { get; set; } = "nonfoil";
        public decimal? Price { get; set; }
        public bool IsFoil { get; set; } = false;   // legacy — migrated into Finish
        public decimal? OfferPrice { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // ── Display-only (not stored): lets the shared grid, gallery, detail
        //    panel, and totals row show these rows like the main collection. ──
        [NotMapped] public int RowIndex { get; set; }
        /// <summary>Filled from the pool on load: the printing exists as etched /
        /// exists ONLY as etched (v1 stored those copies as foil rows).</summary>
        [NotMapped] public bool IsEtched { get; set; }
        [NotMapped] public bool PrintingEtchedOnly { get; set; }
        /// <summary>The finish this row really is (a v1 foil row of an etched-only printing is etched).</summary>
        [NotMapped] public string ShownFinish => CardFinish.Shown(Finish, PrintingEtchedOnly);
        [NotMapped] public string FinishPill => CardFinish.Pill(ShownFinish);
        [NotMapped] public string PriceDisplay => Price.HasValue ? $"${Price.Value:F2}" : "—";
        [NotMapped] public decimal RowValue => (Price ?? 0m) * Quantity;
        [NotMapped] public string RowValueDisplay => Price.HasValue ? $"${RowValue:F2}" : "—";
        [NotMapped] public string DateAddedDisplay => DateAdded.ToString("yyyy-MM-dd");
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
        [NotMapped] public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);
        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;
        [NotMapped] public string OfferPriceDisplay => OfferPrice.HasValue ? $"${OfferPrice.Value:F2}" : string.Empty;
        /// <summary>Your offer is above the market price (shown in amber so you notice before you overpay).</summary>
        [NotMapped] public bool IsOfferOverMarket => OfferPrice.HasValue && Price.HasValue && OfferPrice.Value > Price.Value;
        [NotMapped]
        public string ColorDisplay
        {
            get
            {
                var distinct = (Colors ?? "").Where(c => "WUBRG".Contains(c)).Distinct().ToList();
                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }
        [NotMapped]
        public System.Windows.Media.Brush RowForegroundBrush =>
            BreakersOfE.Services.CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
        [NotMapped]
        public System.Windows.Media.Brush RowBackgroundBrush =>
            BreakersOfE.Services.CardColorService.GetBackground(
                Finish != CardFinish.NonFoil, RowIndex, BreakersOfE.Services.TableType.WantList);
    }

    /// <summary>A collection-type row whose finish display depends on the pool printing.</summary>
    public interface IFinishRow
    {
        string ScryfallId { get; }
        bool IsEtched { set; }
        bool PrintingEtchedOnly { set; }
    }

    // ── Finish constants ───────────────────────────────────────────────────
    /// <summary>
    /// The three card finishes, as stored in the Finish column. Single source
    /// of truth — never hard-code these strings elsewhere. Values match
    /// Scryfall's finish names so pool prices map cleanly (usd → NonFoil,
    /// usd_foil → Foil, usd_etched → Etched).
    /// </summary>
    public static class CardFinish
    {
        public const string NonFoil = "nonfoil";
        public const string Foil = "foil";
        public const string Etched = "etched";

        /// <summary>Short display label for the finish pill: "" / "F" / "E".</summary>
        public static string Pill(string finish) => finish switch
        {
            Foil => "F",
            Etched => "E",
            _ => string.Empty   // non-foil shows no pill
        };

        /// <summary>Full display name.</summary>
        public static string Display(string finish) => finish switch
        {
            Foil => "Foil",
            Etched => "Etched",
            _ => "Non-Foil"
        };

        /// <summary>
        /// THE price rule (one place for the whole app): the USD price of
        /// exactly this finish — non-foil → usd, foil → usd_foil, etched →
        /// usd_etched. No borrowing another finish's price: a foil and an
        /// etched copy of the same printing are different products.
        /// </summary>
        public static decimal? PriceFor(string? finish, decimal? usd, decimal? foil, decimal? etched) =>
            Normalize(finish) switch
            {
                Foil => foil,
                Etched => etched,
                _ => usd,
            };

        /// <summary>
        /// The finish a stored row really is. v1 stored copies of etched-only
        /// printings (no foil version exists, e.g. MUL #66–130) as FOIL rows;
        /// such a row is shown and priced as etched. Nothing in the file changes;
        /// the v1→v2 conversion rewrites these rows later.
        /// </summary>
        public static string Shown(string? storedFinish, bool printingEtchedOnly)
        {
            string f = Normalize(storedFinish);
            return f == Foil && printingEtchedOnly ? Etched : f;
        }

        /// <summary>Owned column text: "3", "3 (1F)", "3 (1F, 1E)", blank when none.</summary>
        public static string OwnedText(int total, int foil, int etched)
        {
            if (total == 0) return string.Empty;
            var parts = new List<string>(2);
            if (foil > 0) parts.Add($"{foil}F");
            if (etched > 0) parts.Add($"{etched}E");
            return parts.Count == 0 ? total.ToString() : $"{total} ({string.Join(", ", parts)})";
        }

        /// <summary>"Non-Foil · Foil · Etched" — the finishes a printing exists in.</summary>
        public static string AvailableText(bool nonFoil, bool foil, bool etched)
        {
            var parts = new List<string>(3);
            if (nonFoil) parts.Add("Non-Foil");
            if (foil) parts.Add("Foil");
            if (etched) parts.Add("Etched");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Unknown";
        }

        /// <summary>
        /// Pool pill: shown only when there is NO non-foil version —
        /// "F" foil-only (or foil + etched), "E" etched-only.
        /// </summary>
        public static string PoolPill(bool nonFoil, bool foil, bool etched) =>
            nonFoil ? string.Empty : foil ? "F" : etched ? "E" : string.Empty;

        /// <summary>Normalizes any legacy/loose value to a canonical finish.</summary>
        public static string Normalize(string? finish)
        {
            if (string.IsNullOrWhiteSpace(finish)) return NonFoil;
            string f = finish.Trim().ToLowerInvariant();
            return f switch
            {
                "foil" => Foil,
                "etched" => Etched,
                "etched foil" => Etched,
                "nonfoil" => NonFoil,
                "non-foil" => NonFoil,
                "normal" => NonFoil,
                _ => NonFoil
            };
        }
    }

    // ── App Settings ─────────────────────────────────────────────────────────
    public class AppSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    // ── Deck Usage ───────────────────────────────────────────────────────────
    /// <summary>
    /// Records that a collection card (by ScryfallId) is used in a specific
    /// deck (by the deck's stable GUID). This REPLACES the old approach of
    /// scanning deck files on disk to figure out "used in decks" — which was
    /// unreliable because deck files can live anywhere. Instead, usage is
    /// recorded in the collection database when the user actually enters a
    /// deck's cards, so only decks the user genuinely entered can appear.
    ///
    /// Keyed on ScryfallId (never PoolId — PoolId is a local pool rowid that
    /// changes when the pool is rebuilt) and DeckId (a GUID stamped at deck
    /// creation, stable across renames and same-name decks).
    /// </summary>
    public class DeckUsage
    {
        [Key]
        public int DeckUsageId { get; set; }

        /// <summary>The card, by its stable Scryfall printing id.</summary>
        public string ScryfallId { get; set; } = string.Empty;

        /// <summary>The deck's stable GUID (never the name — names can change).</summary>
        public string DeckId { get; set; } = string.Empty;

        /// <summary>Deck name, denormalized for display without loading the file.
        /// Refreshed whenever we see the deck; never used as a key.</summary>
        public string DeckName { get; set; } = string.Empty;

        /// <summary>Deck type label for display (Standard / Commander).</summary>
        public string DeckType { get; set; } = string.Empty;

        /// <summary>Non-foil copies of this card used in this deck.</summary>
        public int Quantity { get; set; } = 0;

        /// <summary>Foil copies of this card used in this deck.</summary>
        public int FoilQuantity { get; set; } = 0;

        /// <summary>
        /// Per-deck entry tracking (Phase 2): how many of THIS deck's copies
        /// have been entered into the collection, split by finish. This is what
        /// makes the Deck→Collection guard per-deck: you can enter up to this
        /// deck's Quantity/FoilQuantity, independent of what other decks hold or
        /// what you own for them. A card in multiple decks tracks separately per
        /// deck because DeckUsage is keyed on DeckId + ScryfallId.
        /// </summary>
        public int EnteredNonFoil { get; set; } = 0;
        public int EnteredFoil { get; set; } = 0;
        /// <summary>v2: etched copies claimed (older rows: 0).</summary>
        public int EnteredEtched { get; set; } = 0;

        // ── v2 claims: the EXACT collection row the copies come from ─────
        // One usage row = one deck × one collection row (printing + language
        // + condition, in one collection table), with the claimed copies per
        // finish above. Older v1 rows have these empty: they count as the
        // main collection, English, Near Mint.
        /// <summary>"Collection" (cards) or "CollTokens" (tokens); empty = "Collection".</summary>
        public string CollectionTable { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;

        /// <summary>Mainboard / Commander / Sideboard.</summary>
        public string Category { get; set; } = string.Empty;

        public DateTime DateRecorded { get; set; } = DateTime.Now;

        /// <summary>Total copies (foil + non-foil) used in this deck.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int TotalQuantity => Quantity + FoilQuantity;
    }
}