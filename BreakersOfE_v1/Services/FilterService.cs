using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BreakersOfE.Services
{
    public static class FilterService
    {
        // ════════════════════════════════════════════════════════════════════
        // POOL CARDS
        // ════════════════════════════════════════════════════════════════════
        public static List<PoolCard> Apply(
            List<PoolCard> cards, FilterState filter, string quickSearch)
        {
            if (cards == null) return new List<PoolCard>();

            var result = cards.AsEnumerable();

            // Quick search box
            if (!string.IsNullOrWhiteSpace(quickSearch))
            {
                string s = filter.CaseInsensitive
                    ? quickSearch.ToLower() : quickSearch;

                result = result.Where(c =>
                {
                    string name = filter.CaseInsensitive
                        ? c.Name.ToLower() : c.Name;
                    return name.Contains(s);
                });
            }

            if (!filter.IsActive) return result.ToList();

            // Set/Edition filter
            if (filter.SelectedSetCodes.Count > 0)
                result = result.Where(c =>
                    filter.SelectedSetCodes.Contains(
                        c.SetCode, StringComparer.OrdinalIgnoreCase));

            // Text search
            result = ApplyTextSearch(result, filter,
                c => c.Name, c => c.OracleText,
                c => c.TypeLine, c => c.FlavorText, c => c.Artist);

            // Color
            if (filter.AnyColorSelected)
                result = result.Where(c =>
                    MatchesColor(c.Colors, filter));

            // Rarity
            if (filter.AnyRaritySelected)
                result = result.Where(c =>
                    MatchesRarity(c.Rarity, filter));

            // Card type
            if (filter.AnyTypeSelected)
                result = result.Where(c =>
                    MatchesType(c.TypeLine, filter));

            // CMC range
            if (filter.CmcRange.IsActive)
                result = result.Where(c =>
                    MatchesCmc(c.ManaValue, filter.CmcRange));

            // Power range
            if (filter.PowerRange.IsActive)
                result = result.Where(c =>
                    MatchesStat(c.Power, filter.PowerRange));

            // Toughness range
            if (filter.ToughnessRange.IsActive)
                result = result.Where(c =>
                    MatchesStat(c.Toughness, filter.ToughnessRange));

            return result.ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // COLLECTION DISPLAY ROWS
        // ════════════════════════════════════════════════════════════════════
        public static List<CollectionDisplayRow> Apply(
            List<CollectionDisplayRow> rows,
            FilterState filter,
            string quickSearch)
        {
            if (rows == null) return new List<CollectionDisplayRow>();

            var result = rows.AsEnumerable();

            // Quick search
            if (!string.IsNullOrWhiteSpace(quickSearch))
            {
                string s = filter.CaseInsensitive
                    ? quickSearch.ToLower() : quickSearch;

                result = result.Where(c =>
                {
                    string name = filter.CaseInsensitive
                        ? c.Name.ToLower() : c.Name;
                    return name.Contains(s);
                });
            }

            if (!filter.IsActive) return result.ToList();

            // Set filter
            if (filter.SelectedSetCodes.Count > 0)
                result = result.Where(c =>
                    filter.SelectedSetCodes.Contains(
                        c.SetCode, StringComparer.OrdinalIgnoreCase));

            // Text search
            result = ApplyTextSearch(result, filter,
                c => c.Name, c => c.OracleText,
                c => c.TypeLine, c => c.FlavorText, c => c.Artist);

            // Color
            if (filter.AnyColorSelected)
                result = result.Where(c =>
                    MatchesColor(c.Colors, filter));

            // Rarity
            if (filter.AnyRaritySelected)
                result = result.Where(c =>
                    MatchesRarity(c.Rarity, filter));

            // Card type
            if (filter.AnyTypeSelected)
                result = result.Where(c =>
                    MatchesType(c.TypeLine, filter));

            // CMC range
            if (filter.CmcRange.IsActive)
                result = result.Where(c =>
                    MatchesCmc(c.ManaValue, filter.CmcRange));

            // Power range
            if (filter.PowerRange.IsActive)
                result = result.Where(c =>
                    MatchesStat(c.Power, filter.PowerRange));

            // Toughness range
            if (filter.ToughnessRange.IsActive)
                result = result.Where(c =>
                    MatchesStat(c.Toughness, filter.ToughnessRange));

            // Condition
            if (filter.AnyConditionSelected)
                result = result.Where(c =>
                    MatchesCondition(c.Condition, filter));

            // Language
            if (filter.AnyLanguageSelected)
                result = result.Where(c =>
                    MatchesLanguage(c.Language, filter));

            // Storage location
            if (!string.IsNullOrWhiteSpace(filter.StorageLocationFilter))
            {
                string loc = filter.CaseInsensitive
                    ? filter.StorageLocationFilter.ToLower()
                    : filter.StorageLocationFilter;
                result = result.Where(c =>
                {
                    string cLoc = filter.CaseInsensitive
                        ? c.StorageLocation.ToLower()
                        : c.StorageLocation;
                    return cLoc.Contains(loc);
                });
            }

            return result.ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // TOKEN CARDS
        // ════════════════════════════════════════════════════════════════════
        public static List<TokenCard> Apply(
            List<TokenCard> cards, FilterState filter, string quickSearch)
        {
            if (cards == null) return new List<TokenCard>();

            var result = cards.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(quickSearch))
            {
                string s = filter.CaseInsensitive
                    ? quickSearch.ToLower() : quickSearch;
                result = result.Where(c =>
                    (filter.CaseInsensitive
                        ? c.Name.ToLower() : c.Name).Contains(s));
            }

            if (!filter.IsActive) return result.ToList();

            if (filter.SelectedSetCodes.Count > 0)
                result = result.Where(c =>
                    filter.SelectedSetCodes.Contains(
                        c.SetCode, StringComparer.OrdinalIgnoreCase));

            if (filter.AnyColorSelected)
                result = result.Where(c =>
                    MatchesColor(c.ColorIdentity, filter));

            return result.ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // SHARED FILTER LOGIC
        // ════════════════════════════════════════════════════════════════════

        // ── Text search ──────────────────────────────────────────────────────
        private static IEnumerable<T> ApplyTextSearch<T>(
            IEnumerable<T> source,
            FilterState filter,
            Func<T, string> getName,
            Func<T, string> getText,
            Func<T, string> getType,
            Func<T, string> getFlavor,
            Func<T, string> getArtist)
        {
            // AND search
            if (!string.IsNullOrWhiteSpace(filter.AndSearch.Value))
            {
                foreach (string term in SplitTerms(filter.AndSearch.Value))
                {
                    string t = filter.CaseInsensitive
                        ? term.ToLower() : term;
                    source = source.Where(c =>
                        SearchFields(c, filter, t, getName, getText,
                            getType, getFlavor, getArtist));
                }
            }

            // OR search
            if (!string.IsNullOrWhiteSpace(filter.OrSearch.Value))
            {
                string[] terms = SplitTerms(filter.OrSearch.Value);
                source = source.Where(c => terms.Any(term =>
                {
                    string t = filter.CaseInsensitive
                        ? term.ToLower() : term;
                    return SearchFields(c, filter, t, getName, getText,
                        getType, getFlavor, getArtist);
                }));
            }

            // NOT search
            if (!string.IsNullOrWhiteSpace(filter.NotSearch.Value))
            {
                foreach (string term in SplitTerms(filter.NotSearch.Value))
                {
                    string t = filter.CaseInsensitive
                        ? term.ToLower() : term;
                    source = source.Where(c =>
                        !SearchFields(c, filter, t, getName, getText,
                            getType, getFlavor, getArtist));
                }
            }

            return source;
        }

        private static bool SearchFields<T>(
            T card, FilterState filter, string term,
            Func<T, string> getName,
            Func<T, string> getText,
            Func<T, string> getType,
            Func<T, string> getFlavor,
            Func<T, string> getArtist)
        {
            bool ci = filter.CaseInsensitive;

            bool Matches(string value)
            {
                string v = ci ? value.ToLower() : value;
                if (filter.AndSearch.WholeWords ||
                    filter.OrSearch.WholeWords ||
                    filter.NotSearch.WholeWords)
                    return v.Split(' ').Any(w => w == term);
                return v.Contains(term);
            }

            if (filter.SearchInName && Matches(getName(card))) return true;
            if (filter.SearchInText && Matches(getText(card))) return true;
            if (filter.SearchInType && Matches(getType(card))) return true;
            if (filter.SearchInFlavor && Matches(getFlavor(card))) return true;
            if (filter.SearchInArtist && Matches(getArtist(card))) return true;

            // Default — search name if nothing selected
            if (!filter.SearchInName && !filter.SearchInText &&
                !filter.SearchInType && !filter.SearchInFlavor &&
                !filter.SearchInArtist)
                return Matches(getName(card));

            return false;
        }

        private static string[] SplitTerms(string value) =>
            value.Split(',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        // ── Color matching ───────────────────────────────────────────────────
        private static bool MatchesColor(string colorIdentity,
            FilterState filter)
        {
            bool isColorless = string.IsNullOrWhiteSpace(colorIdentity);
            bool isMulticolor = !isColorless && colorIdentity.Length > 1;

            if (filter.FilterColorless && isColorless) return true;
            if (filter.FilterMulticolor && isMulticolor) return true;

            // AtMost (Commander): card's colors must ALL be within selected set
            // Colorless cards always pass since they have no color requirements
            if (filter.ColorMatch == ColorMatchMode.AtMost)
            {
                if (isColorless) return filter.FilterColorless || true;
                // Every color in the card must be in the selected set
                foreach (char c in colorIdentity.ToUpper())
                {
                    if (c == 'W' && !filter.FilterWhite) return false;
                    if (c == 'U' && !filter.FilterBlue) return false;
                    if (c == 'B' && !filter.FilterBlack) return false;
                    if (c == 'R' && !filter.FilterRed) return false;
                    if (c == 'G' && !filter.FilterGreen) return false;
                }
                return true;
            }

            if (isColorless || isMulticolor)
            {
                // Multicolor — check match mode
                if (isMulticolor && filter.ColorMatch ==
                    ColorMatchMode.AnyOfSelected)
                {
                    if (filter.FilterWhite && colorIdentity.Contains("W"))
                        return true;
                    if (filter.FilterBlue && colorIdentity.Contains("U"))
                        return true;
                    if (filter.FilterBlack && colorIdentity.Contains("B"))
                        return true;
                    if (filter.FilterRed && colorIdentity.Contains("R"))
                        return true;
                    if (filter.FilterGreen && colorIdentity.Contains("G"))
                        return true;
                }
                return false;
            }

            return filter.ColorMatch switch
            {
                ColorMatchMode.AllSelected =>
                    (!filter.FilterWhite || colorIdentity.Contains("W")) &&
                    (!filter.FilterBlue || colorIdentity.Contains("U")) &&
                    (!filter.FilterBlack || colorIdentity.Contains("B")) &&
                    (!filter.FilterRed || colorIdentity.Contains("R")) &&
                    (!filter.FilterGreen || colorIdentity.Contains("G")),

                ColorMatchMode.ExactlyTheSelected =>
                    BuildSelectedColorString(filter) == colorIdentity,

                _ => // AnyOfSelected
                    (filter.FilterWhite && colorIdentity.Contains("W")) ||
                    (filter.FilterBlue && colorIdentity.Contains("U")) ||
                    (filter.FilterBlack && colorIdentity.Contains("B")) ||
                    (filter.FilterRed && colorIdentity.Contains("R")) ||
                    (filter.FilterGreen && colorIdentity.Contains("G"))
            };
        }

        private static string BuildSelectedColorString(FilterState f)
        {
            string s = string.Empty;
            if (f.FilterWhite) s += "W";
            if (f.FilterBlue) s += "U";
            if (f.FilterBlack) s += "B";
            if (f.FilterRed) s += "R";
            if (f.FilterGreen) s += "G";
            return s;
        }

        // ── Rarity matching ──────────────────────────────────────────────────
        private static bool MatchesRarity(string rarity, FilterState filter)
        {
            return rarity?.ToLower() switch
            {
                "common" => filter.FilterCommon,
                "uncommon" => filter.FilterUncommon,
                "rare" => filter.FilterRare,
                "mythic" => filter.FilterMythic,
                "special" => filter.FilterSpecial,
                "bonus" => filter.FilterSpecial,
                _ => false
            };
        }

        // ── Type matching ────────────────────────────────────────────────────
        private static bool MatchesType(string typeLine, FilterState filter)
        {
            if (string.IsNullOrWhiteSpace(typeLine)) return false;

            string t = typeLine.ToLower();

            if (filter.FilterCreature && t.Contains("creature")) return true;
            if (filter.FilterInstant && t.Contains("instant")) return true;
            if (filter.FilterSorcery && t.Contains("sorcery")) return true;
            if (filter.FilterEnchantment && t.Contains("enchantment")) return true;
            if (filter.FilterArtifact && t.Contains("artifact")) return true;
            if (filter.FilterLand && t.Contains("land")) return true;
            if (filter.FilterPlaneswalker && t.Contains("planeswalker")) return true;
            if (filter.FilterBattle && t.Contains("battle")) return true;
            if (filter.FilterTribal && t.Contains("tribal")) return true;
            if (filter.FilterConspiracy && t.Contains("conspiracy")) return true;
            if (filter.FilterScheme && t.Contains("scheme")) return true;
            if (filter.FilterPlane && t.Contains("plane")) return true;
            if (filter.FilterVanguard && t.Contains("vanguard")) return true;

            return false;
        }

        // ── CMC matching ─────────────────────────────────────────────────────
        private static bool MatchesCmc(double cmc, CmcRange range)
        {
            if (range.Min.HasValue && cmc < range.Min.Value) return false;
            if (range.Max.HasValue && cmc > range.Max.Value) return false;
            return true;
        }

        // ── Stat (P/T) matching ──────────────────────────────────────────────
        private static bool MatchesStat(string statValue, StatRange range)
        {
            if (string.IsNullOrWhiteSpace(statValue)) return false;

            if (statValue == "*" || statValue.Contains("*"))
                return range.IncludeStar;

            if (!int.TryParse(statValue, out int val)) return false;

            if (range.Min.HasValue && val < range.Min.Value) return false;
            if (range.Max.HasValue && val > range.Max.Value) return false;

            return true;
        }

        // ── Condition matching ───────────────────────────────────────────────
        private static bool MatchesCondition(string condition,
            FilterState filter)
        {
            return condition?.ToLower() switch
            {
                "mint" => filter.FilterMint,
                "near mint" => filter.FilterNearMint,
                "nm" => filter.FilterNearMint,
                "excellent" => filter.FilterExcellent,
                "good" => filter.FilterGood,
                "played" => filter.FilterPlayed,
                "poor" => filter.FilterPoor,
                _ => false
            };
        }

        // ── Language matching ────────────────────────────────────────────────
        private static bool MatchesLanguage(string language,
            FilterState filter)
        {
            return language?.ToLower() switch
            {
                "english" => filter.FilterEnglish,
                "french" => filter.FilterFrench,
                "german" => filter.FilterGerman,
                "spanish" => filter.FilterSpanish,
                "italian" => filter.FilterItalian,
                "portuguese" => filter.FilterPortuguese,
                "japanese" => filter.FilterJapanese,
                "korean" => filter.FilterKorean,
                "russian" => filter.FilterRussian,
                "chinese" => filter.FilterChinese,
                _ => false
            };
        }
    }
}