using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BreakersOfE.Models
{
    // ── Pool Collection ─────────────────────────────────────────────────────
    public class CollectionEntry
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
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
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
    }

    // ── Token Collection ────────────────────────────────────────────────────
    public class TokenCollectionEntry
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
    }

    // ── Planar Collection ───────────────────────────────────────────────────
    public class PlanarCollectionEntry
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
    }

    // ── Scheme Collection ───────────────────────────────────────────────────
    public class SchemeCollectionEntry
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
    }

    // ── Vanguard Collection ─────────────────────────────────────────────────
    public class VanguardCollectionEntry
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
    }

    // ── Conspiracy Collection ───────────────────────────────────────────────
    public class ConspiracyCollectionEntry
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
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Art Series Collection ───────────────────────────────────────────────
    public class ArtSeriesCollectionEntry
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
    }

    // ── Trade Binder — Have list (cards you own and want to trade away) ────
    public class TradeBinderEntry
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
        public bool IsFoil { get; set; } = false;
        public string Condition { get; set; } = "Near Mint";
        public decimal? AskingPrice { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }

    // ── Want List — Want list (cards you are looking to acquire) ──────────────
    public class WantListEntry
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
        public bool IsFoil { get; set; } = false;
        public decimal? OfferPrice { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }

    // ── App Settings ─────────────────────────────────────────────────────────
    public class AppSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}