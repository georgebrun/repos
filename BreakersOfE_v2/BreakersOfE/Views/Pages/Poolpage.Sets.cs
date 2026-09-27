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
    // Set checklist and the set browser.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // SET CHECKLIST — while viewing cards from the set browser: printings
        // you own are bright, the rest dimmed (gallery); "Missing Only" hides
        // the ones you own; the status line shows Owned X of Y. By printing,
        // like Set Completion. Read-only.
        // ══════════════════════════════════════════════════════════════════
        private bool _missingOnly;

        private static bool IsMissingPrinting(object card) =>
            card is Models.IOwnedCard c && c.OwnedNonFoil + c.OwnedFoil == 0;

        private static string ChecklistNote(IReadOnlyList<object> rows)
        {
            int total = rows.Count;
            if (total == 0) return "";
            int owned = rows.Count(r => !IsMissingPrinting(r));
            double pct = owned * 100.0 / total;
            return $"   ·   Owned {owned:N0} of {total:N0} ({pct:0.#}%)   ·   Missing {total - owned:N0}";
        }

        /// <summary>
        /// Turn the checklist on or off for the current view. Re-applies the
        /// filters when the Missing Only filter actually changed.
        /// </summary>
        private void UpdateChecklist(bool reapply = true)
        {
            bool checklist = _inSetsContext && !IsBrowser(_viewMode);

            BtnMissingOnly.Visibility = checklist ? Visibility.Visible : Visibility.Collapsed;
            BtnMissingOnly.IsChecked = _missingOnly;
            Gallery.DimWhen = checklist ? IsMissingPrinting : null;

            Func<object, bool>? extra = checklist && _missingOnly ? IsMissingPrinting : null;
            Func<IReadOnlyList<object>, string>? note = checklist ? ChecklistNote : null;
            bool changed = (_vm.ExtraFilter != null) != (extra != null) ||
                           (_vm.StatusNote != null) != (note != null);
            _vm.ExtraFilter = extra;
            _vm.StatusNote = note;

            if (changed && reapply && _vm.AllRows.Count > 0 && !IsBrowser(_viewMode))
                _vm.ApplyFilters();
        }

        private void BtnMissingOnly_Click(object sender, RoutedEventArgs e)
        {
            _missingOnly = BtnMissingOnly.IsChecked == true;
            UpdateChecklist();
        }

        // ══════════════════════════════════════════════════════════════════
        // SET BROWSER — tiles built from the in-memory pool, grouped by set
        // type, newest first. Click a tile → Edition filter + gallery.
        // ══════════════════════════════════════════════════════════════════
        private List<SetTile>? _setTiles;                            // per pool load
        private List<(string section, List<SetTile> tiles)> _setGroups = new();
        private List<SetBrowserRow> _setRows = new();
        private int _setsPerRow = 1;
        private SetTile? _setHighlighted;

        private const double SetTileWidth = 300;   // matches XAML
        private const double SetTileMargin = 8;

        private void BuildSetTilesIfNeeded()
        {
            if (_setTiles != null) return;

            var bySet = new Dictionary<string, SetTile>(StringComparer.OrdinalIgnoreCase);

            // The set browser always sits on the Cards pool, whose rows already
            // carry their owned counts (OwnedCountService, filled on load) — no
            // second collection read, and no reflection over 100K rows.
            foreach (var card in _vm.AllRows.OfType<Models.PoolCard>())
            {
                string code = card.SetCode ?? "";
                if (string.IsNullOrWhiteSpace(code)) continue;
                string date = card.ReleasedAt ?? "";

                if (!bySet.TryGetValue(code, out var tile))
                {
                    tile = new SetTile
                    {
                        Code = code,
                        Name = string.IsNullOrEmpty(card.SetName) ? code : card.SetName,
                        SetType = card.SetType ?? "",
                        SymbolPath = Services.AppFolderService.SetSymbolPath(code),
                        ReleasedAt = date,
                    };
                    bySet[code] = tile;
                }

                tile.CardCount++;
                decimal? usd = card.PriceUsd;
                if (usd is decimal u) tile.TotalValue += u;

                // Completion: one pool row = one printing of this set.
                if (card.OwnedTotal > 0)
                    tile.OwnedCount++;
                else
                    tile.MissingValue += usd ?? card.PriceUsdFoil ?? card.PriceUsdEtched ?? 0m;
                // A set's date = its earliest card (ISO dates compare as text).
                if (!string.IsNullOrEmpty(date) &&
                    (string.IsNullOrEmpty(tile.ReleasedAt) ||
                     string.CompareOrdinal(date, tile.ReleasedAt) < 0))
                    tile.ReleasedAt = date;
            }

            _setTiles = bySet.Values.ToList();
            _setGroups = _setTiles
                .GroupBy(t => SetGrouping.SectionFor(t.SetType))
                .OrderBy(g => SetGrouping.OrderOf(g.Key))
                .Select(g => (section: g.Key, tiles: g.OrderByDescending(t => t.ReleasedAt, StringComparer.Ordinal)
                                      .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                      .ToList()))
                .ToList();
        }

        /// <summary>Set Completion button (set browser): the sortable completion table.</summary>
        private void BtnSetCompletion_Click(object sender, RoutedEventArgs e)
        {
            if (_setTiles == null) return;
            var win = new SetCompletionWindow(_setTiles, Window.GetWindow(this));
            win.SetOpened += tile =>
            {
                win.Close();
                // Left the set browser meanwhile? Go back to it (Cards pool) first.
                if (!_inSetsContext || _currentTag != "Cards") ShowSets();
                OpenSet(tile);
            };
            win.Show();
        }

        /// <summary>Flatten sections into header rows + rows of N tiles.</summary>
        private void RebuildSetRows()
        {
            if (SetList == null) return;

            double avail = SetList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            _setsPerRow = perRow;

            var rows = new List<SetBrowserRow>();
            foreach (var (section, tiles) in _setGroups)
            {
                rows.Add(new SetBrowserRow
                {
                    IsHeader = true,
                    HeaderText = section,
                    HeaderCount = tiles.Count.ToString("N0"),
                });
                for (int i = 0; i < tiles.Count; i += perRow)
                    rows.Add(new SetBrowserRow
                    {
                        Tiles = tiles.GetRange(i, Math.Min(perRow, tiles.Count - i))
                    });
            }
            _setRows = rows;
            SetList.ItemsSource = rows;
        }

        private void SetList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode != PoolViewMode.Sets || !e.WidthChanged || _setGroups.Count == 0) return;
            double avail = SetList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            if (perRow != _setsPerRow) RebuildSetRows();
        }

        private void SetTile_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SetTile tile)
                OpenSet(tile);
        }

        /// <summary>
        /// Show one whole set: clear other filters, filter Edition to this set
        /// (a normal filter — Clear All Filters removes it), open the gallery.
        /// </summary>
        private void OpenSet(SetTile tile)
        {
            _vm.Filters.ClearAll();
            var f = _vm.Filters.GetOrCreate("Edition", ColumnToProperty["Edition"]);
            f.SelectedValues.Clear();
            f.SelectedValues.Add(tile.Code);
            f.AllSelected = false;

            PoolGrid.SelectedItem = null;
            ResetSearch();
            _vm.ApplyFilters();
            RefreshFunnelIcons();
            SetViewMode(SwitchMode);          // grid or gallery, per the switch
            _vm.Title = $"Sets — {tile.Name}";
        }
    }
}
