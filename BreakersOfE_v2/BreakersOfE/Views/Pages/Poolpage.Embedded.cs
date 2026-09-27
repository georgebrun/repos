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

        /// <summary>Put keyboard focus on the grid.</summary>
        public void FocusGrid() => PoolGrid.Focus();

        // ── Reload after an edit, then re-select a row ─────────────────
        private Func<object, bool>? _pendingSelect;

        /// <summary>
        /// Re-read this table (filters, sort and layout stay) and then select
        /// the first row that matches <paramref name="select"/>, if any.
        /// </summary>
        public void ReloadRows(Func<object, bool>? select)
        {
            _pendingSelect = select;
            _vm.LoadPool(_currentTag);
        }

        /// <summary>Called at the end of OnItemsChanged.</summary>
        private void ApplyPendingSelection()
        {
            if (_pendingSelect == null) return;
            var match = _pendingSelect;
            _pendingSelect = null;
            object? item = null;
            foreach (var row in PoolGrid.Items)
                if (row != null && match(row)) { item = row; break; }
            if (item == null) return;
            PoolGrid.SelectedItem = item;
            PoolGrid.ScrollIntoView(item);
        }
    }
}