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
    // Embedded mode: this page as one of the two tables on an Edit page.
    // Part of PoolPage (same class).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // EMBEDDED — the Edit page shows the detail panel itself (this page
        // hides its own), keeps its own filters (its own view model) and its
        // own saved layouts and zoom ("Edit:" prefix on the layout key).
        // ══════════════════════════════════════════════════════════════════

        /// <summary>"" in View; "Edit:" when embedded, so Edit layouts are saved separately.</summary>
        private string _layoutPrefix = "";

        /// <summary>The key a table's layout and zoom are saved under.</summary>
        /// <summary>
        /// Column layout and zoom: one per table, shared by View and Edit
        /// (the Collection on View and on Edit look the same).
        /// </summary>
        private static string LayoutKey(string table) => table;

        /// <summary>Edit pages' own Grid / Gallery choice per table.</summary>
        private string EditGalleryKey(string table) => _layoutPrefix + table;

        /// <summary>Selection changed (grid or gallery): the card, or null.</summary>
        public event Action<object?>? SelectedCardChanged;

        /// <summary>Keys pressed in the grid (before the grid handles them).</summary>
        public event System.Windows.Input.KeyEventHandler? GridPreviewKeyDown;

        /// <summary>The selected card (grid and gallery share one selection).</summary>
        public object? SelectedCard => PoolGrid.SelectedItem;

        /// <summary>How many rows are selected.</summary>
        public int SelectedCount => PoolGrid.SelectedItems.Count;

        /// <summary>
        /// The cell of <paramref name="row"/> under the column <paramref name="header"/>
        /// (scrolled into view), or null when that column is hidden.
        /// </summary>
        public DataGridCell? CellFor(object row, string header)
        {
            if (!PoolGrid.IsVisible) return null;      // gallery: the editor opens at the mouse
            var col = PoolGrid.Columns.FirstOrDefault(c =>
                ColumnHeader(c) == header && c.Visibility == Visibility.Visible);
            if (col == null) return null;
            PoolGrid.ScrollIntoView(row, col);
            PoolGrid.UpdateLayout();
            return col.GetCellContent(row)?.Parent as DataGridCell;
        }

        /// <summary>Every selected row, in table order (Edit: several rows at once).</summary>
        public List<object> SelectedCards
        {
            get
            {
                if (PoolGrid.SelectedItems.Count == 0) return new List<object>();
                if (PoolGrid.SelectedItems.Count == 1) return new List<object> { PoolGrid.SelectedItems[0]! };
                var picked = new HashSet<object>(PoolGrid.SelectedItems.Cast<object>());
                var list = new List<object>(picked.Count);
                foreach (var row in PoolGrid.Items)
                    if (row != null && picked.Contains(row)) list.Add(row);
                return list;
            }
        }

        /// <summary>The table shown ("Cards", "Collection", "CollTokens", …).</summary>
        public string CurrentTag => _currentTag;

        /// <summary>Edit pages: a tile's + / − (the card is selected first).</summary>
        public event Action<object, System.Windows.Input.ModifierKeys>? GalleryAdd;
        public event Action<object, System.Windows.Input.ModifierKeys>? GalleryRemove;

        /// <summary>
        /// Hosted inside an Edit page: no detail panel, tighter margins, own
        /// layouts, several rows at once, + / − on gallery tiles, and its own
        /// Grid / Gallery switch (<paramref name="galleryByDefault"/> until changed).
        /// </summary>
        public void SetEmbedded(bool galleryByDefault = false)
        {
            _embeddedGalleryDefault = galleryByDefault;
            _embeddedGallery = galleryByDefault;
            Gallery.ShowEditButtons = true;
            Gallery.PreviewKeyDown += (s, e) =>
            {
                // Keys on the tiles only — Delete on the size slider must not remove cards.
                if (Gallery.TilesHaveFocus) GridPreviewKeyDown?.Invoke(s, e);
            };
            Detail.Visibility = Visibility.Collapsed;
            DetailSplitter.Visibility = Visibility.Collapsed;
            DetailColumn.MinWidth = 0;
            DetailColumn.Width = new GridLength(0);
            RootGrid.Margin = new Thickness(0);
            _layoutPrefix = "Edit:";
            // Edit acts on several rows at once (Ctrl+click, Shift+click).
            PoolGrid.SelectionMode = DataGridSelectionMode.Extended;
            PoolGrid.PreviewKeyDown += (s, e) => GridPreviewKeyDown?.Invoke(s, e);
        }

        /// <summary>
        /// Edit page: called on a cell double-click with (row, column header,
        /// cell). Return true when handled (the card pop-up then doesn't open).
        /// </summary>
        public Func<object, string, DataGridCell, bool>? CellDoubleClickHandler { get; set; }

        /// <summary>Right-click menu for the grid rows and the gallery tiles (Edit actions).</summary>
        public void SetRowContextMenu(ContextMenu menu)
        {
            PoolGrid.ContextMenu = menu;
            Gallery.TileContextMenu = menu;
        }

        // ── Gallery selection (one selection, shared with the grid) ──────
        private object? _galleryAnchor;

        /// <summary>
        /// Tile click: plain = that card; Edit pages also Ctrl = add/remove it,
        /// Shift = everything from the last clicked card to this one.
        /// </summary>
        private void GalleryCardClicked(object card, System.Windows.Input.ModifierKeys keys)
        {
            bool several = PoolGrid.SelectionMode == DataGridSelectionMode.Extended;
            if (several && keys.HasFlag(System.Windows.Input.ModifierKeys.Control))
            {
                if (PoolGrid.SelectedItems.Contains(card)) PoolGrid.SelectedItems.Remove(card);
                else PoolGrid.SelectedItems.Add(card);
                _galleryAnchor = card;
                return;
            }
            if (several && keys.HasFlag(System.Windows.Input.ModifierKeys.Shift) && _galleryAnchor != null)
            {
                var order = GridOrder().Cast<object>().ToList();
                int a = order.IndexOf(_galleryAnchor), b = order.IndexOf(card);
                if (a >= 0 && b >= 0)
                {
                    _applyingSelection = true;         // one "selected" notice, not one per card
                    try
                    {
                        PoolGrid.SelectedItems.Clear();
                        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) PoolGrid.SelectedItems.Add(order[i]);
                    }
                    finally
                    {
                        _applyingSelection = false;
                    }
                    SelectedCardChanged?.Invoke(card);
                    return;
                }
            }
            PoolGrid.SelectedItem = card;
            _galleryAnchor = card;
        }

        /// <summary>Right-click a tile: it joins the selection unless it's already in it (like grid rows).</summary>
        private void GalleryCardRightClicked(object card)
        {
            if (!PoolGrid.SelectedItems.Contains(card)) { PoolGrid.SelectedItem = card; _galleryAnchor = card; }
        }

        /// <summary>A tile's + / −: act on that card (or on the selection it belongs to).</summary>
        private void SelectForTileButton(object card)
        {
            if (!PoolGrid.SelectedItems.Contains(card)) { PoolGrid.SelectedItem = card; _galleryAnchor = card; }
        }

        // ── Card panel width (View pages), remembered ─────────────────
        private void DetailSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
            Services.GridLayoutService.SetNumber("View:DetailWidth", DetailColumn.ActualWidth);

        private void RestoreDetailWidth()
        {
            if (_layoutPrefix.Length > 0) return;                 // embedded: no card panel
            if (Services.GridLayoutService.GetNumber("View:DetailWidth") is double w && w >= DetailColumn.MinWidth)
                DetailColumn.Width = new GridLength(Math.Min(w, DetailColumn.MaxWidth));
        }

        /// <summary>Gallery tiles re-read their counts (after an edit changed Owned).</summary>
        public void RefreshGalleryCards(IEnumerable<object> cards)
        {
            foreach (var c in cards) Gallery.RefreshCard(c);
        }

        /// <summary>First loaded row (all rows, not just the filtered ones) that matches.</summary>
        public object? FindLoaded(Func<object, bool> match)
        {
            foreach (var row in _vm.AllRows)
                if (row != null && match(row)) return row;
            return null;
        }

        /// <summary>Every loaded row (not just the filtered ones) that matches.</summary>
        public List<object> FindAllLoaded(Func<object, bool> match)
        {
            var list = new List<object>();
            foreach (var row in _vm.AllRows)
                if (row != null && match(row)) list.Add(row);
            return list;
        }

        // ── Edit → Decks: the deck being edited ────────────────────────
        /// <summary>
        /// Show a deck (Edit → Decks), like opening it from the deck browser:
        /// Deck columns and layout, the live rules line, Owned / Free /
        /// Missing and Other Decks. Showing the same deck again after an edit
        /// keeps filters, sort and view, and selects the rows
        /// <paramref name="select"/> matches (else the first that
        /// <paramref name="fallback"/> matches).
        /// </summary>
        public void ShowDeck(Models.Deck deck, string path,
                             Func<object, bool>? select = null, Func<object, bool>? fallback = null)
        {
            bool same = _currentTag == DeckTableTag && _openDeck != null &&
                        string.Equals(_openDeckPath, path, StringComparison.OrdinalIgnoreCase);
            // Deck Settings changed the type or format: "Check as" starts from the new one.
            bool sameFormat = same && _openDeck!.DeckType == deck.DeckType &&
                              string.Equals(_openDeck.ConstructedFormat, deck.ConstructedFormat, StringComparison.OrdinalIgnoreCase);
            _inSetsContext = false;
            _inDecksContext = false;
            _openDeck = deck;
            _openDeckPath = path;
            if (!sameFormat) _checkAsFormat = null;
            ApplyDeckFormat();
            Services.DeckCollectionService.Annotate(deck);
            Services.DeckIndexService.Annotate(deck, path);

            _vm.UseFilters("Deck:" + path);
            if (!same)
            {
                PrepareTable(DeckTableTag);
                PoolGrid.SelectedItem = null;
            }
            _pendingSelect = select;
            _pendingFallback = fallback;
            _vm.LoadRows($"Deck — {deck.Name}", deck.Cards.Cast<object>().ToList(),
                         deck.Cards.Count == 1 ? "line" : "lines");
            if (!same)
            {
                if (_embeddedGalleryDefault is bool d)
                    _embeddedGallery = Services.GridLayoutService.GetEditGallery(EditGalleryKey(DeckTableTag)) ?? d;
                SetViewMode(SwitchMode);
            }
            // Same deck: the view stays; the rules line was re-read above.
        }

        /// <summary>No deck picked yet (Edit → Decks): an empty deck table with a hint.</summary>
        public void ShowNoDeck(string message)
        {
            _openDeck = null;
            _openDeckPath = null;
            _pendingSelect = null;
            _pendingFallback = null;
            _vm.UseFilters("Deck:");
            PrepareTable(DeckTableTag);
            _vm.LoadRows("Deck", new List<object>(), "lines");
            _vm.EmptyMessage = message;
            if (_embeddedGalleryDefault is bool d)
                _embeddedGallery = Services.GridLayoutService.GetEditGallery(EditGalleryKey(DeckTableTag)) ?? d;
            SetViewMode(SwitchMode);
        }

        // ── Reload after an edit, then re-select the edited rows ───────
        private Func<object, bool>? _pendingSelect;
        private Func<object, bool>? _pendingFallback;
        private bool _applyingSelection;

        /// <summary>
        /// Re-read this table (filters, sort and layout stay), then select every
        /// row that matches <paramref name="select"/>; if none does, the first
        /// row that matches <paramref name="fallback"/>.
        /// </summary>
        public void ReloadRows(Func<object, bool>? select, Func<object, bool>? fallback = null)
        {
            _pendingSelect = select;
            _pendingFallback = fallback;
            _vm.LoadPool(_currentTag);
        }

        /// <summary>
        /// Bring an edited row into view with a few rows above it for context
        /// (like v1). Two steps — jump past it, then back — so it lands near
        /// the top instead of at the very edge, even in a long virtualized table.
        /// </summary>
        private void ShowRow(object item)
        {
            if (_galleryMode) { Gallery.ScrollToCard(item, toTop: false); return; }
            PoolGrid.UpdateLayout();
            int idx = PoolGrid.Items.IndexOf(item);
            if (idx < 0) return;
            int last = PoolGrid.Items.Count - 1;
            PoolGrid.ScrollIntoView(PoolGrid.Items[Math.Min(idx + 12, last)]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(PoolGrid.Items[Math.Max(idx - 3, 0)]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(item);
        }

        /// <summary>Called at the end of OnItemsChanged.</summary>
        private void ApplyPendingSelection()
        {
            if (_pendingSelect == null && _pendingFallback == null) return;
            var match = _pendingSelect;
            var fallback = _pendingFallback;
            _pendingSelect = null;
            _pendingFallback = null;

            var items = new List<object>();
            if (match != null)
                foreach (var row in PoolGrid.Items)
                    if (row != null && match(row)) items.Add(row);
            if (items.Count == 0 && fallback != null)
                foreach (var row in PoolGrid.Items)
                    if (row != null && fallback(row)) { items.Add(row); break; }
            if (items.Count == 0) return;

            _applyingSelection = true;         // no SelectedCardChanged per row
            try
            {
                if (items.Count == 1 || PoolGrid.SelectionMode != DataGridSelectionMode.Extended)
                    PoolGrid.SelectedItem = items[0];
                else
                {
                    PoolGrid.SelectedItems.Clear();
                    foreach (var it in items) PoolGrid.SelectedItems.Add(it);
                }
            }
            finally
            {
                _applyingSelection = false;
            }
            // After this layout pass, so the new rows exist before scrolling to one.
            var first = items[0];
            Dispatcher.BeginInvoke(new Action(() => ShowRow(first)),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
