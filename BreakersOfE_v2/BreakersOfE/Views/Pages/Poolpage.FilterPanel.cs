using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using BreakersOfE.Filtering;
using BreakersOfE.Views.Controls;

namespace BreakersOfE.Views.Pages
{
    // The Filters panel and the Sort list — the same filters and order in the
    // grid AND the gallery. Part of PoolPage.
    //
    // Colors, rarity and set ARE the Color / Rarity / Edition column filters
    // (a funnel and the panel show the same thing). Everything else is a
    // panel rule on the same engine (PoolColumnFilters.SetExtra), AND-ed with
    // the column filters. Each table keeps its own panel settings, like its
    // funnels.
    public partial class PoolPage
    {
        private object? _panelRowsFor;          // the rows the panel's lists were built for
        private PoolColumnFilters? _panelFiltersFor;   // the table whose settings the panel shows
        private int _panelVersion = -1;         // that table's filter Version when shown
        private FilterPanelState? _panelLoaded; // what the panel showed / applied last
        private bool _panelOptionsStale = true; // lists need rebuilding before the panel opens
        private bool _syncingSort;

        private void InitFilterPanel()
        {
            FilterPanelView.Changed += ApplyFilterPanel;
            FilterPanelView.ClearRequested += ClearFilterPanel;
        }

        private void BtnFilters_Click(object sender, RoutedEventArgs e)
        {
            FilterPanelView.Visibility = BtnFilters.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (BtnFilters.IsChecked == true) SyncFilterPanel(force: true);
        }

        /// <summary>
        /// Called when a table's rows arrive or change: rebuild the panel's
        /// lists for new data, show this table's settings, sync the sort list.
        /// </summary>
        private void SyncFilterPanel(bool force = false)
        {
            bool panelOpen = BtnFilters.IsChecked == true;
            if (force || !ReferenceEquals(_panelRowsFor, _vm.AllRows))
            {
                _panelRowsFor = _vm.AllRows;
                BuildSortOptions();
                _panelOptionsStale = true;
            }
            // The lists (sets, decks…) take a pass over every row: only while the panel is open.
            if (_panelOptionsStale && (panelOpen || force))
            {
                FilterPanelView.SetOptions(BuildPanelOptions());
                _panelOptionsStale = false;
            }
            // Another table, or its filters were cleared / set elsewhere: show its settings.
            if (force || !ReferenceEquals(_panelFiltersFor, _vm.Filters) || _panelVersion != _vm.Filters.Version)
                LoadPanelFromFilters();
            SyncSortBox();
        }

        /// <summary>Show the current table's panel settings (colors, rarity, set read from the columns).</summary>
        private void LoadPanelFromFilters()
        {
            _panelFiltersFor = _vm.Filters;
            var s = (_vm.Filters.PanelState as FilterPanelState)?.Clone() ?? new FilterPanelState();

            s.Colors = ColumnValues("Color");
            var rar = ColumnValues("Rarity");
            if (rar.Remove("B")) rar.Add("S");                 // one chip for special + bonus
            s.Rarities = rar;
            var sets = ColumnValues("Edition");
            s.Set = sets.Count == 1 ? sets.First() : null;

            FilterPanelView.Load(s);
            _panelVersion = _vm.Filters.Version;
            _panelLoaded = s.Clone();
            ShowPanelSummary(s);
        }

