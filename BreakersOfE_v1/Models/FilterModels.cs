using System.Collections.Generic;

namespace BreakersOfE.Models
{
    // ── Which table the filter applies to ────────────────────────────────────
    public enum FilterTarget
    {
        Pool,
        Collection,
        Tokens,
        Planar,
        Schemes,
        Vanguard,
        Conspiracy,
        ArtSeries
    }

    // ── Text search operator ──────────────────────────────────────────────────
    public enum TextOperator
    {
        And,
        Or,
        Not
    }

    // ── Color match mode ──────────────────────────────────────────────────────
    public enum ColorMatchMode
    {
        AllSelected,       // Card must contain ALL selected colors (Includes)
        AnyOfSelected,     // Card must contain ANY selected color
        ExactlyTheSelected,// Card's colors == selected colors exactly
        AtMost             // Card's colors are a subset of selected (Commander)
    }

    // ── Text search entry ─────────────────────────────────────────────────────
    public class TextSearchEntry
    {
        public TextOperator Operator { get; set; } = TextOperator.And;
        public string Value { get; set; } = string.Empty;
        public bool WholeWords { get; set; } = false;
    }

    // ── CMC / Mana Value range ────────────────────────────────────────────────
    public class CmcRange
    {
        public double? Min { get; set; }
        public double? Max { get; set; }

        public bool IsActive => Min.HasValue || Max.HasValue;
    }

    // ── Individual pip filter ─────────────────────────────────────────────────
    public class PipFilter
    {
        public int? White { get; set; }
        public int? Blue { get; set; }
        public int? Black { get; set; }
        public int? Red { get; set; }
        public int? Green { get; set; }
        public int? Colorless { get; set; }
        public int? GenericX { get; set; }

        public bool IsActive =>
            White.HasValue || Blue.HasValue || Black.HasValue ||
            Red.HasValue || Green.HasValue || Colorless.HasValue ||
            GenericX.HasValue;
    }

    // ── Power/Toughness range ─────────────────────────────────────────────────
    public class StatRange
    {
        public int? Min { get; set; }
        public int? Max { get; set; }
        public bool IncludeStar { get; set; } = true;

        public bool IsActive => Min.HasValue || Max.HasValue;
    }

    // ── Full filter state ─────────────────────────────────────────────────────
    public class FilterState
    {
        // ── Tab 1: Blocks / Formats / Editions ───────────────────────────────
        public List<string> SelectedSetCodes { get; set; } = new();
        public List<string> SelectedFormats { get; set; } = new();
        public List<string> SelectedBlocks { get; set; } = new();

        // ── Tab 2: Cards — Search Fields ─────────────────────────────────────
        public bool SearchInText { get; set; } = false;
        public bool SearchInName { get; set; } = true;
        public bool SearchInType { get; set; } = false;
        public bool SearchInFlavor { get; set; } = false;
        public bool SearchInArtist { get; set; } = false;
        public bool CaseInsensitive { get; set; } = true;

        // Text search (AND / OR / NOT)
        public TextSearchEntry AndSearch { get; set; } = new()
        { Operator = TextOperator.And };
        public TextSearchEntry OrSearch { get; set; } = new()
        { Operator = TextOperator.Or };
        public TextSearchEntry NotSearch { get; set; } = new()
        { Operator = TextOperator.Not };

        // ── Color ─────────────────────────────────────────────────────────────
        public bool FilterWhite { get; set; } = false;
        public bool FilterBlue { get; set; } = false;
        public bool FilterBlack { get; set; } = false;
        public bool FilterRed { get; set; } = false;
        public bool FilterGreen { get; set; } = false;
        public bool FilterMulticolor { get; set; } = false;
        public bool FilterColorless { get; set; } = false;
        public ColorMatchMode ColorMatch { get; set; } =
            ColorMatchMode.AnyOfSelected;

        public bool AnyColorSelected =>
            FilterWhite || FilterBlue || FilterBlack ||
            FilterRed || FilterGreen || FilterMulticolor ||
            FilterColorless;

        // ── Rarity ───────────────────────────────────────────────────────────
        public bool FilterCommon { get; set; } = false;
        public bool FilterUncommon { get; set; } = false;
        public bool FilterRare { get; set; } = false;
        public bool FilterMythic { get; set; } = false;
        public bool FilterSpecial { get; set; } = false;

        public bool AnyRaritySelected =>
            FilterCommon || FilterUncommon || FilterRare ||
            FilterMythic || FilterSpecial;

