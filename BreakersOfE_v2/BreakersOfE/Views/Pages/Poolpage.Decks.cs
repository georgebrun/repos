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
    // Deck browser and opening a deck.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // DECK BROWSER — every .deck file under Documents\BoE_V2\Decks
        // (subfolders too), grouped by deck type (DeckFormats order), A→Z. Click a tile →
        // the deck opens read-only in the grid or gallery (per the switch).
        // ══════════════════════════════════════════════════════════════════
        private List<DeckTile> _deckTiles = new();
        private List<(string section, List<DeckTile> tiles)> _deckGroups = new();
        private List<DeckBrowserRow> _deckRows = new();
        private int _decksPerRow = 1;
        private DeckTile? _deckHighlighted;
        /// <summary>Constructed decks: the format they're checked against on screen (not saved).</summary>
        private string? _checkAsFormat;
        private bool _fillingCheckAs;
        private Models.Deck? _openDeck;
        private string? _openDeckPath;             // its file (the pop-up leaves it out of IN YOUR OTHER DECKS)            // the deck on screen (for Statistics)

        /// <summary>"Decks" nav item: the deck browser (re-reads the folder each time).</summary>
        public void ShowDecks()
        {
            _inSetsContext = false;
            _inDecksContext = true;
            _vm.IsEmpty = false;             // the grid's empty message doesn't apply here
            BuildDeckTiles();
            ResetSearch();
            SetViewMode(PoolViewMode.Decks);
        }

        private void BuildDeckTiles()
        {
            var tiles = new List<DeckTile>();
            string root = Services.AppFolderService.DecksFolder;

            // Same cached read as Other Decks: files are parsed once and again
            // only when they change (no pool lookups, so the browser is fast).
            foreach (var (path, deck) in Services.DeckIndexService.AllDecks())
            {
                try
                {

                    string folder = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root);

                    // Color identity: the commander's (command-zone formats), else the main deck's.
                    var rule = Models.DeckFormats.For(deck.DeckType);
                    var main = deck.PlayCards.Where(c => c.Category != Models.DeckCardCategory.Sideboard).ToList();
                    var commanders = main.Where(c => c.IsCommander || c.Category == Models.DeckCardCategory.Commander).ToList();
                    var identitySource = rule.HasLeader && commanders.Count > 0
                        ? commanders : main;
                    var identity = new HashSet<char>(identitySource.SelectMany(c => c.ColorIdentity));
                    string symbols = string.Concat("WUBRG".Where(identity.Contains).Select(c => $"{{{c}}}"));
                    if (symbols.Length == 0) symbols = "{C}";
                    tiles.Add(new DeckTile
                    {
                        Name = string.IsNullOrWhiteSpace(deck.Name)
                            ? Path.GetFileNameWithoutExtension(path) : deck.Name,
                        FilePath = path,
                        IsCommander = rule.HasLeader,
                        Section = rule.Name,
                        SectionOrder = Models.DeckFormats.All.ToList().IndexOf(rule),
                        FormatName = rule.Type == Models.DeckType.Standard
                            ? Models.DeckFormats.Constructed(deck.ConstructedFormat).Name : "",
                        CommanderName = string.Join(" & ", commanders.Select(c => c.Name)),
                        Folder = folder == "." ? "" : folder,
                        IdentitySymbols = symbols,
                        CardCount = deck.PlayCards.Sum(c => c.TotalQuantity),
                        TotalValue = deck.Cards.Sum(c => c.RowValue),
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Deck browser skipped {path}: {ex.Message}");
                }
            }

            _deckTiles = tiles;
            _deckGroups = tiles
                .GroupBy(t => (t.Section, t.SectionOrder))
                .OrderBy(g => g.Key.SectionOrder)
                .Select(g => (section: g.Key.Section,
                              tiles: g.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()))
                .ToList();
        }

        /// <summary>Flatten sections into header rows + rows of N tiles.</summary>
        private void RebuildDeckRows()
        {
            double avail = DeckList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            _decksPerRow = perRow;

            var rows = new List<DeckBrowserRow>();
            if (_deckTiles.Count == 0)
            {
                rows.Add(new DeckBrowserRow
                {
                    IsHeader = true,
                    HeaderText = $"No decks yet — copy .deck files into {Services.AppFolderService.DecksFolder}",
                    HeaderCount = "0",
                });
            }
            foreach (var (section, tiles) in _deckGroups)
            {
                rows.Add(new DeckBrowserRow
                {
                    IsHeader = true,
                    HeaderText = section,
                    HeaderCount = tiles.Count.ToString("N0"),
                });
                for (int i = 0; i < tiles.Count; i += perRow)
                    rows.Add(new DeckBrowserRow
                    {
                        Tiles = tiles.GetRange(i, Math.Min(perRow, tiles.Count - i))
                    });
            }
            _deckRows = rows;
            DeckList.ItemsSource = rows;
        }

        private void DeckList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode != PoolViewMode.Decks || !e.WidthChanged) return;
            double avail = DeckList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            int perRow = Math.Max(1, (int)(avail / (SetTileWidth + SetTileMargin)));
            if (perRow != _decksPerRow) RebuildDeckRows();
        }

        private void DeckTile_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DeckTile tile)
                OpenDeck(tile);
        }

        /// <summary>Open a deck read-only in the shared grid (Deck columns + layout).</summary>
        private void OpenDeck(DeckTile tile)
        {
            Models.Deck? deck;
            try
            {
                deck = Services.DeckService.Load(tile.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this),
                    $"Could not open this deck.\n\n{ex.Message}", "Open Deck",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (deck == null) return;

            _openDeck = deck;
            _openDeckPath = tile.FilePath;
            _checkAsFormat = null;               // a Constructed deck is checked as its own format
            ApplyDeckFormat();

            // Deck vs. collection: Owned / Free / Missing / Wanted per card (read-only).
            Services.DeckCollectionService.Annotate(deck);

            // Cards shared between decks: Other Decks count + tooltip per card.
            Services.DeckIndexService.Annotate(deck, tile.FilePath);

            _vm.UseFilters("Deck:" + tile.FilePath);     // each deck keeps its own filters
            PrepareTable(DeckTableTag);
            PoolGrid.SelectedItem = null;
            _vm.LoadRows($"Decks — {deck.Name}",
                         deck.Cards.Cast<object>().ToList(),
                         deck.Cards.Count == 1 ? "line" : "lines");
            SetViewMode(SwitchMode);          // grid or gallery, per the switch
        }

        // ══════════════════════════════════════════════════════════════════
        // DECK RULES — the open deck against its format's rules (DeckFormats
        // → DeckRulesService): a one-line summary under the grid, the full list
        // in Statistics → Format Check. Constructed decks can be checked as
        // another format (Standard, Modern, …) without changing the deck.
        // ══════════════════════════════════════════════════════════════════
        private void ApplyDeckFormat()
        {
            if (_openDeck == null) return;
            var rule = Models.DeckFormats.For(_openDeck, _checkAsFormat);

            // Legal column + totals: each card against this format's card list.
            string key = rule.LegalityKey ?? "";
            foreach (var card in _openDeck.Cards) card.DeckFormat = key;

            // "Check as" list: Constructed decks only.
            bool constructed = rule.Type == Models.DeckType.Standard;
            _fillingCheckAs = true;
            try
            {
                CheckAsLabel.Visibility = CheckAsBox.Visibility =
                    constructed ? Visibility.Visible : Visibility.Collapsed;
                if (constructed)
                {
                    CheckAsBox.ItemsSource = Models.DeckFormats.ConstructedFormats;
                    CheckAsBox.DisplayMemberPath = nameof(Models.ConstructedFormat.Name);
                    CheckAsBox.SelectedItem = Models.DeckFormats.Constructed(_checkAsFormat ?? _openDeck.ConstructedFormat);
                }
            }
            finally
            {
                _fillingCheckAs = false;
            }

            ShowDeckRules(rule);
        }

        private void ShowDeckRules(Models.DeckFormatRule rule)
        {
            if (_openDeck == null) return;
            var checks = Services.DeckRulesService.Check(_openDeck, rule);
            var problems = checks.Where(c => !c.IsOk).ToList();
            DeckRulesText.Text = Services.DeckRulesService.SummaryLine(rule, checks);
            // ISA-101: plain when all is well, amber only when something needs attention.
            if (problems.Count > 0)
                DeckRulesText.SetResourceReference(TextBlock.ForegroundProperty, "BoeWarningBrush");
            else
                DeckRulesText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            DeckRulesText.ToolTip = string.Join("\n", checks.Select(c =>
                $"{(c.IsOk ? "✓" : "⚠")} {c.Title}: {c.Summary}" +
                (c.Details.Count > 0 ? "\n      " + string.Join("\n      ", c.Details.Take(8)) +
                    (c.Details.Count > 8 ? $"\n      … and {c.Details.Count - 8} more" : "") : ""))) +
                "\n\nStatistics → Format Check shows the full list.";
        }

        /// <summary>Constructed: check the open deck as another format (not saved).</summary>
        private void CheckAsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingCheckAs || _openDeck == null) return;
            if (CheckAsBox.SelectedItem is not Models.ConstructedFormat cf) return;
            _checkAsFormat = cf.Key;
            ApplyDeckFormat();
            var keep = PoolGrid.SelectedItem;
            PoolGrid.Items.Refresh();            // Legal column re-reads the format
            if (keep != null) { PoolGrid.SelectedItem = keep; PoolGrid.ScrollIntoView(keep); }
            UpdateTotals();
        }

        /// <summary>Search in the deck browser: jump to the first deck name that begins with the text.</summary>
        private void SearchDecks(string text)
        {
            for (int r = 0; r < _deckRows.Count; r++)
            {
                var row = _deckRows[r];
                if (row.IsHeader) continue;
                var match = row.Tiles.FirstOrDefault(t =>
                    t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;

                if (_deckHighlighted != null) _deckHighlighted.IsHighlighted = false;
                match.IsHighlighted = true;
                _deckHighlighted = match;

                DeckList.UpdateLayout();
                int jump = Math.Min(r + 10, _deckRows.Count - 1);
                DeckList.ScrollIntoView(_deckRows[jump]);
                DeckList.UpdateLayout();
                DeckList.ScrollIntoView(row);
                return;
            }
        }
    }
}
