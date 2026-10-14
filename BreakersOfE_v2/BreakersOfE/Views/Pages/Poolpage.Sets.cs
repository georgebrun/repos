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
using Microsoft.EntityFrameworkCore;


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
            card is Models.IOwnedCard c && c.OwnedNonFoil + c.OwnedFoil + c.OwnedEtched == 0;

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
        private List<SetTile>? _setTiles;                            // per pool load (the Cards pool's sets)
        /// <summary>What the Sets page shows: releases (a set with its Commander decks, promos, tokens …) or single sets.</summary>
        private List<SetTile> _releaseTiles = new();
        /// <summary>The release whose page is open (its parts), or null for all releases.</summary>
        private string? _openRelease;
        /// <summary>A part's cards are open, opened from this release's page ("◀ release" goes back to it).</summary>
        private string? _partOfRelease;
        private List<(string section, List<SetTile> tiles)> _setGroups = new();
        private List<SetBrowserRow> _setRows = new();
        private int _setsPerRow = 1;
        private SetTile? _setHighlighted;

        private const double SetTileWidth = 300;   // matches XAML
        private const double SetTileMargin = 8;

        private void BuildSetTilesIfNeeded()
        {
            if (_setTiles != null || _currentTag != "Cards") return;      // tiles come from the Cards pool

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

            // Releases, from Scryfall's set families (saved by Update Database).
            // Without that list every set is its own tile, as before.
            if (Services.SetFamilies.Available)
            {
                var all = new List<SetTile>(_setTiles);
                all.AddRange(OtherTableTiles());
                _releaseTiles = all
                    .GroupBy(t => Services.SetFamilies.ReleaseOf(t.Code), StringComparer.OrdinalIgnoreCase)
                    .Select(g => MakeRelease(g.Key, g.ToList()))
                    .ToList();
            }
            else _releaseTiles = _setTiles.ToList();

            _setGroups = _releaseTiles
                .GroupBy(t => SetGrouping.SectionFor(t.SetType))
                .OrderBy(g => SetGrouping.OrderOf(g.Key))
                .Select(g => (section: g.Key, tiles: g.OrderByDescending(t => t.ReleasedAt, StringComparer.Ordinal)
                                      .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                      .ToList()))
                .ToList();
        }

        /// <summary>
        /// One release from its sets: the face is the release's own set (else the
        /// likeliest main set); its tile adds up every part. A release of one set
        /// is just that set's tile.
        /// </summary>
        private static SetTile MakeRelease(string root, List<SetTile> parts)
        {
            var main = parts.FirstOrDefault(p => p.Table == "Cards" && string.Equals(p.Code, root, StringComparison.OrdinalIgnoreCase))
                       ?? parts.OrderBy(SetGrouping.MainRank)
                               .ThenBy(p => p.ReleasedAt, StringComparer.Ordinal)
                               .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                               .First();
            foreach (var p in parts)
            {
                p.Label = SetGrouping.LabelFor(p.Table, p.SetType, ReferenceEquals(p, main));
                p.PartsText = "";
            }
            if (parts.Count == 1) return main;

            var ordered = parts
                .OrderBy(p => ReferenceEquals(p, main) ? 0 : 1)
                .ThenBy(p => SetGrouping.LabelOrder(p.Label))
                .ThenBy(p => p.ReleasedAt, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var p in ordered) p.PartsText = p.Label;     // shown on the release page

            return new SetTile
            {
                Code = main.Code,
                Name = main.Name,
                SetType = main.SetType,
                SymbolPath = main.SymbolPath,
                ReleasedAt = main.ReleasedAt,
                Table = "Release",
                CardCount = ordered.Sum(p => p.CardCount),
                TotalValue = ordered.Sum(p => p.TotalValue),
                OwnedCount = ordered.Sum(p => p.OwnedCount),
                MissingValue = ordered.Sum(p => p.MissingValue),
                Parts = ordered,
                PartsText = string.Join(" · ", ordered.Select(p => p.Label).Distinct()),
            };
        }

        /// <summary>
        /// Set tiles from the other pool tables a release can have parts in:
        /// tokens, art series, oversized and front cards (each with your
        /// owned counts from its own collection table).
        /// </summary>
        private static List<SetTile> OtherTableTiles()
        {
            var list = new List<SetTile>();
            try
            {
                using var db = new Data.AppDbContext();
                void Add(string table, List<object> rows, Func<object, (string Code, string Name, string Type, string Date, decimal? Price)> get)
                {
                    Services.OwnedCountService.Fill(table, rows);
                    var by = new Dictionary<string, SetTile>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in rows)
                    {
                        var (code, name, type, date, price) = get(r);
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        if (!by.TryGetValue(code, out var tile))
                            by[code] = tile = new SetTile
                            {
                                Code = code,
                                Name = string.IsNullOrEmpty(name) ? code : name,
                                SetType = type ?? "",
                                SymbolPath = Services.AppFolderService.SetSymbolPath(code),
                                ReleasedAt = date ?? "",
                                Table = table,
                            };
                        tile.CardCount++;
                        if (price is decimal v) tile.TotalValue += v;
                        bool owned = r is Models.IOwnedCard oc && oc.OwnedNonFoil + oc.OwnedFoil + oc.OwnedEtched > 0;
                        if (owned) tile.OwnedCount++;
                        else tile.MissingValue += price ?? 0m;
                        if (!string.IsNullOrEmpty(date) &&
                            (string.IsNullOrEmpty(tile.ReleasedAt) || string.CompareOrdinal(date, tile.ReleasedAt) < 0))
                            tile.ReleasedAt = date;
                    }
                    list.AddRange(by.Values);
                }

                Add("Tokens", db.TokenCards.AsNoTracking().ToList().Cast<object>().ToList(),
                    r => { var c = (Models.TokenCard)r; return (c.SetCode, c.SetName, c.SetType, c.ReleasedAt, null); });
                Add("ArtSeries", db.ArtSeriesCards.AsNoTracking().ToList().Cast<object>().ToList(),
                    r => { var c = (Models.ArtSeriesCard)r; return (c.SetCode, c.SetName, c.SetType, c.ReleasedAt, null); });
                Add("Oversized", db.OversizedCards.AsNoTracking().ToList().Cast<object>().ToList(),
                    r => { var c = (Models.OversizedCard)r; return (c.SetCode, c.SetName, c.SetType, c.ReleasedAt, c.PriceUsd ?? c.PriceUsdFoil ?? c.PriceUsdEtched); });
                Add("FrontCards", db.FrontCards.AsNoTracking().ToList().Cast<object>().ToList(),
                    r => { var c = (Models.FrontCard)r; return (c.SetCode, c.SetName, c.SetType, c.ReleasedAt, null); });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Release parts: {ex.Message}");
            }
            return list;
        }

        /// <summary>The release with this code (a release of several parts), or null.</summary>
        private SetTile? FindRelease(string? code) =>
            code == null ? null : _releaseTiles.FirstOrDefault(t => t.IsRelease && string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>"Sets", or "Sets — Adventures in the Forgotten Realms" on a release page.</summary>
        private string SetsTitle() => FindRelease(_openRelease) is { } r ? $"Sets — {r.Name}" : "Sets";

        /// <summary>"53 parts" on a release page; "812 releases" (or "… sets" without the families) on the Sets page.</summary>
        private string SetsStatus()
        {
            if (FindRelease(_openRelease) is { } r) return r.Parts.Count == 1 ? "1 part" : $"{r.Parts.Count:N0} parts";
            bool releases = _releaseTiles.Any(t => t.IsRelease);
            return $"{_releaseTiles.Count:N0} {(releases ? "releases" : "sets")}";
        }

        /// <summary>
        /// Edit → Sets: the same set and release tiles, opening each part as
        /// card pictures only (with − / + under each); no Grid / Gallery switch.
        /// </summary>
        public void ShowSetsForEdit()
        {
            _fixedGallery = true;
            _embeddedGallery = true;
            ShowSets();
        }

        /// <summary>Edit → Sets: copies were added or removed, so the tiles' owned counts are rebuilt next time they show.</summary>
        public void SetCountsChanged() => _setTiles = null;

        /// <summary>A release's page: its parts as tiles (set, Commander, promos, tokens …).</summary>
        private void ShowRelease(string code)
        {
            _openRelease = code;
            _partOfRelease = null;
            ResetSearch();
            SetViewMode(PoolViewMode.Sets);
        }

        /// <summary>
        /// Open one set's cards. Cards-pool sets open here (the Cards pool is
        /// loaded); a part in another table (tokens, oversized …) loads that
        /// table, filtered to the set.
        /// </summary>
        private void OpenPart(SetTile tile)
        {
            _partOfRelease = _openRelease;          // from a release page: "◀ release" goes back to it
            _openRelease = null;
            if (tile.Table == "Cards" || tile.Table == "Release")
            {
                OpenSet(tile);
                return;
            }

            _vm.UseFilters(SetsFilterKey);
            _vm.Filters.ClearAll();
            var f = _vm.Filters.GetOrCreate("Edition", ColumnToProperty["Edition"]);
            f.SelectedValues.Clear();
            f.SelectedValues.Add(tile.Code);
            f.AllSelected = false;

            PoolGrid.SelectedItem = null;
            ResetSearch();
            LoadPoolCore(tile.Table);               // the table's rows arrive with the set's filter on
            RefreshFunnelIcons();
            SetViewMode(SwitchMode);
            _vm.Title = $"Sets — {tile.Name}";
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
            // A release page: its parts under the release's name; else every section.
            var groups = FindRelease(_openRelease) is { } release
                ? new List<(string section, List<SetTile> tiles)> { (release.Name, release.Parts) }
                : _setGroups;
            foreach (var (section, tiles) in groups)
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
            if ((sender as FrameworkElement)?.DataContext is not SetTile tile) return;
            if (tile.IsRelease) ShowRelease(tile.Code);   // a release of several parts: its page
            else OpenPart(tile);                          // one set: its cards
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