        // ── Card Type ─────────────────────────────────────────────────────────
        public bool FilterCreature { get; set; } = false;
        public bool FilterInstant { get; set; } = false;
        public bool FilterSorcery { get; set; } = false;
        public bool FilterEnchantment { get; set; } = false;
        public bool FilterArtifact { get; set; } = false;
        public bool FilterLand { get; set; } = false;
        public bool FilterPlaneswalker { get; set; } = false;
        public bool FilterBattle { get; set; } = false;
        public bool FilterTribal { get; set; } = false;
        public bool FilterConspiracy { get; set; } = false;
        public bool FilterScheme { get; set; } = false;
        public bool FilterPlane { get; set; } = false;
        public bool FilterVanguard { get; set; } = false;

        public bool AnyTypeSelected =>
            FilterCreature || FilterInstant || FilterSorcery ||
            FilterEnchantment || FilterArtifact || FilterLand ||
            FilterPlaneswalker || FilterBattle || FilterTribal ||
            FilterConspiracy || FilterScheme || FilterPlane ||
            FilterVanguard;

        // ── Casting Cost ──────────────────────────────────────────────────────
        public CmcRange CmcRange { get; set; } = new();
        public PipFilter PipFilter { get; set; } = new();

        // ── Power / Toughness ─────────────────────────────────────────────────
        public StatRange PowerRange { get; set; } = new();
        public StatRange ToughnessRange { get; set; } = new();

        // ── Collection-only filters ───────────────────────────────────────────
        public bool FilterMint { get; set; } = false;
        public bool FilterNearMint { get; set; } = false;
        public bool FilterExcellent { get; set; } = false;
        public bool FilterGood { get; set; } = false;
        public bool FilterPlayed { get; set; } = false;
        public bool FilterPoor { get; set; } = false;

        public bool AnyConditionSelected =>
            FilterMint || FilterNearMint || FilterExcellent ||
            FilterGood || FilterPlayed || FilterPoor;

        public bool FilterEnglish { get; set; } = false;
        public bool FilterFrench { get; set; } = false;
        public bool FilterGerman { get; set; } = false;
        public bool FilterSpanish { get; set; } = false;
        public bool FilterItalian { get; set; } = false;
        public bool FilterPortuguese { get; set; } = false;
        public bool FilterJapanese { get; set; } = false;
        public bool FilterKorean { get; set; } = false;
        public bool FilterRussian { get; set; } = false;
        public bool FilterChinese { get; set; } = false;

        public bool AnyLanguageSelected =>
            FilterEnglish || FilterFrench || FilterGerman ||
            FilterSpanish || FilterItalian || FilterPortuguese ||
            FilterJapanese || FilterKorean || FilterRussian ||
            FilterChinese;

        public string StorageLocationFilter { get; set; } = string.Empty;

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>True if any filter is active</summary>
        public bool IsActive =>
            SelectedSetCodes.Count > 0 ||
            SelectedFormats.Count > 0 ||
            SelectedBlocks.Count > 0 ||
            !string.IsNullOrWhiteSpace(AndSearch.Value) ||
            !string.IsNullOrWhiteSpace(OrSearch.Value) ||
            !string.IsNullOrWhiteSpace(NotSearch.Value) ||
            AnyColorSelected ||
            AnyRaritySelected ||
            AnyTypeSelected ||
            CmcRange.IsActive ||
            PipFilter.IsActive ||
            PowerRange.IsActive ||
            ToughnessRange.IsActive ||
            AnyConditionSelected ||
            AnyLanguageSelected ||
            !string.IsNullOrWhiteSpace(StorageLocationFilter);

        /// <summary>Human-readable summary of active filters</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();

                if (SelectedSetCodes.Count > 0)
                    parts.Add($"Edition: {string.Join(", ", SelectedSetCodes)}");

                if (SelectedFormats.Count > 0)
                    parts.Add($"Format: {string.Join(", ", SelectedFormats)}");

                if (!string.IsNullOrWhiteSpace(AndSearch.Value))
                    parts.Add($"AND \"{AndSearch.Value}\"");

                if (!string.IsNullOrWhiteSpace(OrSearch.Value))
                    parts.Add($"OR \"{OrSearch.Value}\"");

                if (!string.IsNullOrWhiteSpace(NotSearch.Value))
                    parts.Add($"NOT \"{NotSearch.Value}\"");

                if (AnyColorSelected)
                {
                    var colors = new List<string>();
                    if (FilterWhite) colors.Add("White");
                    if (FilterBlue) colors.Add("Blue");
                    if (FilterBlack) colors.Add("Black");
                    if (FilterRed) colors.Add("Red");
                    if (FilterGreen) colors.Add("Green");
                    if (FilterMulticolor) colors.Add("Multi");
                    if (FilterColorless) colors.Add("Colorless");
                    parts.Add($"Color: {string.Join("/", colors)}");
                }

