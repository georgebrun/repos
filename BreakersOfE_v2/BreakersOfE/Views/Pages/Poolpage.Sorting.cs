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
    // Edition order toggle and grid sorting (plus the multi-level sort comparer).
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ── Edition sort order toggle ──────────────────────────────────
        private bool _editionChronological = true;

        private void BtnEditionOrder_Click(object sender, RoutedEventArgs e)
        {
            _editionChronological = !_editionChronological;
            if (sender is Button btn)
                btn.Content = _editionChronological ? "Edition: Chrono" : "Edition: A→Z";

            // Re-sort with current settings
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view?.CustomSort is PoolSortComparer existing)
            {
                // Re-apply with toggled edition order
                view.CustomSort = new PoolSortComparer(
                    _lastSortProp ?? "Name", _lastSortAsc, _editionChronological);
            }
            else
            {
                // Default sort with new edition order
                if (view != null)
                    view.CustomSort = new PoolSortComparer(
                        "Name", true, _editionChronological);
            }
            OnSortChanged();
        }

        private string? _lastSortProp;
        private bool _lastSortAsc = true;

        private void SortColumn(string propName, bool ascending)
        {
            _lastSortProp = propName;
            _lastSortAsc = ascending;

            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view == null) return;

            view.CustomSort = new PoolSortComparer(
                propName, ascending, _editionChronological);
            ShowSortArrow();
            SyncSortBox();
            OnSortChanged();
        }

        /// <summary>
        /// Header click: sort with OUR multi-level comparer (natural numbers,
        /// then Edition, then collector number; decks keep the commander on
        /// top) — not WPF's plain text sort, which put 8 after 79 and dropped
        /// the sub-sorts. Click again to reverse.
        /// </summary>
        private void PoolGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            string header = ColumnHeader(e.Column);
            string prop = ColumnToProperty.TryGetValue(header, out var p) ? p
                        : !string.IsNullOrEmpty(e.Column.SortMemberPath) ? e.Column.SortMemberPath
                        : header;
            prop = SortPropFor(prop);
            bool ascending = !(prop == _lastSortProp && _lastSortAsc);
            SortColumn(prop, ascending);
        }

        /// <summary>
        /// Money columns show text ("$0.19", "0.25 tix", "—"); they sort by
        /// the number behind it, so 10.00 tix comes after 9.00 tix. The Sort
        /// list uses the same names, so it and the header arrow stay in step.
        /// </summary>
        private static readonly Dictionary<string, string> SortByNumber = new(StringComparer.Ordinal)
        {
            ["PriceUsdDisplay"] = "PriceUsd",
            ["PriceUsdFoilDisplay"] = "PriceUsdFoil",
            ["PriceUsdEtchedDisplay"] = "PriceUsdEtched",
            ["PriceTixDisplay"] = "PriceTix",
            ["PriceDisplay"] = "Price",
            ["RowValueDisplay"] = "RowValue",
        };

        private static string SortPropFor(string prop) =>
            SortByNumber.TryGetValue(prop, out var raw) ? raw : prop;

        /// <summary>The arrow sits on the column that owns the current sort.</summary>
        private void ShowSortArrow()
        {
            foreach (var col in PoolGrid.Columns)
            {
                string header = ColumnHeader(col);
                string prop = SortPropFor(ColumnToProperty.TryGetValue(header, out var p) ? p : col.SortMemberPath ?? header);
                col.SortDirection = prop == _lastSortProp
                    ? (_lastSortAsc ? ListSortDirection.Ascending : ListSortDirection.Descending)
                    : null;
            }
        }

        /// <summary>
        /// After any re-sort: search index and gallery must follow the new order.
        /// </summary>
        private void OnSortChanged()
        {
            RebuildSearchIndex();
            RebuildGalleryIfVisible();
        }

        private void UpdateFunnelIcon(Button funnelButton, bool active)
        {
            if (funnelButton.Content is TextBlock icon)
            {
                icon.Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA0, 0xFF)) // accent
                    : new SolidColorBrush(Color.FromRgb(0x9F, 0x9F, 0x9F));
                icon.Text = active ? "\uE71C" : "\uE71C"; // same glyph, color signals state
            }
        }

        private void ClearAllFilters_Click(object sender, RoutedEventArgs e)
        {
            _vm.Filters.ClearAll();
            _missingOnly = false;
            BtnMissingOnly.IsChecked = false;
            UpdateChecklist(reapply: false);
            _vm.ApplyFilters();

            // Reset to default multi-level sort (Name → Edition → Collector Number)
            _lastSortProp = "Name";
            _lastSortAsc = true;
            var view = CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                       as ListCollectionView;
            if (view != null)
                view.CustomSort = new PoolSortComparer(
                    "Name", true, _editionChronological);

            // Reset all funnel icons to inactive
            foreach (var btn in FindVisualChildren<Button>(PoolGrid)
                     .Where(b => b.Name == "FunnelButton"))
            {
                UpdateFunnelIcon(btn, false);
            }

            ResetSearch();
            OnSortChanged();
            LoadPanelFromFilters();            // the Filters panel is cleared too
            SyncSortBox();

            // Viewing a set from the set browser: the set filter is gone now.
            if (_inSetsContext) _vm.Title = "Sets — All Cards";
        }
    }

    /// <summary>
    /// Multi-level sort for the pool grid. Primary sort = whatever column the
    /// user clicked. Sub-sorts: Edition (alpha or chrono) → Collector Number.
    /// When no user sort is active, default is Name → Edition → Collector Number.
    /// </summary>
    public class PoolSortComparer : System.Collections.IComparer
    {
        private readonly string _primaryProp;
        private readonly bool _primaryAsc;
        private readonly bool _editionChronological;

        // Cached reflection
        private System.Reflection.PropertyInfo? _primaryPi;
        private System.Reflection.PropertyInfo? _setCodePi;
        private System.Reflection.PropertyInfo? _releasedAtPi;
        private System.Reflection.PropertyInfo? _collNumSortPi;

        private static bool IsCommander(Models.DeckCard c) =>
            c.IsCommander || c.Category == Models.DeckCardCategory.Commander;

        /// <summary>Deck part order: command zone, main deck, sideboard, tokens.</summary>
        private static int PartRank(Models.DeckCard c) =>
            IsCommander(c) ? 0
            : c.IsTokenLine ? 3
            : c.Category == Models.DeckCardCategory.Sideboard ? 2 : 1;

        public PoolSortComparer(string primaryProp, bool ascending,
            bool editionChronological = true)
        {
            _primaryProp = primaryProp;
            _primaryAsc = ascending;
            _editionChronological = editionChronological;
        }

        public int Compare(object? x, object? y)
        {
            if (x == null || y == null) return 0;

            // Decks: grouped by part, whatever the sort — the commander on top,
            // then the main deck, the sideboard, and the tokens last (grid and
            // gallery — the gallery follows the grid's order).
            if (x is Models.DeckCard dx && y is Models.DeckCard dy)
            {
                int cx = PartRank(dx), cy = PartRank(dy);
                if (cx != cy) return cx.CompareTo(cy);
            }

            // Cache property info on first call
            var type = x.GetType();
            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase;
            _primaryPi ??= type.GetProperty(_primaryProp, flags);
            _setCodePi ??= type.GetProperty("SetCode", flags);
            _releasedAtPi ??= type.GetProperty("ReleasedAt", flags);
            _collNumSortPi ??= type.GetProperty("CollectorNumberSort", flags);

            // Primary sort
            int cmp;
            if (_primaryProp.StartsWith(PoolColumnFilters.LegalityPrefix, StringComparison.Ordinal))
            {
                // Legality column: by severity (Ban → Res → No → Legal), then name.
                string key = _primaryProp.Substring(PoolColumnFilters.LegalityPrefix.Length);
                int ra = (x as Models.ILegalityRow)?.Legality[key].SortRank ?? 4;
                int rb = (y as Models.ILegalityRow)?.Legality[key].SortRank ?? 4;
                cmp = ra.CompareTo(rb);
                if (!_primaryAsc) cmp = -cmp;
                if (cmp != 0) return cmp;
                _namePi ??= type.GetProperty("Name", flags);
                cmp = string.Compare(_namePi?.GetValue(x) as string, _namePi?.GetValue(y) as string,
                                     StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            else
            {
                string a = _primaryPi?.GetValue(x)?.ToString() ?? "";
                string b = _primaryPi?.GetValue(y)?.ToString() ?? "";
                if (_primaryProp == "CollectorNumber" && _collNumSortPi != null)
                {
                    // Collector numbers: by their number part (8 before 77,
                    // 123a after 123), then as text.
                    double na = (_collNumSortPi.GetValue(x) as double?) ?? 9999;
                    double nb = (_collNumSortPi.GetValue(y) as double?) ?? 9999;
                    cmp = na.CompareTo(nb);
                    if (cmp == 0) cmp = ColumnFilterState.CompareNatural(a, b);
                }
                else
                {
                    cmp = ColumnFilterState.CompareNatural(a, b);
                }
                if (!_primaryAsc) cmp = -cmp;
                if (cmp != 0) return cmp;
            }

            // Sub-sort 1: Edition
            if (_primaryProp != "SetCode" && _primaryProp != "ReleasedAt")
            {
                if (_editionChronological)
                {
                    string ra = _releasedAtPi?.GetValue(x)?.ToString() ?? "";
                    string rb = _releasedAtPi?.GetValue(y)?.ToString() ?? "";
                    cmp = string.Compare(ra, rb, StringComparison.Ordinal); // ISO dates sort naturally
                    if (cmp != 0) return cmp;
                }
                else
                {
                    string sa = _setCodePi?.GetValue(x)?.ToString() ?? "";
                    string sb = _setCodePi?.GetValue(y)?.ToString() ?? "";
                    cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }

            // Sub-sort 2: Collector Number (numeric)
            if (_primaryProp != "CollectorNumber" && _primaryProp != "CollectorNumberSort")
            {
                double na = (_collNumSortPi?.GetValue(x) as double?) ?? 9999;
                double nb = (_collNumSortPi?.GetValue(y) as double?) ?? 9999;
                cmp = na.CompareTo(nb);
                if (cmp != 0) return cmp;
            }

            // Sub-sort 3: Finish (collection rows) — the non-foil row sits just
            // above the foil row of the same printing. Pool rows have no Finish.
            _finishPi ??= type.GetProperty("Finish", flags);
            if (_finishPi != null && _primaryProp != "Finish" && _primaryProp != "FinishPill")
            {
                cmp = FinishRank(_finishPi.GetValue(x) as string)
                          .CompareTo(FinishRank(_finishPi.GetValue(y) as string));
                if (cmp != 0) return cmp;
            }

            return 0;
        }

        private System.Reflection.PropertyInfo? _finishPi;
        private System.Reflection.PropertyInfo? _namePi;

        private static int FinishRank(string? finish) => finish switch
        {
            Models.CardFinish.Foil => 1,
            Models.CardFinish.Etched => 2,
            _ => 0      // non-foil first
        };
    }
}