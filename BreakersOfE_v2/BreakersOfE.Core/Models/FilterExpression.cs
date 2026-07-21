using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BreakersOfE.Models
{
    // ── Group operator ────────────────────────────────────────────────────────
    public enum FilterGroupOp
    {
        And,
        Or,
        NotAnd,
        NotOr
    }

    // ── Condition operator ────────────────────────────────────────────────────
    public enum FilterOperator
    {
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        IsLike,
        IsNotLike,
        Contains,
        DoesNotContain,
        BeginsWith,
        EndsWith,
        IsBlank,
        IsNotBlank,
        IsBetween,
        IsNotBetween,
        IsAnyOf,
        IsNoneOf
    }

    // ── Filter context — which table is being filtered ────────────────────────
    public enum FilterContext
    {
        Pool,
        Planechase,
        Archenemy,
        Vanguard,
        Tokens,
        ArtSeries,
        Conspiracy,
        Collection,
        Deck
    }

    // ── Field definition ──────────────────────────────────────────────────────
    public class FilterField
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DataType { get; set; } = "string"; // string, number, bool, date

        public static List<FilterField> GetFields(FilterContext context)
        {
            var common = new List<FilterField>
            {
                new() { Name = "Name",            DisplayName = "Name",             DataType = "string" },
                new() { Name = "SetCode",         DisplayName = "Edition",          DataType = "string" },
                new() { Name = "SetName",         DisplayName = "Edition Name",     DataType = "string" },
                new() { Name = "SetType",         DisplayName = "Set Type",         DataType = "string" },
                new() { Name = "ColorIdentity",   DisplayName = "Color Identity",   DataType = "string" },
                new() { Name = "TypeLine",        DisplayName = "Type",             DataType = "string" },
                new() { Name = "Rarity",          DisplayName = "Rarity",           DataType = "string" },
                new() { Name = "ManaCost",        DisplayName = "Mana Cost",        DataType = "string" },
                new() { Name = "ManaValue",       DisplayName = "CMC",              DataType = "number" },
                new() { Name = "Power",           DisplayName = "Power",            DataType = "string" },
                new() { Name = "Toughness",       DisplayName = "Toughness",        DataType = "string" },
                new() { Name = "OracleText",      DisplayName = "Oracle Text",      DataType = "string" },
                new() { Name = "FlavorText",      DisplayName = "Flavor Text",      DataType = "string" },
                new() { Name = "Artist",          DisplayName = "Artist",           DataType = "string" },
                new() { Name = "CollectorNumber", DisplayName = "Collector Number", DataType = "string" },
                new() { Name = "Layout",          DisplayName = "Layout",           DataType = "string" },
                new() { Name = "IsFoil",          DisplayName = "Is Foil",          DataType = "bool"   },
                new() { Name = "IsNonFoil",       DisplayName = "Is Non-Foil",      DataType = "bool"   },
            };

            // Pool-only pricing fields
            if (context == FilterContext.Pool)
            {
                common.Add(new() { Name = "PriceUsd", DisplayName = "USD Price", DataType = "number" });
                common.Add(new() { Name = "PriceUsdFoil", DisplayName = "USD Foil Price", DataType = "number" });
            }

            // Collection-only fields
            if (context == FilterContext.Collection)
            {
                common.Add(new() { Name = "Quantity", DisplayName = "Quantity", DataType = "number" });
                common.Add(new() { Name = "FoilQuantity", DisplayName = "Foil Quantity", DataType = "number" });
                common.Add(new() { Name = "Condition", DisplayName = "Condition", DataType = "string" });
                common.Add(new() { Name = "Language", DisplayName = "Language", DataType = "string" });
                common.Add(new() { Name = "StorageLocation", DisplayName = "Storage", DataType = "string" });
                common.Add(new() { Name = "PriceUsd", DisplayName = "USD Price", DataType = "number" });
                common.Add(new() { Name = "PriceUsdFoil", DisplayName = "USD Foil Price", DataType = "number" });
                common.Add(new() { Name = "TotalValue", DisplayName = "Total Value", DataType = "number" });
            }

            return common;
        }

        // Display names for operators based on data type
        public static List<(FilterOperator Op, string Label)>
            GetOperators(string dataType)
        {
            var all = new List<(FilterOperator, string)>
            {
                (FilterOperator.Equals,           "Equals"),
                (FilterOperator.NotEquals,        "Does not equal"),
                (FilterOperator.LessThan,         "Is less than"),
                (FilterOperator.LessThanOrEqual,  "Is less than or equal to"),
                (FilterOperator.GreaterThan,      "Is greater than"),
                (FilterOperator.GreaterThanOrEqual,"Is greater than or equal to"),
                (FilterOperator.IsLike,           "Is like"),
                (FilterOperator.IsNotLike,        "Is not like"),
                (FilterOperator.Contains,         "Contains"),
                (FilterOperator.DoesNotContain,   "Does not contain"),
                (FilterOperator.BeginsWith,       "Begins with"),
                (FilterOperator.EndsWith,         "Ends with"),
                (FilterOperator.IsBlank,          "Is blank"),
                (FilterOperator.IsNotBlank,       "Is not blank"),
                (FilterOperator.IsBetween,        "Is between"),
                (FilterOperator.IsNotBetween,     "Is not between"),
                (FilterOperator.IsAnyOf,          "Is any of"),
                (FilterOperator.IsNoneOf,         "Is none of"),
            };

            if (dataType == "number")
                return all; // all operators valid for numbers

            if (dataType == "bool")
                return new List<(FilterOperator, string)>
                {
                    (FilterOperator.Equals,    "Equals"),
                    (FilterOperator.NotEquals, "Does not equal"),
                    (FilterOperator.IsBlank,   "Is blank"),
                    (FilterOperator.IsNotBlank,"Is not blank"),
                };

            return all; // string gets all
        }
    }

    // ── Single filter node (condition or group) ───────────────────────────────
    public class FilterNode
    {
        // Is this a GROUP node (has children) or a CONDITION node (has field/op/value)?
        public bool IsGroup { get; set; } = false;

        // Group properties
        public FilterGroupOp GroupOp { get; set; } = FilterGroupOp.And;

        // Condition properties
        public string Field { get; set; } = "Name";
        public string FieldDisplay { get; set; } = "Name";
        public FilterOperator Operator { get; set; } = FilterOperator.Contains;
        public string Value { get; set; } = string.Empty;
        public string Value2 { get; set; } = string.Empty; // IsBetween second value

        // Children (only for group nodes)
        public List<FilterNode> Children { get; set; } = new();

        // ── Display helpers ───────────────────────────────────────────────────
        [JsonIgnore]
        public string GroupOpDisplay => GroupOp switch
        {
            FilterGroupOp.And => "AND",
            FilterGroupOp.Or => "OR",
            FilterGroupOp.NotAnd => "NOT AND",
            FilterGroupOp.NotOr => "NOT OR",
            _ => "AND"
        };

        [JsonIgnore]
        public string OperatorDisplay => Operator switch
        {
            FilterOperator.Equals => "Equals",
            FilterOperator.NotEquals => "Does not equal",
            FilterOperator.LessThan => "Is less than",
            FilterOperator.LessThanOrEqual => "Is less than or equal to",
            FilterOperator.GreaterThan => "Is greater than",
            FilterOperator.GreaterThanOrEqual => "Is greater than or equal to",
            FilterOperator.IsLike => "Is like",
            FilterOperator.IsNotLike => "Is not like",
            FilterOperator.Contains => "Contains",
            FilterOperator.DoesNotContain => "Does not contain",
            FilterOperator.BeginsWith => "Begins with",
            FilterOperator.EndsWith => "Ends with",
            FilterOperator.IsBlank => "Is blank",
            FilterOperator.IsNotBlank => "Is not blank",
            FilterOperator.IsBetween => "Is between",
            FilterOperator.IsNotBetween => "Is not between",
            FilterOperator.IsAnyOf => "Is any of",
            FilterOperator.IsNoneOf => "Is none of",
            _ => "Contains"
        };

        [JsonIgnore]
        public string ValueDisplay
        {
            get
            {
                if (Operator == FilterOperator.IsBlank ||
                    Operator == FilterOperator.IsNotBlank)
                    return string.Empty;

                if (Operator == FilterOperator.IsBetween ||
                    Operator == FilterOperator.IsNotBetween)
                    return $"{Value} and {Value2}";

                if (Operator == FilterOperator.IsAnyOf ||
                    Operator == FilterOperator.IsNoneOf)
                    return Value; // comma-separated

                return Value;
            }
        }

        // ── Factory methods ───────────────────────────────────────────────────
        public static FilterNode NewGroup(FilterGroupOp op = FilterGroupOp.And)
            => new() { IsGroup = true, GroupOp = op };

        public static FilterNode NewCondition(
            string field = "Name",
            string fieldDisplay = "Name",
            FilterOperator op = FilterOperator.Contains,
            string value = "")
            => new()
            {
                IsGroup = false,
                Field = field,
                FieldDisplay = fieldDisplay,
                Operator = op,
                Value = value
            };

        // ── Clone ─────────────────────────────────────────────────────────────
        public FilterNode Clone()
        {
            var clone = new FilterNode
            {
                IsGroup = IsGroup,
                GroupOp = GroupOp,
                Field = Field,
                FieldDisplay = FieldDisplay,
                Operator = Operator,
                Value = Value,
                Value2 = Value2
            };
            foreach (var child in Children)
                clone.Children.Add(child.Clone());
            return clone;
        }
    }

    // ── Complete saved filter file ────────────────────────────────────────────
    public class SavedFilter
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public FilterContext Context { get; set; } = FilterContext.Pool;
        public FilterNode RootNode { get; set; } = FilterNode.NewGroup();
        public System.DateTime SavedAt { get; set; } = System.DateTime.Now;
    }
}