        private HashSet<string> ColumnValues(string column)
        {
            var st = _vm.Filters.Get(column);
            return st is { IsActive: true }
                ? new HashSet<string>(st.SelectedValues, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        private void ClearFilterPanel()
        {
            var f = _vm.Filters;
            foreach (var col in new[] { "Color", "Rarity", "Edition" }) f.Get(col)?.Clear();
            foreach (var key in PanelKeys) f.SetExtra(key, "", null);
            f.PanelState = null;
            _vm.ApplyFilters();
            RefreshFunnelIcons();
            ResetSearch();
            LoadPanelFromFilters();
        }

        private static readonly string[] PanelKeys =
        {
            "panel:type", "panel:format", "panel:finish", "panel:artist", "panel:collection",
            "panel:deck", "panel:list", "panel:text", "panel:price", "panel:mv", "panel:pow", "panel:tou", "panel:own",
        };

        /// <summary>The panel changed: turn its choices into filters and apply them.</summary>
        private void ApplyFilterPanel()
        {
            // The table changed under the panel (rows still loading, or a set
            // opened): show that table's settings instead of applying old ones.
            if (!ReferenceEquals(_panelFiltersFor, _vm.Filters) || !ReferenceEquals(_panelRowsFor, _vm.AllRows) ||
                _panelVersion != _vm.Filters.Version)
            {
                SyncFilterPanel(force: true);
                return;
            }

            var s = FilterPanelView.Read();
            var f = _vm.Filters;
            var sample = _vm.AllRows.FirstOrDefault(r => r != null);
            var was = _panelLoaded ?? new FilterPanelState();

            // Linked to the columns (funnels show them too). Only what changed
            // here, so a funnel's own choice (two sets, "B only"…) isn't undone.
            if (!s.Colors.SetEquals(was.Colors))
                SetColumn("Color", s.Colors);
            if (!s.Rarities.SetEquals(was.Rarities))
            {
                var rar = new HashSet<string>(s.Rarities, StringComparer.Ordinal);
                if (rar.Contains("S")) rar.Add("B");
                SetColumn("Rarity", rar);
            }
            if (s.Set != was.Set)
                SetColumn("Edition", s.Set == null ? new HashSet<string>() : new HashSet<string> { s.Set });

            // Panel rules.
            f.SetExtra("panel:type", "", s.Type is { } type
                ? r => PStr(r, "TypeLine").Contains(type, StringComparison.OrdinalIgnoreCase) : null);

            f.SetExtra("panel:format", "", s.Format is { } fmt ? r => LegalIn(r, fmt) : null);

            f.SetExtra("panel:finish", "", s.Finish is { } fin ? r => HasFinish(r, fin) : null);

            f.SetExtra("panel:artist", "", s.Artist.Length > 0
                ? r => PStr(r, "Artist").Contains(s.Artist, StringComparison.OrdinalIgnoreCase) : null);

            f.SetExtra("panel:collection", "", CollectionTest(s.Collection));
            f.SetExtra("panel:deck", "", DeckTest(s.Deck));
            f.SetExtra("panel:list", "", ListTest(s.List));

            f.SetExtra("panel:text", "", s.Text.Length > 0
                ? r => PStr(r, "Name").Contains(s.Text, StringComparison.OrdinalIgnoreCase) ||
                       PStr(r, "OracleText").Contains(s.Text, StringComparison.OrdinalIgnoreCase)
                : null);

            f.SetExtra("panel:price", "", RangeTest(PriceProp(sample), s.PriceMin, s.PriceMax));
            f.SetExtra("panel:mv", "", RangeTest("ManaValue", s.MvMin, s.MvMax));
            f.SetExtra("panel:pow", "", RangeTest("Power", s.PowMin, s.PowMax));
            f.SetExtra("panel:tou", "", RangeTest("Toughness", s.TouMin, s.TouMax));
            f.SetExtra("panel:own", "", RangeTest(OwnedProp(sample), s.OwnMin, s.OwnMax));

            f.PanelState = s.Clone();
            _panelVersion = f.Version;             // our own change: no re-read needed
            _panelLoaded = s.Clone();

            _vm.ApplyFilters();
            RefreshFunnelIcons();
            ResetSearch();
            ShowPanelSummary(s);
        }

        private void SetColumn(string column, HashSet<string> values)
        {
            if (!ColumnToProperty.TryGetValue(column, out var prop)) return;
            var st = _vm.Filters.GetOrCreate(column, prop);
            if (values.Count == 0) { st.Clear(); return; }
            st.SelectedValues = new HashSet<string>(values, StringComparer.Ordinal);
            st.AllSelected = false;
        }

        /// <summary>"Filters (3)" on the button, and the list of what's on in the panel.</summary>
        private void ShowPanelSummary(FilterPanelState s)
        {
            var labels = new List<string>();
            if (s.Colors.Count > 0) labels.Add("Color " + string.Join("", s.Colors.OrderBy(ColorOrder)));
            if (s.Rarities.Count > 0) labels.Add("Rarity " + string.Join("", s.Rarities.OrderBy(r => "CURMS".IndexOf(r, StringComparison.Ordinal))));
            if (s.Type != null) labels.Add(s.Type);
            if (s.Format != null) labels.Add("Legal in " + s.Format);
            if (s.Finish != null) labels.Add(Models.CardFinish.Display(s.Finish));
            if (s.Artist.Length > 0) labels.Add($"Artist \"{s.Artist}\"");
            if (s.Collection != null) labels.Add(s.Collection);
            if (s.Set != null) labels.Add("Set " + s.Set);
            if (s.Deck != null) labels.Add(s.Deck.StartsWith("path:") ? "In one deck" : s.Deck == "any" ? "In a deck" : "Not in a deck");
            if (s.List != null) labels.Add(s.List == "want" ? "On Want List" : "In Trade Binder");
            if (s.Text.Length > 0) labels.Add($"Text \"{s.Text}\"");
            void Range(string name, string min, string max)
            {
                if (min.Length > 0 || max.Length > 0) labels.Add($"{name} {(min.Length > 0 ? min : "…")}–{(max.Length > 0 ? max : "…")}");
            }
            Range("Price", s.PriceMin, s.PriceMax);
            Range("MV", s.MvMin, s.MvMax);
            Range("Power", s.PowMin, s.PowMax);
            Range("Toughness", s.TouMin, s.TouMax);
            Range("Owned", s.OwnMin, s.OwnMax);

            FilterPanelView.SetActiveText(labels);
            BtnFilters.Content = labels.Count > 0 ? $"Filters ({labels.Count})" : "Filters";
        }

        private static int ColorOrder(string c) => "WUBRGMN".IndexOf(c, StringComparison.Ordinal);

        // ── The lists for this table ───────────────────────────────────
        private FilterPanelOptions BuildPanelOptions()
        {
            var rows = _vm.AllRows;
            var sample = rows.FirstOrDefault(r => r != null);
            var kind = KindOf(_currentTag);

            var collection = new List<FilterChoice> { new("Any", null) };
            if (sample != null)
            {
                if (PHas(sample, "CollectionMissing"))            // a deck
                {
                    collection.Add(new("Missing", "Missing"));
                    collection.Add(new("Owned", "Owned"));
                    collection.Add(new("In other decks", "In other decks"));
                }
                else if (PHas(sample, "OwnedTotal"))              // a pool
                {
                    collection.Add(new("Owned", "Owned"));
                    collection.Add(new("Not owned", "Not owned"));
                }
                else                                             // a collection table
                {
                    if (PHas(sample, "AvailableCount")) collection.Add(new("Free copies", "Free copies"));
                    if (PHas(sample, "UsedCount")) collection.Add(new("In use", "In use"));
                    if (PHas(sample, "IsFavorite")) collection.Add(new("Favorites", "Favorites"));
                }
            }

            var sets = new List<FilterChoice> { new("Any set", null) };
            sets.AddRange(rows.Where(r => r != null)
                .Select(r => (code: PStr(r, "SetCode"), name: PStr(r, "SetName")))
                .Where(x => x.code.Length > 0)
                .GroupBy(x => x.code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new FilterChoice($"{x.name} ({x.code})", x.code)));

            var decks = new List<FilterChoice>
            {
                new("Any", null), new("In any deck", "any"), new("Not in any deck", "none"),
            };
            try
            {
                decks.AddRange(Services.DeckIndexService.AllDecks()
                    .Select(d => (d.Path, Name: string.IsNullOrWhiteSpace(d.Deck.Name)
                        ? System.IO.Path.GetFileNameWithoutExtension(d.Path) : d.Deck.Name))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new FilterChoice("Deck: " + d.Name, "path:" + d.Path)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Filter panel decks: {ex.Message}");
            }

            var formats = new List<FilterChoice> { new("Any", null) };
            formats.AddRange(Models.LegalityInfo.Formats.Select(f => new FilterChoice(f.Header, f.Key)));

            bool tix = kind is TableKind.MtgoPool or TableKind.MtgoCollection;
            return new FilterPanelOptions
            {
                HasColor = sample == null || PHas(sample, "ColorDisplay"),
                HasLegality = sample == null || sample is Models.ILegalityRow || PHas(sample, "LegalitiesJson"),
                Collection = collection,
                Sets = sets,
                Decks = decks,
                Lists = new() { new("Any", null), new("On Want List", "want"), new("In Trade Binder", "binder") },
                Formats = formats,
                PriceLabel = tix ? "Price (tix)" : "Price ($)",
                OwnedLabel = sample != null && (PHas(sample, "TotalQuantity") || PHas(sample, "Quantity")) ? "Qty" : "Owned",
            };
        }

        // ── Tests ──────────────────────────────────────────────────────
        private Func<object, bool>? CollectionTest(string? choice) => choice switch
        {
            "Owned" => r => PHas(r, "CollectionOwned") ? PNum(r, "CollectionOwned") > 0 : PNum(r, "OwnedTotal") > 0,
            "Not owned" => r => PNum(r, "OwnedTotal") is null or <= 0,
            "Missing" => r => PNum(r, "CollectionMissing") > 0,
            "In other decks" => r => PNum(r, "OtherDecksCount") > 0,
            "Free copies" => r => PNum(r, "AvailableCount") > 0,
            "In use" => r => PNum(r, "UsedCount") > 0,
            "Favorites" => r => PVal(r, "IsFavorite") is true,
            _ => null,
        };

        private static Func<object, bool>? DeckTest(string? choice)
        {
            if (choice == null) return null;
            HashSet<string> keys;
            try
            {
                keys = choice.StartsWith("path:")
                    ? Services.DeckIndexService.CardKeys(choice[5..])
                    : Services.DeckIndexService.CardKeys();
            }
            catch { return null; }
            bool inside = choice != "none";
            return r => keys.Contains(Services.DeckIndexService.CardKey(PStr(r, "Name"))) == inside;
        }

        private static Func<object, bool>? ListTest(string? choice)
        {
            if (choice == null) return null;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new Data.CollectionDbContext();
                ids.UnionWith(choice == "want"
                    ? db.WantListEntries.Select(e => e.ScryfallId).ToList()
                    : db.TradeBinderEntries.Select(e => e.ScryfallId).ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Filter panel list: {ex.Message}");
            }
            return r => ids.Contains(PStr(r, "ScryfallId"));
        }

        /// <summary>
        /// Legal (or restricted) in a format. Collection and deck rows answer
        /// themselves; pool cards read their stored legalities as needed (not
        /// kept per card — 100,000 cards would hold a lot of memory).
        /// </summary>
        private static bool LegalIn(object r, string key)
        {
            if (r is Models.ILegalityRow lr) return lr.Legality[key].Status is "legal" or "restricted";
            string json = PStr(r, "LegalitiesJson");
            if (json.Length == 0) return false;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty(key, out var v) &&
                       v.ValueKind == System.Text.Json.JsonValueKind.String &&
                       v.GetString() is "legal" or "restricted";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Paper pool: finishes the printing exists in. Collection: the row's finish. Deck: copies in that finish.</summary>
        private static bool HasFinish(object r, string finish)
        {
            if (PVal(r, "ShownFinish") is string shown && shown.Length > 0)
                return Models.CardFinish.Normalize(shown) == finish;
            if (PVal(r, "Finish") is string stored)
                return Models.CardFinish.Normalize(stored) == finish;
            if (r is Models.DeckCard d)
                return finish switch
                {
                    Models.CardFinish.Foil => d.FoilQuantity > 0 && !d.IsEtchedOnly,
                    Models.CardFinish.Etched => d.EtchedQuantity > 0 || (d.FoilQuantity > 0 && d.IsEtchedOnly),
                    _ => d.Quantity > 0,
                };
            return finish switch
            {
                Models.CardFinish.Foil => PVal(r, "IsFoil") is true,
                Models.CardFinish.Etched => PVal(r, "IsEtched") is true,
                _ => PVal(r, "IsNonFoil") is true,
            };
        }

        private static Func<object, bool>? RangeTest(string? prop, string min, string max)
        {
            if (prop == null) return null;
            double? lo = PParse(min), hi = PParse(max);
            if (lo == null && hi == null) return null;
            return r =>
            {
                var v = PNum(r, prop);
                if (v == null) return false;               // no value (no price, "*" power…)
                return (lo == null || v >= lo) && (hi == null || v <= hi);
            };
        }

        /// <summary>The price the table shows: MTGO tickets, a collection row's price, else USD.</summary>
        private string? PriceProp(object? sample)
        {
            if (KindOf(_currentTag) == TableKind.MtgoPool) return "PriceTix";
            if (sample == null) return "PriceUsd";
            return PHas(sample, "Price") ? "Price" : "PriceUsd";
        }

        /// <summary>Copies: a deck line's total, a collection row's Qty, else how many you own.</summary>
        private static string? OwnedProp(object? sample)
        {
            if (sample == null) return "OwnedTotal";
            if (PHas(sample, "TotalQuantity")) return "TotalQuantity";
            if (PHas(sample, "Quantity")) return "Quantity";
            return "OwnedTotal";
        }

        // ── Reflection helpers (cached per type + property) ─────────────
        private static readonly Dictionary<(Type, string), PropertyInfo?> _panelProps = new();

        private static PropertyInfo? PProp(object r, string name)
        {
            var key = (r.GetType(), name);
            if (!_panelProps.TryGetValue(key, out var pi))
                _panelProps[key] = pi = r.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return pi;
        }

        private static bool PHas(object r, string name) => PProp(r, name) != null;
        private static object? PVal(object r, string name) => PProp(r, name)?.GetValue(r);
        private static string PStr(object r, string name) => PVal(r, name)?.ToString() ?? "";

        private static double? PNum(object r, string name) => PVal(r, name) switch
        {
            null => null,
            int i => i,
            double d => d,
            decimal m => (double)m,
            string s => PParse(s),
            _ => null,
        };

        private static double? PParse(string? s) =>
            double.TryParse((s ?? "").Trim().TrimStart('$'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        // ══════════════════════════════════════════════════════════════════
        // SORT LIST — the gallery has no column headers, so sorting lives here
        // too (same sort as clicking a grid header; the two stay in step).
        // ══════════════════════════════════════════════════════════════════
        private sealed record SortOption(string Label, string Prop)
        {
            public override string ToString() => Label;
        }

        private void BuildSortOptions()
        {
            var sample = _vm.AllRows.FirstOrDefault(r => r != null);
            var list = new List<SortOption> { new("Name", "Name") };
            void Add(string label, string prop)
            {
                if (sample == null || PHas(sample, prop)) list.Add(new SortOption(label, prop));
            }
            Add("Release date", "ReleasedAt");
            Add("Set (A→Z)", "SetCode");
            Add("Collector number", "CollectorNumber");
            Add("Mana value", "ManaValue");
            Add("Color", "ColorDisplay");
            Add("Type", "TypeLine");
            if (PriceProp(sample) is { } price) Add(KindOf(_currentTag) == TableKind.MtgoPool ? "Price (tix)" : "Price", price);
            if (OwnedProp(sample) is { } owned) Add(owned == "OwnedTotal" ? "Owned" : "Qty", owned);
            Add("Date added", "DateAddedDisplay");
            Add("Artist", "Artist");

            _syncingSort = true;
            try
            {
                SortBox.ItemsSource = list;
                SortBox.DisplayMemberPath = nameof(SortOption.Label);
            }
            finally
            {
                _syncingSort = false;
            }
        }

        /// <summary>Show the current sort (header clicks included) in the list and on the arrow.</summary>
        private void SyncSortBox()
        {
            _syncingSort = true;
            try
            {
                SortBox.SelectedItem = (SortBox.ItemsSource as IEnumerable<SortOption>)?
                    .FirstOrDefault(o => o.Prop == (_lastSortProp ?? "Name"));
                BtnSortDir.Content = _lastSortAsc ? "↑" : "↓";
                BtnSortDir.ToolTip = _lastSortAsc ? "Ascending (click for descending)" : "Descending (click for ascending)";
            }
            finally
            {
                _syncingSort = false;
            }
        }

        private void SortBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingSort || SortBox.SelectedItem is not SortOption o) return;
            SortColumn(o.Prop, _lastSortAsc);
        }

        private void BtnSortDir_Click(object sender, RoutedEventArgs e) =>
            SortColumn(_lastSortProp ?? "Name", !_lastSortAsc);
    }
}
