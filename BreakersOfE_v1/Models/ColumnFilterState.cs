using System.Collections.Generic;

namespace BreakersOfE.Models
{
    // ── Operator for text filter ──────────────────────────────────────────────
    public enum ColumnFilterOperator
    {
        Equals,
        DoesNotEqual,
        BeginsWith,
        EndsWith,
        Contains,
        DoesNotContain,
        IsBlank,
        IsNotBlank,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }

    // ── Single column filter state ────────────────────────────────────────────
    public class ColumnFilterState
    {
        public string ColumnName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;

        // Values tab — selected unique values (OR logic)
        public HashSet<string> SelectedValues { get; set; } = new();
        public bool AllSelected { get; set; } = true;
        public int TotalValueCount { get; set; } = 0; // total available values

        // Text filter tab
        public bool UseTextFilter { get; set; } = false;
        public ColumnFilterOperator TextOperator { get; set; } =
            ColumnFilterOperator.Contains;
        public string TextValue { get; set; } = string.Empty;

        // ── Is this filter active? ────────────────────────────────────────────
        public bool IsActive =>
            (!AllSelected && SelectedValues.Count > 0) ||
            (UseTextFilter && !string.IsNullOrEmpty(TextValue)) ||
            (UseTextFilter && (TextOperator == ColumnFilterOperator.IsBlank ||
                               TextOperator == ColumnFilterOperator.IsNotBlank));

        // ── Apply filter to a value ───────────────────────────────────────────
        public bool Matches(string? value)
        {
            value ??= string.Empty;

            // Text filter takes priority if active
            if (UseTextFilter)
                return MatchesTextFilter(value);

            // Values filter
            if (AllSelected) return true;
            if (SelectedValues.Count == 0) return false;

            return SelectedValues.Contains(value);
        }

        private bool MatchesTextFilter(string value)
        {
            string v = value.ToLower();
            string t = TextValue.ToLower();

            return TextOperator switch
            {
                ColumnFilterOperator.Equals =>
                    v == t,
                ColumnFilterOperator.DoesNotEqual =>
                    v != t,
                ColumnFilterOperator.BeginsWith =>
                    v.StartsWith(t),
                ColumnFilterOperator.EndsWith =>
                    v.EndsWith(t),
                ColumnFilterOperator.Contains =>
                    v.Contains(t),
                ColumnFilterOperator.DoesNotContain =>
                    !v.Contains(t),
                ColumnFilterOperator.IsBlank =>
                    string.IsNullOrWhiteSpace(value),
                ColumnFilterOperator.IsNotBlank =>
                    !string.IsNullOrWhiteSpace(value),
                ColumnFilterOperator.GreaterThan =>
                    CompareNumeric(value, TextValue) > 0,
                ColumnFilterOperator.GreaterThanOrEqual =>
                    CompareNumeric(value, TextValue) >= 0,
                ColumnFilterOperator.LessThan =>
                    CompareNumeric(value, TextValue) < 0,
                ColumnFilterOperator.LessThanOrEqual =>
                    CompareNumeric(value, TextValue) <= 0,
                _ => true
            };
        }

        private static int CompareNumeric(string a, string b)
        {
            if (decimal.TryParse(a,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal da) &&
                decimal.TryParse(b,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal db))
                return da.CompareTo(db);

            return string.Compare(a, b,
                System.StringComparison.OrdinalIgnoreCase);
        }

        // ── Reset ─────────────────────────────────────────────────────────────
        public void Clear()
        {
            SelectedValues.Clear();
            AllSelected = true;
            UseTextFilter = false;
            TextOperator = ColumnFilterOperator.Contains;
            TextValue = string.Empty;
        }

        // ── Natural sort comparer (numeric-aware) ────────────────────────────
        // Sorts numeric strings by value (including $-prefixed prices),
        // non-numeric strings alphabetically after numbers.
        public static int CompareNatural(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            string sa = a.TrimStart('$', ' ').TrimEnd('%');
            string sb = b.TrimStart('$', ' ').TrimEnd('%');
            bool aNum = double.TryParse(sa,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double aVal);
            bool bNum = double.TryParse(sb,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double bVal);
            if (aNum && bNum) return aVal.CompareTo(bVal);
            if (aNum) return -1;
            if (bNum) return 1;
            return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
        }

        // ── Display label for operator ────────────────────────────────────────
        public static string GetOperatorLabel(ColumnFilterOperator op) =>
            op switch
            {
                ColumnFilterOperator.Equals => "Equals",
                ColumnFilterOperator.DoesNotEqual => "Does Not Equal",
                ColumnFilterOperator.BeginsWith => "Begins With",
                ColumnFilterOperator.EndsWith => "Ends With",
                ColumnFilterOperator.Contains => "Contains",
                ColumnFilterOperator.DoesNotContain => "Does Not Contain",
                ColumnFilterOperator.IsBlank => "Is Blank",
                ColumnFilterOperator.IsNotBlank => "Is Not Blank",
                ColumnFilterOperator.GreaterThan => "Greater Than",
                ColumnFilterOperator.GreaterThanOrEqual => "Greater Than Or Equal To",
                ColumnFilterOperator.LessThan => "Less Than",
                ColumnFilterOperator.LessThanOrEqual => "Less Than Or Equal To",
                _ => "Contains"
            };
    }

    // ── Collection of column filters for a grid ───────────────────────────────
    public class GridColumnFilters
    {
        private readonly Dictionary<string, ColumnFilterState> _filters = new();

        public ColumnFilterState GetOrCreate(
            string columnName, string propertyName)
        {
            if (!_filters.TryGetValue(columnName, out var state))
            {
                state = new ColumnFilterState
                {
                    ColumnName = columnName,
                    PropertyName = propertyName
                };
                _filters[columnName] = state;
            }
            return state;
        }

        public ColumnFilterState? Get(string columnName)
        {
            _filters.TryGetValue(columnName, out var state);
            return state;
        }

        public bool HasActiveFilters =>
            _filters.Values.Any(f => f.IsActive);

        public void ClearAll()
        {
            foreach (var f in _filters.Values)
                f.Clear();
        }

        public List<ColumnFilterState> GetActiveFilters() =>
            _filters.Values.Where(f => f.IsActive).ToList();

        // ── Apply all active column filters to a list ─────────────────────────
        public List<T> Apply<T>(List<T> items)
        {
            var activeFilters = _filters.Values
                .Where(f => f.IsActive).ToList();

            if (activeFilters.Count == 0) return items;

            // Cache PropertyInfo lookups — reflection once, not per item
            var propCache = new Dictionary<string, System.Reflection.PropertyInfo?>();
            foreach (var f in activeFilters)
            {
                if (!propCache.ContainsKey(f.PropertyName))
                    propCache[f.PropertyName] = typeof(T).GetProperty(f.PropertyName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase);
            }

            return items.Where(item =>
            {
                foreach (var filter in activeFilters)
                {
                    var prop = propCache[filter.PropertyName];
                    string? value = prop?.GetValue(item)?.ToString();

                    if (!filter.Matches(value))
                        return false;
                }
                return true;
            }).ToList();
        }
    }
}