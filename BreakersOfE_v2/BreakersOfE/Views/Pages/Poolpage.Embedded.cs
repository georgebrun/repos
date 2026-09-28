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
        private string LayoutKey(string table) => _layoutPrefix + table;

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

        /// <summary>Hosted inside an Edit page: no detail panel, tighter margins, own layouts.</summary>
        public void SetEmbedded()
        {
            Detail.Visibility = Visibility.Collapsed;
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

        /// <summary>Right-click menu for the grid rows (Edit actions).</summary>
        public void SetRowContextMenu(ContextMenu menu) => PoolGrid.ContextMenu = menu;

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

        /// <summary>Put keyboard focus on the grid.</summary>
        public void FocusGrid() => PoolGrid.Focus();

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
            PoolGrid.ScrollIntoView(items[0]);
        }
    }
}
