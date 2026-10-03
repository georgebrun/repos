using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BreakersOfE.Filtering;
using BreakersOfE.ViewModels;


namespace BreakersOfE.Views.Pages
{
    // Totals row under the grid, and the Statistics button.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // TOTALS ROW (Collection) — one green row under the grid, like v1.
        // Each grid column has a twin here: same width, position, and hidden
        // state, and the row scrolls sideways with the grid. Totals are for the
        // rows on screen, so they follow the filters.
        // ══════════════════════════════════════════════════════════════════
        private readonly List<(DataGridColumn src, DataGridColumn sum)> _totalsColumns = new();
        private bool _totalsSyncQueued;
        private ScrollViewer? _gridScroll;

        // Grid column header → CollectionTotalsRow property.
        private static readonly Dictionary<string, string> TotalsBindings = new()
        {
            ["Name"] = nameof(CollectionTotalsRow.Label),
            ["Qty"] = nameof(CollectionTotalsRow.Qty),
            ["Used"] = nameof(CollectionTotalsRow.Used),
            ["Available"] = nameof(CollectionTotalsRow.Available),
            ["Value"] = nameof(CollectionTotalsRow.Value),
            // Deck totals
            ["Legal"] = nameof(CollectionTotalsRow.Legal),
            ["Non-Foil"] = nameof(CollectionTotalsRow.NonFoil),
            ["Foil"] = nameof(CollectionTotalsRow.Foil),
            ["Etched"] = nameof(CollectionTotalsRow.Etched),
            ["Total"] = nameof(CollectionTotalsRow.Total),
            ["Owned"] = nameof(CollectionTotalsRow.Owned),
            ["Missing"] = nameof(CollectionTotalsRow.Missing),
            ["Claimed"] = nameof(CollectionTotalsRow.Claimed),
            ["Wanted"] = nameof(CollectionTotalsRow.Wanted),
            ["Other Decks"] = nameof(CollectionTotalsRow.OtherDecks),
            ["Asking"] = nameof(CollectionTotalsRow.Asking),
            ["Trade Value"] = nameof(CollectionTotalsRow.TradeValue),
            ["Offer"] = nameof(CollectionTotalsRow.Offer),
        };

        private void BuildTotalsColumns()
        {
            var widthDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
            var visDescriptor = DependencyPropertyDescriptor.FromProperty(
                DataGridColumn.VisibilityProperty, typeof(DataGridColumn));

            foreach (var src in PoolGrid.Columns)
            {
                var sum = new DataGridTextColumn { IsReadOnly = true, Width = src.Width };
                if (TotalsBindings.TryGetValue(ColumnHeader(src), out var prop))
                    sum.Binding = new Binding(prop);
                TotalsGrid.Columns.Add(sum);
                _totalsColumns.Add((src, sum));

                widthDescriptor?.AddValueChanged(src, (_, _) => QueueTotalsSync());
                visDescriptor?.AddValueChanged(src, (_, _) => QueueTotalsSync());
            }
            PoolGrid.ColumnReordered += (_, _) => QueueTotalsSync();
        }

