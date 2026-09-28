using CommunityToolkit.Mvvm.ComponentModel;
using BreakersOfE.Data;
using BreakersOfE.Filtering;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BreakersOfE.ViewModels
{
    /// <summary>
    /// PoolViewModel — v1 pattern (load once into memory, bind directly) with
    /// the unified column-filter engine attached.
    ///
    /// _allRows holds the full table. Items is what the grid shows. Applying
    /// column filters recomputes Items from _allRows via PoolColumnFilters.
    /// </summary>
    public partial class PoolViewModel : ObservableObject
    {
        // Full unfiltered table (the single source the filters cascade over)
        private List<object> _allRows = new();

        /// <summary>
        /// The column filters of the table on screen. Each table (pool page,
        /// Collection, each deck, the set view) has its own, kept while the app
        /// is open — switch away and back and they're still there.
        /// </summary>
        public PoolColumnFilters Filters { get; private set; } = new();

        private readonly Dictionary<string, PoolColumnFilters> _filtersByTable = new();

        /// <summary>Switch to this table's filters (created empty the first time).</summary>
        public void UseFilters(string tableKey)
        {
            if (!_filtersByTable.TryGetValue(tableKey, out var f))
                _filtersByTable[tableKey] = f = new PoolColumnFilters();
            Filters = f;
        }

        /// <summary>
        /// The full unfiltered table. The set browser builds its tiles from
        /// this so set counts don't shrink when filters are active.
        /// </summary>
        public IReadOnlyList<object> AllRows => _allRows;

        [ObservableProperty]
        private IEnumerable items = System.Array.Empty<object>();

        [ObservableProperty]
        private string title = "Card Pool";

        [ObservableProperty]
        private string statusText = "Loading...";

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string emptyMessage = "";

        [ObservableProperty]
        private bool isEmpty;

        private string _label = "cards";

        // Newest load wins: loads run in the background and can finish out of
        // order (two quick edits → two reloads); an older result is dropped.
        private int _loadGeneration;

        public void LoadPool(string tag)
        {
            int generation = ++_loadGeneration;
            IsLoading = true;
            IsEmpty = false;
            EmptyMessage = "";
            Title = TitleFor(tag);
            StatusText = "Loading...";
            // Filters are NOT cleared: this table's remembered filters re-apply.

            Task.Run(() =>
            {
                List<object> rows;
                string label;

                try
                {
                    using var db = new AppDbContext();
                    switch (tag)
                    {
                        case "Tokens":
                            rows = db.TokenCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "tokens"; break;
                        case "Planes":
                            rows = db.PlanarCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "planes"; break;
                        case "Schemes":
                            rows = db.SchemeCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "schemes"; break;
                        case "Vanguards":
                            rows = db.VanguardCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "vanguards"; break;
                        case "ArtSeries":
                            rows = db.ArtSeriesCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "art series cards"; break;
                        case "Conspiracies":
                            rows = db.ConspiracyCards.AsNoTracking().OrderBy(c => c.Name)
                                     .ToList().Cast<object>().ToList();
                            label = "conspiracies"; break;
                        case "Collection":
                            // View-only: read the collection, never write.
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.CollectionEntries.AsNoTracking()
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "collection rows"; break;
                        // ── Other collection tables (view-only) ──
                        case "CollTokens":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.TokenCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "token rows"; break;
                        case "CollPlanes":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.PlanarCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "plane rows"; break;
                        case "CollSchemes":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.SchemeCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "scheme rows"; break;
                        case "CollVanguards":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.VanguardCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "vanguard rows"; break;
                        case "CollArtSeries":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.ArtSeriesCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "art series rows"; break;
                        case "CollConspiracies":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.ConspiracyCollectionEntries.AsNoTracking().OrderBy(c => c.Name)
                                          .ToList().Cast<object>().ToList();
                            label = "conspiracy rows"; break;
                        case "TradeBinder":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.TradeBinderEntries.AsNoTracking()
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "trade binder rows"; break;
                        case "WantList":
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.WantListEntries.AsNoTracking()
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "want list rows"; break;
                        // ── Online (MTGO / Arena): own pool table and own collection ──
                        case "MtgoCards":
                            using (var odb = new OnlineDbContext())
                                rows = odb.OnlineCards.AsNoTracking().Where(c => c.IsOnMtgo)
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "MTGO cards"; break;
                        case "ArenaCards":
                            using (var odb = new OnlineDbContext())
                                rows = odb.OnlineCards.AsNoTracking().Where(c => c.IsOnArena)
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "Arena cards"; break;
                        case "MtgoCollection":
                        case "ArenaCollection":
                        {
                            string game = tag == "MtgoCollection" ? Models.OnlineGame.Mtgo : Models.OnlineGame.Arena;
                            using (var cdb = new CollectionDbContext())
                                rows = cdb.OnlineCollectionEntries.AsNoTracking().Where(c => c.Game == game)
                                          .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                          .ToList().Cast<object>().ToList();
                            label = "collection rows"; break;
                        }
                        case "Cards":
                        default:
                            rows = db.PoolCards.AsNoTracking()
                                     .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                                     .ToList().Cast<object>().ToList();
                            label = "cards"; break;
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadPool({tag}) failed: {ex.Message}");
                    rows = new List<object>();
                    label = "cards";
                }

                // Pool pages: how many of each printing you own (read-only).
                if (IsPoolTag(tag))
                    Services.OwnedCountService.Fill(tag, rows);
                else if (!IsOnlineCollectionTag(tag))
                    Services.FinishInfoService.Fill(tag, rows);   // etched facts from the pool

                AssignRowIndices(rows);

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (generation != _loadGeneration) return;   // a newer load is on its way
                    _allRows = rows;
                    _label = label;
                    IsLoading = false;

                    if (rows.Count == 0)
                    {
                        Items = rows;
                        IsEmpty = true;
                        EmptyMessage = IsOnlineCollectionTag(tag)
                            ? "Nothing here yet.\n\nAdd cards with Edit → Online → Collection." :
                            tag.StartsWith("Coll") || tag is "TradeBinder" or "WantList"
                            ? "Nothing here yet." :
                            "No cards in this pool yet.\n\n" +
                            "Use \"Update Database\" (bottom-left) to download " +
                            "the card data from Scryfall.";
                        StatusText = "0 cards";
                    }
                    else
                    {
                        IsEmpty = false;
                        ApplyFilters();   // this table's remembered filters (or none)
                    }
                });
            });
        }

        /// <summary>
        /// Show rows that are already in memory (an opened deck). Same result
        /// as LoadPool: fresh filters, row indices, status text.
        /// </summary>
        public void LoadRows(string title, List<object> rows, string label)
        {
            Title = title;
            _allRows = rows;
            _label = label;
            IsLoading = false;
            IsEmpty = rows.Count == 0;
            EmptyMessage = rows.Count == 0 ? "This deck has no cards." : "";
            ApplyFilters();   // this table's remembered filters (or none)
        }

        /// <summary>Card Pool pages (not the collection tables).</summary>
        private static bool IsPoolTag(string tag) =>
            !(tag == "Collection" || tag.StartsWith("Coll") || tag is "TradeBinder" or "WantList"
              || IsOnlineCollectionTag(tag));

        /// <summary>The MTGO or Arena collection table.</summary>
        private static bool IsOnlineCollectionTag(string tag) => tag is "MtgoCollection" or "ArenaCollection";

        /// <summary>Distinct values for a column, cascaded by other active filters.</summary>
        public List<string> DistinctValuesFor(string columnName, string propertyName) =>
            Filters.DistinctValuesFor(columnName, propertyName, _allRows);

        /// <summary>Recompute Items from the full table using active filters.</summary>
        /// <summary>
        /// Page-level filter on top of the column filters (the set checklist's
        /// "Missing Only"). Null = off. The page sets it; not remembered per table.
        /// </summary>
        public System.Func<object, bool>? ExtraFilter { get; set; }

        /// <summary>
        /// Extra status text computed from the column-filtered rows, before
        /// ExtraFilter (the set checklist's "Owned X of Y"). Null = none.
        /// </summary>
        public System.Func<IReadOnlyList<object>, string>? StatusNote { get; set; }

        public void ApplyFilters()
        {
            var filtered = Filters.Apply(_allRows);
            string note = StatusNote?.Invoke(filtered) ?? "";
            if (ExtraFilter != null)
                filtered = filtered.Where(ExtraFilter).ToList();
            AssignRowIndices(filtered);
            Items = filtered;

            if (Filters.HasActiveFilters || ExtraFilter != null)
                StatusText = $"{filtered.Count:N0} of {_allRows.Count:N0} {_label}  (filtered)" + note;
            else
                StatusText = $"{_allRows.Count:N0} {_label}" + note;
        }

        private static void AssignRowIndices(List<object> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var prop = rows[i]?.GetType().GetProperty("RowIndex");
                prop?.SetValue(rows[i], i);
            }
        }

        private static string TitleFor(string tag) => tag switch
        {
            "Tokens"       => "Card Pool — Tokens",
            "Planes"       => "Card Pool — Planes",
            "Schemes"      => "Card Pool — Schemes",
            "Vanguards"    => "Card Pool — Vanguards",
            "ArtSeries"    => "Card Pool — Art Series",
            "Conspiracies" => "Card Pool — Conspiracies",
            "Collection"   => "Collection — Cards",
            "CollTokens"       => "Collection — Tokens",
            "CollPlanes"       => "Collection — Planes",
            "CollSchemes"      => "Collection — Schemes",
            "CollVanguards"    => "Collection — Vanguards",
            "CollArtSeries"    => "Collection — Art Series",
            "CollConspiracies" => "Collection — Conspiracies",
            "TradeBinder"      => "Trade Binder",
            "WantList"         => "Want List",
            "MtgoCards"        => "Online — MTGO Cards",
            "ArenaCards"       => "Online — Arena Cards",
            "MtgoCollection"   => "Online — MTGO Collection",
            "ArenaCollection"  => "Online — Arena Collection",
            _              => "Card Pool — Cards"
        };
    }
}
