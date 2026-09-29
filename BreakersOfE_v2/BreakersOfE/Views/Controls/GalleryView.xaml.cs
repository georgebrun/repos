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
        /// <summary>Single click on a tile (with Ctrl / Shift for several).</summary>
        public event Action<object, System.Windows.Input.ModifierKeys>? CardClicked;
        /// <summary>Double click on a tile.</summary>
        public event Action<object>? CardOpened;
        /// <summary>Right-click on a tile (before its menu opens).</summary>
        public event Action<object>? CardRightClicked;
        /// <summary>Edit pages: the tile's + (with the keys held: Shift = foil, Ctrl = etched).</summary>
        public event Action<object, System.Windows.Input.ModifierKeys>? CardAdd;
        /// <summary>Edit pages: the tile's −.</summary>
        public event Action<object, System.Windows.Input.ModifierKeys>? CardRemove;

        /// <summary>Show the + / − buttons on the tiles (Edit pages).</summary>
        public static readonly DependencyProperty ShowEditButtonsProperty = DependencyProperty.Register(
            nameof(ShowEditButtons), typeof(bool), typeof(GalleryView), new PropertyMetadata(false));
        public bool ShowEditButtons
        {
            get => (bool)GetValue(ShowEditButtonsProperty);
            set => SetValue(ShowEditButtonsProperty, value);
        }

        /// <summary>The menu for right-clicking a tile (Edit pages: the same as the grid's rows).</summary>
        public ContextMenu? TileContextMenu
        {
            get => GalleryList.ContextMenu;
            set => GalleryList.ContextMenu = value;
        }

        private readonly Dictionary<object, GalleryItem> _items =
            new(ReferenceEqualityComparer.Instance);
        private readonly List<GalleryItem> _ordered = new();
        private List<GalleryRow> _rows = new();
        private readonly List<GalleryItem> _selected = new();
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
        /// Tiles for which this returns true show dimmed (set checklist:
        /// printings you don't own). Null = nothing dimmed. Applied on Show.
        /// </summary>
        public Func<object, bool>? DimWhen { get; set; }

        /// <summary>
        /// Show these cards in this order (the host's sorted, filtered view),
        /// starting at the top, highlighting <paramref name="selected"/>.
        /// </summary>
        public void Show(IEnumerable cardsInOrder, object? selected)
        {
            // Keep tiles only for the cards shown now (an Edit reload brings new
            // row objects; the old tiles must not pile up).
            _ordered.Clear();
            var keep = new Dictionary<object, GalleryItem>(ReferenceEqualityComparer.Instance);
            foreach (var card in cardsInOrder)
            {
                if (card == null) continue;
                if (!_items.TryGetValue(card, out var gi))
                    gi = GalleryItem.FromCard(card);
                keep[card] = gi;
                gi.IsDimmed = DimWhen?.Invoke(card) ?? false;
                _ordered.Add(gi);
            }
            _items.Clear();
            foreach (var kv in keep) _items[kv.Key] = kv.Value;
            _selected.RemoveAll(gi => !keep.ContainsKey(gi.Card));
            RebuildRows(keepPosition: false);
            SelectCard(selected);
        }

        /// <summary>New data set (e.g. another pool type): drop cached tiles.</summary>
        public void Reset()
        {
            _items.Clear();
            _ordered.Clear();
            _rows = new List<GalleryRow>();
            _selected.Clear();
            GalleryList.ItemsSource = null;
        }

        /// <summary>Highlight the tile for this card (null clears).</summary>
        public void SelectCard(object? card) =>
            SelectCards(card == null ? Array.Empty<object>() : new[] { card });

        /// <summary>Highlight these cards' tiles (several on Edit pages).</summary>
        public void SelectCards(IEnumerable<object> cards)
        {
            foreach (var gi in _selected) gi.IsSelected = false;
            _selected.Clear();
            foreach (var card in cards)
            {
                if (card != null && _items.TryGetValue(card, out var gi))
                {
                    gi.IsSelected = true;
                    _selected.Add(gi);
                }
            }
        }

        /// <summary>The card tiles have keyboard focus (not the size slider).</summary>
        public bool TilesHaveFocus => GalleryList.IsKeyboardFocusWithin;

        /// <summary>Re-read a tile's count marker (after an edit).</summary>
        public void RefreshCard(object card)
        {
            if (_items.TryGetValue(card, out var gi)) gi.RefreshCounts();
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

            GalleryList.Focus();               // keys (Enter, Delete, Ctrl+Z) go to the gallery
            CardClicked?.Invoke(gi.Card, System.Windows.Input.Keyboard.Modifiers);
            if (e.ClickCount == 2)
                CardOpened?.Invoke(gi.Card);
        }

        private void GalleryTile_PreviewMouseRightButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is GalleryItem gi)
            {
                GalleryList.Focus();
                CardRightClicked?.Invoke(gi.Card);
            }
        }

        private void TileAdd_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is GalleryItem gi)
                CardAdd?.Invoke(gi.Card, System.Windows.Input.Keyboard.Modifiers);
        }

        private void TileRemove_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is GalleryItem gi)
                CardRemove?.Invoke(gi.Card, System.Windows.Input.Keyboard.Modifiers);
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
