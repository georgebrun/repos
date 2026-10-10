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
    public class PlanarCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

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
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }

    // ── Archenemy Schemes ────────────────────────────────────────────────────
    public class SchemeCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

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
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }

    // ── Vanguard ─────────────────────────────────────────────────────────────
    public class VanguardCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string HandModifier { get; set; } = string.Empty;
        public string LifeModifier { get; set; } = string.Empty;

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
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }

    // ── Art Series ───────────────────────────────────────────────────────────
    public class ArtSeriesCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

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
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }

    // ── Front Cards ──────────────────────────────────────────────────────────
    // The theme cards at the front of Jumpstart and similar packs (Scryfall
    // layout "front_card"). Not game cards; their own table, like Art Series.
    public class FrontCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
    {
        [Key] public int FrontCardId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        /// <summary>The theme text ("Theme color: {W}" …).</summary>
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

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
        public string SetSymbolPath => Services.AppFolderService.SetSymbolPath(SetCode);

        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }

    // ── Conspiracy Card ────────────────────────────────────────────────────────
    // Conspiracy cards are stored separately (not in PoolCards) because they
    // are only playable in Conspiracy draft formats.
    public class ConspiracyCard : IOwnedCard, System.ComponentModel.INotifyPropertyChanged
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
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped] public string Power => string.Empty;
        [NotMapped] public string Toughness => string.Empty;
        [NotMapped] public string PowerToughness => string.Empty;
        [NotMapped] public bool IsLand => false;
        [NotMapped] public bool IsCreature => false;

        // Row text in the card's colour (was missing: Conspiracy rows took the theme's text colour).
        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(Colors, ColorIdentity, TypeLine);
    }
}