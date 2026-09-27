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
        // (subfolders too), grouped Commander / Standard, A→Z. Click a tile →
        // the deck opens read-only in the grid or gallery (per the switch).
        // ══════════════════════════════════════════════════════════════════
        private List<DeckTile> _deckTiles = new();
        private List<(string section, List<DeckTile> tiles)> _deckGroups = new();
        private List<DeckBrowserRow> _deckRows = new();
        private int _decksPerRow = 1;
        private DeckTile? _deckHighlighted;
        private string _openDeckFormat = "standard";
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

                    // Color identity: the commander's (Commander decks), else the main deck's.
                    var main = deck.Cards.Where(c => c.Category != Models.DeckCardCategory.Sideboard).ToList();
                    var commanders = main.Where(c => c.IsCommander || c.Category == Models.DeckCardCategory.Commander).ToList();
                    var identitySource = deck.DeckType == Models.DeckType.Commander && commanders.Count > 0
                        ? commanders : main;
                    var identity = new HashSet<char>(identitySource.SelectMany(c => c.ColorIdentity));
                    string symbols = string.Concat("WUBRG".Where(identity.Contains).Select(c => $"{{{c}}}"));
                    if (symbols.Length == 0) symbols = "{C}";
                    tiles.Add(new DeckTile
                    {
                        Name = string.IsNullOrWhiteSpace(deck.Name)
                            ? Path.GetFileNameWithoutExtension(path) : deck.Name,
                        FilePath = path,
                        IsCommander = deck.DeckType == Models.DeckType.Commander,
                        CommanderName = string.Join(" & ",
                            deck.Cards.Where(c => c.IsCommander).Select(c => c.Name)),
                        Folder = folder == "." ? "" : folder,
                        IdentitySymbols = symbols,
                        CardCount = deck.Cards.Sum(c => c.TotalQuantity),
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
                .GroupBy(t => t.IsCommander ? "Commander" : "Standard")
                .OrderBy(g => g.Key == "Commander" ? 0 : 1)
                .Select(g => (section: g.Key,
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

            // Legal column checks each card against this deck's format.
            string format = deck.DeckType == Models.DeckType.Commander ? "commander" : "standard";
            foreach (var card in deck.Cards) card.DeckFormat = format;
            _openDeckFormat = format;
            _openDeck = deck;
            _openDeckPath = tile.FilePath;

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
