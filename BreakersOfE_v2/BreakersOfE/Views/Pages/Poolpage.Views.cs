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
    // View modes (Grid, Gallery, Sets, Decks), the gallery feed and grid-row focus.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // CARD GALLERY — its own view (Views/Controls/GalleryView). This page
        // feeds it the grid's sorted/filtered cards and shares the selection.
        // ══════════════════════════════════════════════════════════════════
        private bool _galleryMode => _viewMode == PoolViewMode.Gallery;

        // ── View modes: Grid, Gallery, Sets, Decks ──────────────────────
        private enum PoolViewMode { Grid, Gallery, Sets, Decks }

        /// <summary>A browser (set or deck tiles), not a card view.</summary>
        private static bool IsBrowser(PoolViewMode m) => m == PoolViewMode.Sets || m == PoolViewMode.Decks;
        private PoolViewMode _viewMode = PoolViewMode.Grid;

        /// <summary>The card view the switch above the navigation is set to.</summary>
        private static PoolViewMode SwitchMode =>
            Services.CardViewModeService.Mode == Services.CardViewMode.Gallery
                ? PoolViewMode.Gallery : PoolViewMode.Grid;

        /// <summary>
        /// Switch flipped. In the set browser nothing changes on screen; the
        /// switch just decides what opening a set shows.
        /// </summary>
        private void OnCardViewModeChanged(Services.CardViewMode _)
        {
            if (!IsBrowser(_viewMode)) SetViewMode(SwitchMode);
        }

        private void SetViewMode(PoolViewMode mode)
        {
            var previous = _viewMode;
            var selected = PoolGrid.SelectedItem;

            _viewMode = mode;
            UpdateChecklist();

            PoolGrid.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            Gallery.Visibility = mode == PoolViewMode.Gallery ? Visibility.Visible : Visibility.Collapsed;
            SetList.Visibility = mode == PoolViewMode.Sets ? Visibility.Visible : Visibility.Collapsed;
            DeckList.Visibility = mode == PoolViewMode.Decks ? Visibility.Visible : Visibility.Collapsed;

            // Header buttons: Edition sort only matters in the grid; the
            // browsers use none of the card-view buttons.
            bool cardView = !IsBrowser(mode);
            BtnClearFilters.Visibility = cardView ? Visibility.Visible : Visibility.Collapsed;
            BtnEditionOrder.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            BtnColumns.Visibility = mode == PoolViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
            BtnSetCompletion.Visibility = mode == PoolViewMode.Sets ? Visibility.Visible : Visibility.Collapsed;
            BtnStatistics.Visibility = cardView && (_currentTag == "Collection" || _currentTag == DeckTableTag)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnLegality.Visibility = mode == PoolViewMode.Grid && _currentTag == "Collection"
                ? Visibility.Visible : Visibility.Collapsed;
            bool showTotals = mode == PoolViewMode.Grid &&
                              (IsCollectionKind(KindOf(_currentTag)) || _currentTag == DeckTableTag);
            TotalsGrid.Visibility = showTotals ? Visibility.Visible : Visibility.Collapsed;
            if (showTotals)
            {
                HookTotalsScroll();
                QueueTotalsSync();
            }
            BtnBackToSets.Visibility = cardView && (_inSetsContext || _inDecksContext)
                ? Visibility.Visible : Visibility.Collapsed;
            BtnBackToSets.Content = _inDecksContext ? "◀  All Decks" : "◀  All Sets";
            BtnBackToSets.ToolTip = _inDecksContext ? "Back to the deck browser" : "Back to the set browser";

            if (mode == PoolViewMode.Sets)
            {
                _vm.Title = "Sets";
                if (_setTiles != null) _vm.StatusText = $"{_setTiles.Count:N0} sets";
            }
            else if (mode == PoolViewMode.Decks)
            {
                _vm.Title = "Decks";
                _vm.StatusText = _deckTiles.Count == 1 ? "1 deck" : $"{_deckTiles.Count:N0} decks";
            }

            // Wait for layout so the new view has a real width to size rows.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (mode)
                {
                    case PoolViewMode.Gallery:
                        Gallery.Show(GridOrder(), selected);
                        if (selected != null) Gallery.ScrollToCard(selected, toTop: true);
                        break;
                    case PoolViewMode.Grid:
                        if (selected != null && previous != PoolViewMode.Grid)
                            FocusGridRow(selected);
                        break;
                    case PoolViewMode.Sets:
                        if (_vm.AllRows.Count == 0) break;   // still loading; OnItemsChanged builds
                        BuildSetTilesIfNeeded();
                        RebuildSetRows();
                        _vm.StatusText = $"{_setTiles!.Count:N0} sets";
                        break;
                    case PoolViewMode.Decks:
                        RebuildDeckRows();
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Select a card in the grid, put its row at the top (same two-step as
        /// search), and give the row keyboard focus. Without focus the DataGrid
        /// draws selection with its faint "inactive" color, which is nearly
        /// invisible on the dark theme, and arrow keys don't start from it.
        /// </summary>
        private void FocusGridRow(object card)
        {
            PoolGrid.SelectedItem = card;

            int idx = PoolGrid.Items.IndexOf(card);
            if (idx < 0) return;

            PoolGrid.UpdateLayout();
            int jump = Math.Min(idx + 50, PoolGrid.Items.Count - 1);
            PoolGrid.ScrollIntoView(PoolGrid.Items[jump]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(card);
            PoolGrid.UpdateLayout();

            if (PoolGrid.Columns.Count > 0)
                PoolGrid.CurrentCell = new DataGridCellInfo(card, PoolGrid.Columns[0]);

            if (PoolGrid.ItemContainerGenerator.ContainerFromItem(card) is DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
            else
            {
                PoolGrid.Focus();
            }
        }

        /// <summary>Gallery follows the grid's data and order.</summary>
        private void RebuildGalleryIfVisible()
        {
            if (_galleryMode) Gallery.Show(GridOrder(), PoolGrid.SelectedItem);
        }

        /// <summary>The grid's cards in display order (sort + filters applied).</summary>
        private System.Collections.IEnumerable GridOrder() =>
            CollectionViewSource.GetDefaultView(PoolGrid.ItemsSource)
                ?? (System.Collections.IEnumerable)Array.Empty<object>();
    }
}
