using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BreakersOfE.Services
{
    public static class FilterExpressionService
    {
        // ════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ════════════════════════════════════════════════════════════════════

        public static List<T> Apply<T>(
            List<T> items, FilterNode? root, bool caseInsensitive = true)
        {
            if (items == null || items.Count == 0) return items ?? new();
            if (root == null) return items;
            if (!HasConditions(root)) return items;

            return items.Where(item =>
                EvaluateNode(root, item, caseInsensitive)).ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // NODE EVALUATOR
        // ════════════════════════════════════════════════════════════════════
        private static bool EvaluateNode<T>(
            FilterNode node, T item, bool ci)
        {
            if (node.IsGroup)
                return EvaluateGroup(node, item, ci);
            else
                return EvaluateCondition(node, item, ci);
        }

        private static bool EvaluateGroup<T>(
            FilterNode group, T item, bool ci)
        {
            if (group.Children.Count == 0) return true;

            bool result;

            switch (group.GroupOp)
            {
                case FilterGroupOp.And:
                    result = group.Children.All(
                        c => EvaluateNode(c, item, ci));
                    break;

                case FilterGroupOp.Or:
                    result = group.Children.Any(
                        c => EvaluateNode(c, item, ci));
                    break;

                case FilterGroupOp.NotAnd:
                    // NOT (all conditions true) = at least one false
                    result = !group.Children.All(
                        c => EvaluateNode(c, item, ci));
                    break;

                case FilterGroupOp.NotOr:
                    // NOT (any condition true) = all false
                    result = !group.Children.Any(
                        c => EvaluateNode(c, item, ci));
                    break;

                default:
                    result = true;
                    break;
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        // CONDITION EVALUATOR
        // ════════════════════════════════════════════════════════════════════
        private static bool EvaluateCondition<T>(
            FilterNode cond, T item, bool ci)
        {
            // Get the field value via reflection
            object? rawValue = GetFieldValue(item, cond.Field);
            string strValue = Normalize(rawValue?.ToString(), ci);
            string condVal = Normalize(cond.Value, ci);
            string condVal2 = Normalize(cond.Value2, ci);

            switch (cond.Operator)
            {
                case FilterOperator.Equals:
                    return strValue == condVal;

                case FilterOperator.NotEquals:
                    return strValue != condVal;

                case FilterOperator.LessThan:
                    return CompareNumeric(rawValue, cond.Value) < 0;

                case FilterOperator.LessThanOrEqual:
                    return CompareNumeric(rawValue, cond.Value) <= 0;

                case FilterOperator.GreaterThan:
                    return CompareNumeric(rawValue, cond.Value) > 0;

                case FilterOperator.GreaterThanOrEqual:
                    return CompareNumeric(rawValue, cond.Value) >= 0;

                case FilterOperator.IsLike:
                    return IsLike(strValue, condVal);

                case FilterOperator.IsNotLike:
                    return !IsLike(strValue, condVal);

                case FilterOperator.Contains:
                    return strValue.Contains(condVal);

                case FilterOperator.DoesNotContain:
                    return !strValue.Contains(condVal);

                case FilterOperator.BeginsWith:
                    return strValue.StartsWith(condVal);

                case FilterOperator.EndsWith:
                    return strValue.EndsWith(condVal);

                case FilterOperator.IsBlank:
                    return string.IsNullOrWhiteSpace(strValue);

                case FilterOperator.IsNotBlank:
                    return !string.IsNullOrWhiteSpace(strValue);

                case FilterOperator.IsBetween:
                    return CompareNumeric(rawValue, cond.Value) >= 0 &&
                           CompareNumeric(rawValue, cond.Value2) <= 0;

                case FilterOperator.IsNotBetween:
                    return CompareNumeric(rawValue, cond.Value) < 0 ||
                           CompareNumeric(rawValue, cond.Value2) > 0;

                case FilterOperator.IsAnyOf:
                    {
                        var vals = SplitValues(condVal);
                        return vals.Any(v => strValue == v.Trim());
                    }

                case FilterOperator.IsNoneOf:
                    {
                        var vals = SplitValues(condVal);
                        return vals.All(v => strValue != v.Trim());
                    }

                default:
                    return true;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // REFLECTION FIELD ACCESS
        // ════════════════════════════════════════════════════════════════════
        private static object? GetFieldValue<T>(T item, string fieldName)
        {
            if (item == null) return null;

            // Try property first
            var prop = typeof(T).GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.IgnoreCase);

            if (prop != null)
                return prop.GetValue(item);

            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════
        private static string Normalize(string? value, bool ci)
        {
            if (value == null) return string.Empty;
            return ci ? value.ToLower() : value;
        }

        private static int CompareNumeric(object? rawValue, string condValue)
        {
            if (rawValue == null) return -1;

            if (decimal.TryParse(rawValue.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal left) &&
                decimal.TryParse(condValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out decimal right))
            {
                return left.CompareTo(right);
            }

            // Fall back to string comparison
            string l = rawValue.ToString() ?? string.Empty;
            return string.Compare(l, condValue,
                StringComparison.OrdinalIgnoreCase);
        }

        // Simple SQL LIKE pattern: % = any chars, _ = single char
        private static bool IsLike(string value, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;

            // Convert SQL LIKE to regex-style matching
            string regexPattern = "^" +
                System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("%", ".*")
                    .Replace("_", ".") +
                "$";

            return System.Text.RegularExpressions.Regex.IsMatch(
                value, regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string[] SplitValues(string value) =>
            value.Split(',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        // ── Check if tree has any actual conditions ───────────────────────────
        public static bool HasConditions(FilterNode? node)
        {
            if (node == null) return false;
            if (!node.IsGroup) return true; // it's a condition
            return node.Children.Any(HasConditions);
        }

        // ── Generate human-readable summary of filter ─────────────────────────
        public static string Summarize(FilterNode? root)
        {
            if (root == null || !HasConditions(root))
                return string.Empty;

            return SummarizeNode(root, 0);
        }

        private static string SummarizeNode(FilterNode node, int depth)
        {
            if (!node.IsGroup)
            {
                string val = node.Operator == FilterOperator.IsBlank ||
                             node.Operator == FilterOperator.IsNotBlank
                    ? string.Empty
                    : $" \"{node.ValueDisplay}\"";

                return $"{node.FieldDisplay} {node.OperatorDisplay}{val}";
            }

            if (node.Children.Count == 0) return string.Empty;

            var parts = node.Children
                .Where(HasConditions)
                .Select(c => SummarizeNode(c, depth + 1))
                .Where(s => !string.IsNullOrEmpty(s));

            string joined = string.Join($" {node.GroupOpDisplay} ", parts);

            return depth > 0 ? $"({joined})" : joined;
        }
    }
}