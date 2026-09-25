using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BreakersOfE.Models;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    /// <summary>
    /// Collection overview: summary numbers, breakdowns by color / rarity /
    /// type / set (bars = card counts, plus value), and the most valuable
    /// cards. Read-only; works on the rows it's given (the Collection grid's
    /// rows on screen, so filters narrow it).
    /// </summary>
    public partial class CollectionStatsWindow : FluentWindow
    {
        private const double MaxBar = 260;   // bar width for the largest count in a section

        public CollectionStatsWindow(IEnumerable<CollectionEntry> rows, bool filtered, int allRows,
                                     Window? owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };

            var list = rows.Where(r => r.Quantity > 0).ToList();
            ScopeText.Text = filtered
                ? $"Filtered: {list.Count:N0} of {allRows:N0} collection rows (clear the grid's filters for the whole collection)."
                : $"Whole collection: {list.Count:N0} rows.";

            Build(list);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Build(List<CollectionEntry> rows)
        {
            int cards = rows.Sum(r => r.Quantity);
            int foil = rows.Where(r => r.Finish != CardFinish.NonFoil).Sum(r => r.Quantity);
            decimal value = rows.Sum(r => r.RowValue);

            SummaryList.ItemsSource = new[]
            {
                new SummaryItem("Cards", cards.ToString("N0")),
                new SummaryItem("Unique printings",
                    rows.Select(r => r.ScryfallId).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0")),
                new SummaryItem("Unique names",
                    rows.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0")),
                new SummaryItem("Foil cards",
                    cards > 0 ? $"{foil:N0} ({foil * 100.0 / cards:0}%)" : "0"),
                new SummaryItem("Total value", $"${value:N2}"),
                new SummaryItem("Average per card", cards > 0 ? $"${value / cards:N2}" : "—"),
                new SummaryItem("Used in decks", rows.Sum(r => r.UsedCount).ToString("N0")),
                new SummaryItem("Available", rows.Sum(r => r.AvailableCount).ToString("N0")),
            };

            // By color: lands are their own bucket; otherwise the card's colors.
            string[] colorOrder = { "White", "Blue", "Black", "Red", "Green", "Multicolor", "Colorless", "Land" };
            ColorBars.ItemsSource = Bars(rows, ColorBucket, colorOrder);

            string[] rarityOrder = { "Common", "Uncommon", "Rare", "Mythic", "Special", "Bonus", "Other" };
            RarityBars.ItemsSource = Bars(rows, r => RarityBucket(r.Rarity), rarityOrder);

            string[] typeOrder = { "Creature", "Planeswalker", "Battle", "Instant", "Sorcery",
                                   "Artifact", "Enchantment", "Land", "Other" };
            TypeBars.ItemsSource = Bars(rows, r => TypeBucket(r.TypeLine), typeOrder);

            // Top 10 sets by value (bars still show card counts).
            var sets = rows.GroupBy(r => string.IsNullOrWhiteSpace(r.SetName) ? r.SetCode : r.SetName)
                .Select(g => (label: g.Key, count: g.Sum(r => r.Quantity), value: g.Sum(r => r.RowValue)))
                .OrderByDescending(x => x.value)
                .Take(10)
                .ToList();
            SetBars.ItemsSource = ToBars(sets);

            // Most valuable cards: by this row's own finish price.
            TopCardsGrid.ItemsSource = rows
                .Where(r => r.Price.HasValue)
                .OrderByDescending(r => r.Price)
                .ThenBy(r => r.Name)
                .Take(25)
                .ToList();
        }

        // ── Buckets ───────────────────────────────────────────────────────
        private static string ColorBucket(CollectionEntry r)
        {
            if (r.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)) return "Land";
            return r.ColorDisplay switch
            {
                "W" => "White",
                "U" => "Blue",
                "B" => "Black",
                "R" => "Red",
                "G" => "Green",
                "M" => "Multicolor",
                _ => "Colorless",
            };
        }

        private static string RarityBucket(string? rarity) => (rarity ?? "").ToLowerInvariant() switch
        {
            "common" => "Common",
            "uncommon" => "Uncommon",
            "rare" => "Rare",
            "mythic" => "Mythic",
            "special" => "Special",
            "bonus" => "Bonus",
            _ => "Other",
        };

        /// <summary>First matching type, so each card counts once.</summary>
        private static string TypeBucket(string? typeLine)
        {
            string t = typeLine ?? "";
            foreach (var type in new[] { "Creature", "Planeswalker", "Battle", "Instant", "Sorcery",
                                         "Artifact", "Enchantment", "Land" })
                if (t.Contains(type, StringComparison.OrdinalIgnoreCase)) return type;
            return "Other";
        }

        // ── Bars ──────────────────────────────────────────────────────────
        private static List<StatBar> Bars(List<CollectionEntry> rows, Func<CollectionEntry, string> bucket,
                                          string[] order)
        {
            var groups = rows.GroupBy(bucket)
                .ToDictionary(g => g.Key, g => (count: g.Sum(r => r.Quantity), value: g.Sum(r => r.RowValue)));
            var ordered = order.Where(groups.ContainsKey)
                .Select(k => (label: k, groups[k].count, groups[k].value))
                .ToList();
            return ToBars(ordered);
        }

        private static List<StatBar> ToBars(List<(string label, int count, decimal value)> items)
        {
            int max = items.Count == 0 ? 0 : items.Max(i => i.count);
            return items.Select(i => new StatBar
            {
                Label = i.label,
                CountText = i.count.ToString("N0"),
                ValueText = $"${i.value:N2}",
                BarWidth = max > 0 ? Math.Max(2, MaxBar * i.count / max) : 0,
            }).ToList();
        }

        public sealed record SummaryItem(string Label, string Value);

        public sealed class StatBar
        {
            public string Label { get; init; } = "";
            public string CountText { get; init; } = "";
            public string ValueText { get; init; } = "";
            public double BarWidth { get; init; }
        }
    }
}