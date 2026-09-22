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

        /// <summary>The per-column filter engine for this grid.</summary>
        public PoolColumnFilters Filters { get; } = new();

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

        public void LoadPool(string tag)
        {
            IsLoading = true;
            IsEmpty = false;
            EmptyMessage = "";
            Title = TitleFor(tag);
            StatusText = "Loading...";
            Filters.ClearAll();  // fresh table → drop any prior filters

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

                AssignRowIndices(rows);

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    _allRows = rows;
                    _label = label;
                    Items = rows;
                    IsLoading = false;

                    if (rows.Count == 0)
                    {
                        IsEmpty = true;
                        EmptyMessage =
                            "No cards in this pool yet.\n\n" +
                            "Use \"Update Database\" (bottom-left) to download " +
                            "the card data from Scryfall.";
                        StatusText = "0 cards";
                    }
                    else
                    {
                        IsEmpty = false;
                        StatusText = $"{rows.Count:N0} {label}";
                    }
                });
            });
        }

        /// <summary>Distinct values for a column, cascaded by other active filters.</summary>
        public List<string> DistinctValuesFor(string columnName, string propertyName) =>
            Filters.DistinctValuesFor(columnName, propertyName, _allRows);

        /// <summary>Recompute Items from the full table using active filters.</summary>
        public void ApplyFilters()
        {
            var filtered = Filters.Apply(_allRows);
            AssignRowIndices(filtered);
            Items = filtered;

            if (Filters.HasActiveFilters)
                StatusText = $"{filtered.Count:N0} of {_allRows.Count:N0} {_label}  (filtered)";
            else
                StatusText = $"{_allRows.Count:N0} {_label}";
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
            "Tokens" => "Card Pool — Tokens",
            "Planes" => "Card Pool — Planes",
            "Schemes" => "Card Pool — Schemes",
            "Vanguards" => "Card Pool — Vanguards",
            "ArtSeries" => "Card Pool — Art Series",
            "Conspiracies" => "Card Pool — Conspiracies",
            _ => "Card Pool — Cards"
        };
    }
}