                if (AnyRaritySelected)
                {
                    var rarities = new List<string>();
                    if (FilterCommon) rarities.Add("C");
                    if (FilterUncommon) rarities.Add("U");
                    if (FilterRare) rarities.Add("R");
                    if (FilterMythic) rarities.Add("M");
                    if (FilterSpecial) rarities.Add("S");
                    parts.Add($"Rarity: {string.Join("/", rarities)}");
                }

                if (AnyTypeSelected)
                {
                    var types = new List<string>();
                    if (FilterCreature) types.Add("Creature");
                    if (FilterInstant) types.Add("Instant");
                    if (FilterSorcery) types.Add("Sorcery");
                    if (FilterEnchantment) types.Add("Enchantment");
                    if (FilterArtifact) types.Add("Artifact");
                    if (FilterLand) types.Add("Land");
                    if (FilterPlaneswalker) types.Add("Planeswalker");
                    if (FilterBattle) types.Add("Battle");
                    parts.Add($"Type: {string.Join("/", types)}");
                }

                if (CmcRange.IsActive)
                {
                    string min = CmcRange.Min.HasValue
                        ? CmcRange.Min.Value.ToString() : "*";
                    string max = CmcRange.Max.HasValue
                        ? CmcRange.Max.Value.ToString() : "*";
                    parts.Add($"CMC: {min}-{max}");
                }

                if (PowerRange.IsActive)
                {
                    string min = PowerRange.Min.HasValue
                        ? PowerRange.Min.Value.ToString() : "*";
                    string max = PowerRange.Max.HasValue
                        ? PowerRange.Max.Value.ToString() : "*";
                    parts.Add($"Power: {min}-{max}");
                }

                if (ToughnessRange.IsActive)
                {
                    string min = ToughnessRange.Min.HasValue
                        ? ToughnessRange.Min.Value.ToString() : "*";
                    string max = ToughnessRange.Max.HasValue
                        ? ToughnessRange.Max.Value.ToString() : "*";
                    parts.Add($"Toughness: {min}-{max}");
                }

                if (AnyConditionSelected)
                {
                    var conds = new List<string>();
                    if (FilterMint) conds.Add("M");
                    if (FilterNearMint) conds.Add("NM");
                    if (FilterExcellent) conds.Add("EX");
                    if (FilterGood) conds.Add("GD");
                    if (FilterPlayed) conds.Add("PL");
                    if (FilterPoor) conds.Add("PR");
                    parts.Add($"Condition: {string.Join("/", conds)}");
                }

                if (!string.IsNullOrWhiteSpace(StorageLocationFilter))
                    parts.Add($"Storage: {StorageLocationFilter}");

                return parts.Count > 0
                    ? string.Join("  \u2022  ", parts)
                    : string.Empty;
            }
        }

        /// <summary>Reset all filters to default state</summary>
        public void Clear()
        {
            SelectedSetCodes.Clear();
            SelectedFormats.Clear();
            SelectedBlocks.Clear();

            SearchInText = false;
            SearchInName = true;
            SearchInType = false;
            SearchInFlavor = false;
            SearchInArtist = false;
            CaseInsensitive = true;

            AndSearch = new TextSearchEntry { Operator = TextOperator.And };
            OrSearch = new TextSearchEntry { Operator = TextOperator.Or };
            NotSearch = new TextSearchEntry { Operator = TextOperator.Not };

            FilterWhite = FilterBlue = FilterBlack = FilterRed =
                FilterGreen = FilterMulticolor = FilterColorless = false;
            ColorMatch = ColorMatchMode.AnyOfSelected;

            FilterCommon = FilterUncommon = FilterRare =
                FilterMythic = FilterSpecial = false;

            FilterCreature = FilterInstant = FilterSorcery =
            FilterEnchantment = FilterArtifact = FilterLand =
            FilterPlaneswalker = FilterBattle = FilterTribal =
            FilterConspiracy = FilterScheme = FilterPlane =
            FilterVanguard = false;

            CmcRange = new CmcRange();
            PipFilter = new PipFilter();
            PowerRange = new StatRange();
            ToughnessRange = new StatRange();

            FilterMint = FilterNearMint = FilterExcellent =
            FilterGood = FilterPlayed = FilterPoor = false;

            FilterEnglish = FilterFrench = FilterGerman =
            FilterSpanish = FilterItalian = FilterPortuguese =
            FilterJapanese = FilterKorean = FilterRussian =
            FilterChinese = false;

            StorageLocationFilter = string.Empty;
        }
    }
}