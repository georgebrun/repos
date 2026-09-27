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
    // Left detail panel and the card detail pop-up.
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // LEFT DETAIL PANEL — shared CardDetailPanel control
        // ══════════════════════════════════════════════════════════════════
        private void PoolGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Gallery.SelectCard(PoolGrid.SelectedItem);
            Detail.ShowCard(PoolGrid.SelectedItem);
            SelectedCardChanged?.Invoke(PoolGrid.SelectedItem);
        }


        private void ClearDetail() => Detail.ShowCard(null);

        // ══════════════════════════════════════════════════════════════════
        // CARD DETAIL POPUP — double-click grid row or detail image
        // ══════════════════════════════════════════════════════════════════
        private CardDetailWindow? _cardDetailWindow;

        private void PoolGrid_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PoolGrid.SelectedItem == null) return;

            // Edit page: a double-click on some cells (e.g. Qty) edits instead.
            if (CellDoubleClickHandler != null &&
                FindParent<DataGridCell>(e.OriginalSource as DependencyObject) is { } cell &&
                CellDoubleClickHandler(PoolGrid.SelectedItem, ColumnHeader(cell.Column), cell))
            {
                e.Handled = true;
                return;
            }

            OpenCardDetailPopup(PoolGrid.SelectedItem);
        }

        private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T)
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private void Detail_ImageDoubleClicked(object? sender, EventArgs e)
        {
            if (PoolGrid.SelectedItem == null) return;
            OpenCardDetailPopup(PoolGrid.SelectedItem);
        }

        private void OpenCardDetailPopup(object card)
        {
            if (_cardDetailWindow != null && _cardDetailWindow.IsVisible)
            {
                _cardDetailWindow.Close();
                _cardDetailWindow = null;
                return;
            }

            // Build items list and find index for prev/next navigation
            var items = new List<object>();
            int idx = -1;
            for (int i = 0; i < PoolGrid.Items.Count; i++)
            {
                var item = PoolGrid.Items[i];
                if (item != null) items.Add(item);
                if (item == card) idx = items.Count - 1;
            }

            // From a deck: the pop-up lists the OTHER decks (matches the Other Decks column).
            _cardDetailWindow = new CardDetailWindow(
                card, Window.GetWindow(this), items, idx,
                _currentTag == DeckTableTag ? _openDeckPath : null);
            _cardDetailWindow.CardChanged += newCard =>
            {
                PoolGrid.SelectedItem = newCard;
                if (_galleryMode) Gallery.ScrollToCard(newCard, toTop: false);
                else PoolGrid.ScrollIntoView(newCard);
            };
            _cardDetailWindow.Closed += (s, ev) => _cardDetailWindow = null;
            _cardDetailWindow.Show();
        }

        private void PoolPage_Loaded(object sender, RoutedEventArgs e)
            => DisableParentScrollViewers();

        // NavigationView wraps pages in a ScrollViewer → infinite height →
        // virtualization defeated → freeze. Kill it.
        private void DisableParentScrollViewers()
        {
            DependencyObject current = this;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is ScrollViewer sv)
                {
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    break;
                }
            }
        }
    }
}