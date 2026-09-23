using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreakersOfE.ViewModels;

namespace BreakersOfE.Views.Controls
{
    /// <summary>
    /// Card gallery view. Self-contained: the host page gives it cards in
    /// display order (<see cref="Show"/>) and handles
    /// <see cref="CardClicked"/> / <see cref="CardOpened"/>. Selection lives
    /// with the host; the gallery just highlights what it's told
    /// (<see cref="SelectCard"/>).
    ///
    /// Virtualized: each ListBox item is a ROW of N tiles, only visible rows
    /// are built, and only visible tiles load images (on demand, cached by
    /// ScryfallId via ImageCacheService).
    /// </summary>
    public partial class GalleryView : UserControl
    {
        /// <summary>Single click on a tile.</summary>
        public event Action<object>? CardClicked;
        /// <summary>Double click on a tile.</summary>
        public event Action<object>? CardOpened;

        private readonly Dictionary<object, GalleryItem> _items =
            new(ReferenceEqualityComparer.Instance);
        private readonly List<GalleryItem> _ordered = new();
        private List<GalleryRow> _rows = new();
        private GalleryItem? _selected;
        private int _perRow = 1;
        private double _rowHeight = 1;

        private const double TileMargin = 8;          // 4 each side (matches XAML)
        private const double CardAspect = 680.0 / 488.0;

        public GalleryView()
        {
            InitializeComponent();

            // App-wide download counter: listen only while on screen.
            Loaded += (_, _) =>
            {
                Services.ImageCacheService.PendingChanged += OnPendingChanged;
                UpdateDownloadsPill(Services.ImageCacheService.Pending);
            };
            Unloaded += (_, _) =>
                Services.ImageCacheService.PendingChanged -= OnPendingChanged;
            IsVisibleChanged += (_, _) =>
                UpdateDownloadsPill(Services.ImageCacheService.Pending);
        }

        // ── Host API ────────────────────────────────────────────────────

        /// <summary>
        /// Show these cards in this order (the host's sorted, filtered view),
        /// starting at the top, highlighting <paramref name="selected"/>.
        /// </summary>
        public void Show(IEnumerable cardsInOrder, object? selected)
        {
            _ordered.Clear();
            foreach (var card in cardsInOrder)
            {
                if (card == null) continue;
                if (!_items.TryGetValue(card, out var gi))
                {
                    gi = GalleryItem.FromCard(card);
                    _items[card] = gi;
                }
                _ordered.Add(gi);
            }
            RebuildRows(keepPosition: false);
            SelectCard(selected);
        }

        /// <summary>New data set (e.g. another pool type): drop cached tiles.</summary>
        public void Reset()
        {
            _items.Clear();
            _ordered.Clear();
            _rows = new List<GalleryRow>();
            _selected = null;
            GalleryList.ItemsSource = null;
        }

        /// <summary>Highlight the tile for this card (null clears).</summary>
        public void SelectCard(object? card)
        {
            if (_selected != null) _selected.IsSelected = false;
            _selected = null;
            if (card != null && _items.TryGetValue(card, out var gi))
            {
                gi.IsSelected = true;
                _selected = gi;
            }
        }

        /// <summary>
        /// Scroll to a card's row. toTop = put the row at the top (search);
        /// otherwise just bring it into view (prev/next navigation).
        /// </summary>
        public void ScrollToCard(object card, bool toTop)
        {
            if (!_items.TryGetValue(card, out var gi)) return;
            int idx = _ordered.IndexOf(gi);
            if (idx < 0 || _rows.Count == 0) return;

            int row = Math.Min(idx / _perRow, _rows.Count - 1);
            GalleryList.UpdateLayout();
            if (toTop)
            {
                // Proven two-step: jump past, then scroll back up so the
                // target row lands at the top.
                int jump = Math.Min(row + 10, _rows.Count - 1);
                GalleryList.ScrollIntoView(_rows[jump]);
                GalleryList.UpdateLayout();
            }
            GalleryList.ScrollIntoView(_rows[row]);
        }

        // ── Rows ────────────────────────────────────────────────────────

        private int PerRowForWidth()
        {
            double tileW = GallerySizeSlider.Value;
            double avail = GalleryList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
            return Math.Max(1, (int)(avail / (tileW + TileMargin)));
        }

        /// <summary>
        /// Chunk tiles into rows of N, where N fits the current width at the
        /// current card size. Optionally keep the user's place.
        /// </summary>
        private void RebuildRows(bool keepPosition)
        {
            // Remember which card is at the top, to restore after resize.
            int anchorIndex = 0;
            var sv = FindVisualChild<ScrollViewer>(GalleryList);
            if (keepPosition && sv != null && _rowHeight > 0)
                anchorIndex = (int)(sv.VerticalOffset / _rowHeight) * _perRow;

            double tileW = GallerySizeSlider.Value;
            double tileH = Math.Round(tileW * CardAspect);
            int perRow = PerRowForWidth();

            _perRow = perRow;
            _rowHeight = tileH + TileMargin;

            var rows = new List<GalleryRow>(_ordered.Count / perRow + 1);
            for (int i = 0; i < _ordered.Count; i += perRow)
            {
                var chunk = _ordered.GetRange(i, Math.Min(perRow, _ordered.Count - i));
                foreach (var gi in chunk) { gi.TileWidth = tileW; gi.TileHeight = tileH; }
                rows.Add(new GalleryRow(chunk));
            }
            _rows = rows;
            GalleryList.ItemsSource = rows;

            if (!keepPosition)
            {
                // A fresh build (new data, filter, or sort) starts at the top.
                sv?.ScrollToTop();
            }
            else if (anchorIndex > 0)
            {
                int row = anchorIndex / perRow;
                Dispatcher.BeginInvoke(new Action(() =>
                    FindVisualChild<ScrollViewer>(GalleryList)?
                        .ScrollToVerticalOffset(row * _rowHeight)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void GalleryList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || _ordered.Count == 0) return;
            // Only rebuild when the number of cards per row actually changes.
            if (PerRowForWidth() != _perRow) RebuildRows(keepPosition: true);
        }

        private void GallerySizeSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // Also fires during InitializeComponent: guard until ready.
            if (GalleryList == null || _ordered.Count == 0) return;
            RebuildRows(keepPosition: true);
        }

        // ── Tiles ───────────────────────────────────────────────────────

        private void GalleryTile_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GalleryItem gi) return;

            CardClicked?.Invoke(gi.Card);
            if (e.ClickCount == 2)
                CardOpened?.Invoke(gi.Card);
        }

        // ── Downloads pill ──────────────────────────────────────────────

        // Event fires off the UI thread.
        private void OnPendingChanged(int n) =>
            Dispatcher.BeginInvoke(new Action(() => UpdateDownloadsPill(n)));

        private void UpdateDownloadsPill(int pending)
        {
            if (IsVisible && pending > 0)
            {
                DownloadsPillText.Text = pending == 1
                    ? "1 download in progress"
                    : $"{pending} downloads in progress";
                DownloadsPill.Visibility = Visibility.Visible;
            }
            else
            {
                DownloadsPill.Visibility = Visibility.Collapsed;
            }
        }

        // ── Visual tree helper ──────────────────────────────────────────
        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
