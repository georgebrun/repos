using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace BreakersOfE.Filtering
{
    /// <summary>
    /// Text-filter operators offered on the Text Filters tab.
    /// These don't create a second "mode" — they compute which distinct
    /// values match, and those values become the checked set. There is only
    /// ever ONE source of truth: SelectedValues.
    /// </summary>
    public enum TextFilterOperator
    {
        Contains,
        DoesNotContain,
        Equals,
        DoesNotEqual,
        BeginsWith,
        EndsWith,
        IsBlank,
        IsNotBlank,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }

    /// <summary>
    /// One column's filter state.
    ///
    /// UNIFIED MODEL — the single source of truth is <see cref="SelectedValues"/>
    /// together with <see cref="AllSelected"/>. A text filter is NOT a separate
    /// mode; applying one simply replaces the checked set with the values that
    /// match the text rule. This makes the old "clear didn't clear because the
    /// other mode was still active" bug structurally impossible.
    /// </summary>
    public class ColumnFilterState
    {
        public string ColumnName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>The checked values. Authoritative when AllSelected is false.</summary>
        public HashSet<string> SelectedValues { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>True = no filtering on this column (everything passes).</summary>
        public bool AllSelected { get; set; } = true;

        /// <summary>Active = actually narrowing the data.</summary>
        public bool IsActive => !AllSelected;

        /// <summary>Does a row's value for this column pass the filter?</summary>
        public bool Matches(string? value)
        {
            if (AllSelected) return true;
            value ??= string.Empty;
            return SelectedValues.Contains(value);
        }

        /// <summary>Reset to the inert "everything passes" state.</summary>
        public void Clear()
        {
            SelectedValues.Clear();
            AllSelected = true;
        }

        /// <summary>
        /// Apply a text rule against the given distinct values and store the
        /// matching ones as the checked set. This is how the Text Filters tab
        /// feeds the single source of truth.
        /// </summary>
        public void ApplyTextRule(
            TextFilterOperator op, string text, IEnumerable<string> distinctValues)
        {
            var matched = distinctValues.Where(v => TextMatch(v, op, text));
            SelectedValues = new HashSet<string>(matched, StringComparer.Ordinal);
            AllSelected = false; // a text rule always narrows
        }

        private static bool TextMatch(string? value, TextFilterOperator op, string text)
        {
            value ??= string.Empty;
            string v = value.ToLowerInvariant();
            string t = (text ?? string.Empty).ToLowerInvariant();

            return op switch
            {
                TextFilterOperator.Contains => v.Contains(t),
                TextFilterOperator.DoesNotContain => !v.Contains(t),
                TextFilterOperator.Equals => v == t,
                TextFilterOperator.DoesNotEqual => v != t,
                TextFilterOperator.BeginsWith => v.StartsWith(t),
                TextFilterOperator.EndsWith => v.EndsWith(t),
                TextFilterOperator.IsBlank => string.IsNullOrWhiteSpace(value),
                TextFilterOperator.IsNotBlank => !string.IsNullOrWhiteSpace(value),
                TextFilterOperator.GreaterThan => CompareNumeric(value, text) > 0,
                TextFilterOperator.GreaterThanOrEqual => CompareNumeric(value, text) >= 0,
                TextFilterOperator.LessThan => CompareNumeric(value, text) < 0,
                TextFilterOperator.LessThanOrEqual => CompareNumeric(value, text) <= 0,
                _ => true
            };
        }

        private static int CompareNumeric(string a, string b)
        {
            if (decimal.TryParse(a.TrimStart('$', ' '), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var da) &&
                decimal.TryParse(b.TrimStart('$', ' '), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var db))
                return da.CompareTo(db);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string OperatorLabel(TextFilterOperator op) => op switch
        {
            TextFilterOperator.Contains => "Contains",
            TextFilterOperator.DoesNotContain => "Does Not Contain",
            TextFilterOperator.Equals => "Equals",
            TextFilterOperator.DoesNotEqual => "Does Not Equal",
            TextFilterOperator.BeginsWith => "Begins With",
            TextFilterOperator.EndsWith => "Ends With",
            TextFilterOperator.IsBlank => "Is Blank",
            TextFilterOperator.IsNotBlank => "Is Not Blank",
            TextFilterOperator.GreaterThan => "Greater Than",
            TextFilterOperator.GreaterThanOrEqual => "Greater Than Or Equal",
            TextFilterOperator.LessThan => "Less Than",
            TextFilterOperator.LessThanOrEqual => "Less Than Or Equal",
            _ => "Contains"
        };

        /// <summary>Numeric-aware natural sort (1,2,10 not 1,10,2; handles $ and %).</summary>
        public static int CompareNatural(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            string sa = a.TrimStart('$', ' ').TrimEnd('%');
            string sb = b.TrimStart('$', ' ').TrimEnd('%');
            bool aNum = double.TryParse(sa, NumberStyles.Any,
                CultureInfo.InvariantCulture, out double av);
            bool bNum = double.TryParse(sb, NumberStyles.Any,
                CultureInfo.InvariantCulture, out double bv);
            if (aNum && bNum) return av.CompareTo(bv);
            if (aNum) return -1;
            if (bNum) return 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Owns every column's filter for one grid, plus the shared plumbing:
    /// distinct-value gathering with Excel-style cascade, and applying all
    /// active filters to an in-memory list. This is the logic that used to be
    /// scattered across MainWindow in v1 — now in one testable place.
    /// </summary>
    public class PoolColumnFilters
    {
        private readonly Dictionary<string, ColumnFilterState> _filters =
            new(StringComparer.Ordinal);

        // Reflection cache: property lookups happen once per property, not per row.
        private readonly Dictionary<string, PropertyInfo?> _propCache =
            new(StringComparer.Ordinal);

        public ColumnFilterState GetOrCreate(string columnName, string propertyName)
        {
            if (!_filters.TryGetValue(columnName, out var s))
            {
                s = new ColumnFilterState
                {
                    ColumnName = columnName,
                    PropertyName = propertyName
                };
                _filters[columnName] = s;
            }
            return s;
        }

        public ColumnFilterState? Get(string columnName) =>
            _filters.TryGetValue(columnName, out var s) ? s : null;

        public bool HasActiveFilters => _filters.Values.Any(f => f.IsActive);

        public IEnumerable<ColumnFilterState> ActiveFilters =>
            _filters.Values.Where(f => f.IsActive);

        public void ClearAll()
        {
            foreach (var f in _filters.Values) f.Clear();
        }

        /// <summary>
        /// Property-name prefix for legality columns: "Legality.commander" reads
        /// the row's Legality["commander"] chip text (Legal / Ban / Res / No).
        /// </summary>
        public const string LegalityPrefix = "Legality.";

        /// <summary>A row's display value for a column (property or legality).</summary>
        private string? ValueOf(object item, string propertyName)
        {
            if (propertyName.StartsWith(LegalityPrefix, StringComparison.Ordinal))
                return item is Models.ILegalityRow row
                    ? row.Legality[propertyName.Substring(LegalityPrefix.Length)].Text
                    : null;
            return PropFor(item.GetType(), propertyName)?.GetValue(item)?.ToString();
        }

        private PropertyInfo? PropFor(Type t, string propertyName)
        {
            string key = t.FullName + "::" + propertyName;
            if (!_propCache.TryGetValue(key, out var p))
            {
                p = t.GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                _propCache[key] = p;
            }
            return p;
        }

        /// <summary>
        /// Apply all active column filters to a source list, returning the
        /// rows that pass every active filter (AND across columns).
        /// </summary>
        public List<object> Apply(IEnumerable<object> source)
        {
            var active = ActiveFilters.ToList();
            var list = source as List<object> ?? source.ToList();
            if (active.Count == 0) return list;

            return list.Where(item =>
            {
                foreach (var f in active)
                {
                    if (!f.Matches(ValueOf(item, f.PropertyName))) return false;
                }
                return true;
            }).ToList();
        }

        /// <summary>
        /// Excel-style distinct values for one column: the values that remain
        /// available once every OTHER active column filter is applied. Any
        /// values the user already checked for THIS column are merged back in
        /// so they can still be seen and unchecked.
        /// </summary>
        public List<string> DistinctValuesFor(
            string columnName, string propertyName, IEnumerable<object> source)
        {
            var all = source as List<object> ?? source.ToList();

            var others = ActiveFilters
                .Where(f => f.ColumnName != columnName)
                .ToList();

            IEnumerable<object> cascaded = all;
            if (others.Count > 0)
            {
                cascaded = all.Where(item =>
                {
                    foreach (var f in others)
                    {
                        if (!f.Matches(ValueOf(item, f.PropertyName))) return false;
                    }
                    return true;
                });
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in cascaded)
                set.Add(ValueOf(item, propertyName) ?? string.Empty);

            // Merge previously-checked values for this column so they stay visible
            var self = Get(columnName);
            if (self is { IsActive: true })
                set.UnionWith(self.SelectedValues);

            return set
                .OrderBy(v => v, Comparer<string>.Create(ColumnFilterState.CompareNatural))
                .ToList();
        }
    }
}