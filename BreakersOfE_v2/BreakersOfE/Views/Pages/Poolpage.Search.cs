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
    // Begins-with search for the grid, gallery and set browser (never hides rows).
    // Part of PoolPage (split from Poolpage.xaml.cs; same class, no behavior change).
    public partial class PoolPage
    {
        // ══════════════════════════════════════════════════════════════════
        // SEARCH — begins-with, scroll to top, debounced. Never hides rows.
        // ══════════════════════════════════════════════════════════════════
        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private List<string> _searchNames = new();

        private void RebuildSearchIndex()
        {
            _searchNames.Clear();
            if (PoolGrid.Items.Count == 0) return;
            System.Reflection.PropertyInfo? namePi = null;
            for (int i = 0; i < PoolGrid.Items.Count; i++)
            {
                var item = PoolGrid.Items[i];
                if (item == null) { _searchNames.Add(""); continue; }
                namePi ??= item.GetType().GetProperty("Name");
                _searchNames.Add(namePi?.GetValue(item) as string ?? "");
            }
        }

        private void ResetSearch()
        {
            _lastSearchTerm = string.Empty;
            _searchMatchIndex = -1;
            _searchMatchIndices.Clear();
            if (SearchBox != null) SearchBox.Text = string.Empty;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce — wait 150ms after last keystroke
            _searchTimer?.Stop();
            _searchTimer ??= new System.Windows.Threading.DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchTimer.Tick -= SearchTimer_Tick;
            _searchTimer.Tick += SearchTimer_Tick;
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer?.Stop();
            ExecuteSearch();
        }

        private void ExecuteSearch()
        {
            var text = SearchBox.Text?.Trim();

            // Set browser: jump to a set by name (never hides anything)
            if (_viewMode == PoolViewMode.Sets)
            {
                if (!string.IsNullOrEmpty(text)) SearchSets(text);
                return;
            }
            // Deck browser: jump to a deck by name (never hides anything)
            if (_viewMode == PoolViewMode.Decks)
            {
                if (!string.IsNullOrEmpty(text)) SearchDecks(text);
                return;
            }
            if (string.IsNullOrEmpty(text) || PoolGrid.Items.Count == 0)
            {
                _lastSearchTerm = string.Empty;
                _searchMatchIndex = -1;
                _searchMatchIndices.Clear();
                return;
            }

            if (!text.Equals(_lastSearchTerm, StringComparison.OrdinalIgnoreCase))
            {
                _lastSearchTerm = text;
                _searchMatchIndices.Clear();
                _searchMatchIndex = -1;

                // Use cached names — no reflection per keystroke
                for (int i = 0; i < _searchNames.Count; i++)
                {
                    if (_searchNames[i].StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        _searchMatchIndices.Add(i);
                }
            }

            if (_searchMatchIndices.Count == 0) return;

            _searchMatchIndex++;
            if (_searchMatchIndex >= _searchMatchIndices.Count) _searchMatchIndex = 0;

            var matchItem = PoolGrid.Items[_searchMatchIndices[_searchMatchIndex]];
            PoolGrid.SelectedItem = matchItem;

            // Gallery mode: put the match's row at the top of the gallery
            if (_galleryMode)
            {
                Gallery.ScrollToCard(matchItem, toTop: true);
                return;
            }

            // Scroll the match to the TOP of the visible area.
            // Two-step trick: first scroll far past the target so it's above
            // the viewport, then ScrollIntoView scrolls UP to put it at top.
            PoolGrid.UpdateLayout();
            int matchRow = _searchMatchIndices[_searchMatchIndex];
            int jumpTarget = Math.Min(matchRow + 50, PoolGrid.Items.Count - 1);
            PoolGrid.ScrollIntoView(PoolGrid.Items[jumpTarget]);
            PoolGrid.UpdateLayout();
            PoolGrid.ScrollIntoView(matchItem);
        }

        /// <summary>Search in the set browser: jump to the first set name that begins with the text.</summary>
        private void SearchSets(string text)
        {
            for (int r = 0; r < _setRows.Count; r++)
            {
                var row = _setRows[r];
                if (row.IsHeader) continue;
                var match = row.Tiles.FirstOrDefault(t =>
                    t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;

                if (_setHighlighted != null) _setHighlighted.IsHighlighted = false;
                match.IsHighlighted = true;
                _setHighlighted = match;

                // Put the row at the top (same two-step as the grid and gallery).
                SetList.UpdateLayout();
                int jump = Math.Min(r + 10, _setRows.Count - 1);
                SetList.ScrollIntoView(_setRows[jump]);
                SetList.UpdateLayout();
                SetList.ScrollIntoView(row);
                return;
            }
        }
    }
}