        /// <summary>Coalesce many column changes into one sync after layout.</summary>
        private void QueueTotalsSync()
        {
            if (_totalsSyncQueued) return;
            _totalsSyncQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _totalsSyncQueued = false;
                SyncTotalsColumns();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Totals columns take the grid's widths, hidden state, and order.</summary>
        private void SyncTotalsColumns()
        {
            if (TotalsGrid.Visibility != Visibility.Visible) return;

            foreach (var (src, sum) in _totalsColumns)
            {
                sum.Visibility = src.Visibility;
                double w = src.ActualWidth > 0 ? src.ActualWidth
                         : src.Width.IsAbsolute ? src.Width.Value : 0;
                if (w > 0) sum.Width = new DataGridLength(w);
            }
            // Positions in ascending order so each lands where intended.
            foreach (var (src, sum) in _totalsColumns.OrderBy(p => p.src.DisplayIndex))
            {
                if (src.DisplayIndex >= 0 && src.DisplayIndex < TotalsGrid.Columns.Count)
                    sum.DisplayIndex = src.DisplayIndex;
            }
            MatchTotalsScroll();
        }

        /// <summary>Scroll the totals row sideways with the grid.</summary>
        private void HookTotalsScroll()
        {
            if (_gridScroll != null) return;
            PoolGrid.ApplyTemplate();
            _gridScroll = FindVisualChild<ScrollViewer>(PoolGrid);
            if (_gridScroll == null) return;
            _gridScroll.ScrollChanged += (_, e) =>
            {
                if (e.HorizontalChange != 0 || e.ViewportWidthChange != 0 || e.ExtentWidthChange != 0)
                    MatchTotalsScroll();
            };
        }

        private void MatchTotalsScroll()
        {
            if (_gridScroll == null) return;
            // Same visible width as the grid (the grid loses some to its
            // vertical scroll bar), so both scroll the same distance.
            double gap = Math.Max(0, _gridScroll.ActualWidth - _gridScroll.ViewportWidth);
            TotalsGrid.Margin = new Thickness(0, 0, gap, 0);
            FindVisualChild<ScrollViewer>(TotalsGrid)?
                .ScrollToHorizontalOffset(_gridScroll.HorizontalOffset);
        }

        /// <summary>Recompute totals from the rows currently shown.</summary>
        private void UpdateTotals()
        {
            static string Money(decimal v) => v > 0 ? $"${v:N2}" : "";
            static string Count(int v) => v > 0 ? v.ToString("N0") : "";

            if (_currentTag == DeckTableTag)
            {
                // Tokens (the deck's Tokens part) aren't part of the deck: counted apart.
                var shown = _vm.Items.OfType<Models.DeckCard>().ToList();
                var cards = shown.Where(c => !c.IsTokenLine).ToList();
                int missing = cards.Sum(c => c.CollectionMissing);
                // Cost to finish: missing copies at the card's price (non-foil, else foil).
                decimal missingCost = cards.Sum(c => c.CollectionMissing * (c.PriceUsd ?? c.FoilCopyPrice ?? 0m));
                // Pauper Commander: "restricted" = an uncommon, fine only as the commander.
                int illegal = cards.Count(c =>
                    c.Legality[Models.LegalityAccessor.DeckFormatKey].Status is "banned" or "not_legal" ||
                    (c.DeckFormat == "paupercommander" && !Services.DeckRulesService.IsLeaderCard(c) &&
                     c.Legality[Models.LegalityAccessor.DeckFormatKey].Status == "restricted"));
                TotalsGrid.ItemsSource = new[]
                {
                    new CollectionTotalsRow
                    {
                        // Just the count: Missing, Claimed and the tokens have their own columns / line.
                        Label = $"Totals ({cards.Count:N0} lines)",
                        Owned = cards.Sum(c => c.CollectionOwned).ToString("N0"),
                        Claimed = $"{cards.Sum(c => c.ClaimedCount):N0} of {cards.Sum(c => c.TotalQuantity):N0}",
                        Missing = missing == 0 ? "0" : $"{missing:N0} (${missingCost:N2})",
                        Wanted = Count(cards.Sum(c => c.WantedCount)),
                        OtherDecks = $"{cards.Count(c => c.OtherDecksCount > 0):N0} shared",
                        Legal = illegal == 0 ? "All legal" : $"{illegal} illegal",
                        NonFoil = cards.Sum(c => c.Quantity).ToString("N0"),
                        Foil = cards.Sum(c => c.FoilQuantity).ToString("N0"),
                        Etched = cards.Sum(c => c.EtchedQuantity).ToString("N0"),
                        Total = cards.Sum(c => c.TotalQuantity).ToString("N0"),
                        Value = $"${cards.Sum(c => c.RowValue):N2}",
                    }
                };
                QueueTotalsSync();
                return;
            }

            if (!IsCollectionKind(KindOf(_currentTag)))
            {
                TotalsGrid.ItemsSource = null;
                return;
            }

            // Every collection table (main, tokens, …, Trade Binder, Want List):
            // the tables differ, so read the fields by name; a field a table
            // doesn't have totals as blank.
            var rows = _vm.Items.Cast<object>().Where(r => r != null).ToList();
            int foil = rows.Where(r => (Str(r, "Finish") ?? "nonfoil") != Models.CardFinish.NonFoil)
                           .Sum(r => Int(r, "Quantity"));

            // Online: MTGO values are in event tickets; Arena has no prices or foils.
            var kind = KindOf(_currentTag);
            decimal value = rows.Sum(r => Dec(r, "RowValue"));
            TotalsGrid.ItemsSource = new[]
            {
                new CollectionTotalsRow
                {
                    Label = kind == TableKind.ArenaCollection
                        ? $"Totals ({rows.Count:N0} rows)"
                        : $"Totals ({rows.Count:N0} rows, {foil:N0} foil)",
                    Qty = rows.Sum(r => Int(r, "Quantity")).ToString("N0"),
                    Used = Count(rows.Sum(r => Int(r, "UsedCount"))),
                    Available = Count(rows.Sum(r => Int(r, "AvailableCount"))),
                    Value = kind switch
                    {
                        TableKind.MtgoCollection => $"{value:N2} tix",
                        TableKind.ArenaCollection => "",
                        _ => $"${value:N2}",
                    },
                    // Asking / offer / trade value are per copy. Trade Binder:
                    // no asking price = the market price.
                    Asking = kind == TableKind.TradeBinder
                        ? Money(rows.Sum(r => ((Val(r, "AskingPrice") as decimal?) ?? (Val(r, "Price") as decimal?) ?? 0m) * Int(r, "Quantity")))
                        : Money(rows.Sum(r => Dec(r, "AskingPrice") * Int(r, "Quantity"))),
                    TradeValue = Money(rows.Sum(r => Dec(r, "TradeValue") * Int(r, "Quantity"))),
                    Offer = Money(rows.Sum(r => Dec(r, "OfferPrice") * Int(r, "Quantity"))),
                }
            };
            QueueTotalsSync();
        }

        // ── Field readers for the totals (cached per type + property) ──
        private static readonly Dictionary<(Type, string), System.Reflection.PropertyInfo?> _totalsProps = new();

        private static object? Val(object row, string prop)
        {
            var key = (row.GetType(), prop);
            if (!_totalsProps.TryGetValue(key, out var pi))
                _totalsProps[key] = pi = row.GetType().GetProperty(prop);
            return pi?.GetValue(row);
        }
        private static int Int(object row, string prop) => Val(row, prop) is int i ? i : 0;
        private static decimal Dec(object row, string prop) => Val(row, prop) switch
        {
            decimal d => d,
            _ => 0m,
        };
        private static string? Str(object row, string prop) => Val(row, prop) as string;

        /// <summary>Statistics button: overview of the rows on screen (Collection).</summary>
        private void BtnStatistics_Click(object sender, RoutedEventArgs e)
        {
            // Deck: statistics for the whole open deck.
            if (_currentTag == DeckTableTag)
            {
                if (_openDeck != null)
                    new DeckStatsWindow(_openDeck, Window.GetWindow(this), _checkAsFormat).Show();
                return;
            }
            if (_currentTag != Services.CollectionEditService.CardsTable) return;
            var rows = _vm.Items.OfType<Models.CollectionEntry>().ToList();
            new CollectionStatsWindow(rows, _vm.Filters.HasActiveFilters, _vm.AllRows.Count,
                                      Window.GetWindow(this)).Show();
        }
    }

    /// <summary>The Collection totals row's values (display strings).</summary>
    public sealed class CollectionTotalsRow
    {
        public string Label { get; init; } = "";
        public string Qty { get; init; } = "";
        public string Used { get; init; } = "";
        public string Available { get; init; } = "";
        public string Value { get; init; } = "";
        // Deck totals
        public string Legal { get; init; } = "";
        public string NonFoil { get; init; } = "";
        public string Foil { get; init; } = "";
        public string Etched { get; init; } = "";
        public string Total { get; init; } = "";
        public string Owned { get; init; } = "";
        public string OtherDecks { get; init; } = "";
        public string Missing { get; init; } = "";
        public string Claimed { get; init; } = "";
        public string Wanted { get; init; } = "";
        public string Asking { get; init; } = "";
        public string TradeValue { get; init; } = "";
        public string Offer { get; init; } = "";
    }
}
