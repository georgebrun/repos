using BreakersOfE.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Views.Pages
{
    public partial class CollectionPage : Page
    {
        // ── Search state ───────────────────────────────────────────────
        private string _lastSearchTerm = string.Empty;
        private int _searchMatchIndex = -1;
        private List<int> _searchMatchIndices = new();

        public CollectionPage()
        {
            InitializeComponent();
            Loaded += CollectionPage_Loaded;
        }

        /// <summary>
        /// Wpf.Ui's NavigationView wraps page content in an internal ScrollViewer.
        /// This gives the DataGrid infinite height, defeating row virtualization
        /// and causing it to render all 100k+ rows at once (instant freeze).
        /// 
        /// Fix: walk up the visual tree, find that ScrollViewer, disable its
        /// vertical scrolling. The DataGrid has its own internal ScrollViewer
        /// for scrolling through rows — the outer one is not needed.
        /// </summary>
        private void CollectionPage_Loaded(object sender, RoutedEventArgs e)
        {
            DisableParentScrollViewers();
        }

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
                    System.Diagnostics.Debug.WriteLine(
                        $"CollectionPage: Disabled parent ScrollViewer ({sv.Name})");
                    break;
                }
            }
        }

        /// <summary>
        /// Search bar handler — begins-with matching, scroll-to, NEVER filters.
        /// Typing a new term finds the first match.
        /// Typing the same term again cycles to the next match.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(text) || PoolGrid.Items.Count == 0)
            {
                _lastSearchTerm = string.Empty;
                _searchMatchIndex = -1;
                _searchMatchIndices.Clear();
                return;
            }

            // If the search term changed, rebuild match list
            if (!text.Equals(_lastSearchTerm, StringComparison.OrdinalIgnoreCase))
            {
                _lastSearchTerm = text;
                _searchMatchIndices.Clear();
                _searchMatchIndex = -1;

                for (int i = 0; i < PoolGrid.Items.Count; i++)
                {
                    if (PoolGrid.Items[i] is PoolCard card &&
                        card.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchMatchIndices.Add(i);
                    }
                }
            }

            if (_searchMatchIndices.Count == 0)
                return;

            // Advance to next match (wraps around)
            _searchMatchIndex++;
            if (_searchMatchIndex >= _searchMatchIndices.Count)
                _searchMatchIndex = 0;

            // Scroll to and select the match
            int matchIdx = _searchMatchIndices[_searchMatchIndex];
            var matchItem = PoolGrid.Items[matchIdx];
            PoolGrid.ScrollIntoView(matchItem);
            PoolGrid.SelectedItem = matchItem;
        }
    }
}