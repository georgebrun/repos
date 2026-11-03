using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    // The table, after Duels of the Planeswalkers: your mat at real playmat
    // proportions with the table around it, your badge (portrait, life, mana,
    // counters) on its left and your piles on its right, your hand held in a
    // fan along the bottom (the card under the mouse lifts and grows), the
    // card under the mouse grown to 200% where it sits, and the game log as a
    // message bar along the bottom. Part of PlaytestWindow (same class).
    public partial class PlaytestWindow
    {
        /// <summary>The card under the mouse, grown: about 42% of the window's height (readable), never less than 200%.</summary>
        private double GrownWidth(double shownWidth) =>
            Math.Max(shownWidth * 2, Math.Max(CardW * 2.3, Root.ActualHeight * 0.42 * CardAspect));

        /// <summary>A standard playmat: 24 × 14 inches.</summary>
        private const double MatAspect = 24.0 / 14.0;

        /// <summary>Size the mat to the room it has, at playmat proportions, centered.</summary>
        private void LayoutMat()
        {
            double w = Math.Max(100, FieldArea.ActualWidth - 16), h = Math.Max(60, FieldArea.ActualHeight - 16);
            double mw = Math.Min(w, h * MatAspect);
            MatBox.Width = mw;
            MatBox.Height = mw / MatAspect;
        }

        // ── Your badge ───────────────────────────────────────────────────
        /// <summary>What the badge shows now (path and framing), so the picture is loaded once.</summary>
        private string _portraitBadge = "\0";

        /// <summary>
        /// The badge: you, the player (⚙ Table → Badge: your picture, the same for
        /// every deck; none = a plain person icon), your name, and the gold edge on your turn.
        /// </summary>
        private void RenderBadge()
        {
            string path = S.BadgePath();
            var f = S.FramingOf(PlaytestSettings.BadgeKey(S.Badge));
            string shown = path.Length == 0 ? "" : $"{path}|{f.Zoom}|{f.CenterX}|{f.CenterY}";
            if (shown != _portraitBadge)
            {
                _portraitBadge = shown;
                var bmp = path.Length > 0 ? ImageCacheService.LoadBitmap(path, 400) : null;
                if (bmp != null)
                {
                    MyPortraitBox.Child = null;
                    MyPortraitBox.Background = new ImageBrush(Framed(bmp, f, 1.0, Brushes.Black)) { Stretch = Stretch.Fill };
                }
                else
                {
                    // No picture: a plain person icon.
                    MyPortraitBox.Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
                    MyPortraitBox.Child = new TextBlock
                    {
                        Text = "\uE77B", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 54,
                        Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    };
                }
            }
            MyName.Text = (Me.IsYou ? "YOU" : Me.Name.ToUpperInvariant()) + (_deck.Name.Length > 0 ? " · " + _deck.Name.ToUpperInvariant() : "");
            bool myTurn = _game.Seats.Count < 2 || _game.ActiveSeat == Me.Index;
            MyPortraitBox.BorderBrush = myTurn ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255));
            MatBox.BorderBrush = myTurn && _game.Seats.Count > 1 ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
        }

        // ── Your hand, held ──────────────────────────────────────────────
        /// <summary>Where the first card of the fan starts (the mouse is read against it).</summary>
        private double _handStart;

        /// <summary>
        /// The hand as you'd hold it: a fan along the bottom edge, overlapping,
        /// tilted out from the middle, the lower part off the table. The card
        /// under the mouse comes up out of the fan, upright, at 200%. Past what
        /// fits, the hand scrolls sideways (the mouse wheel).
        /// </summary>
        private void RenderHand()
        {
            HandArea.Children.Clear();
            double w = CardW * 1.15, h = w / CardAspect;
            HandArea.Height = h * 0.72;                               // the rest of the cards is below the table's edge
            int n = Me.Hand.Count;
            HandText.Text = n == 0 ? "" : $"HAND · {n}";
            if (n == 0) { _handScroll = 0; _handStep = 0; return; }
            double room = Math.Max(w, HandArea.ActualWidth);
            double step = n == 1 ? 0 : Math.Clamp((room - w) / (n - 1), w * 0.30, w * 0.62);
            double total = (n - 1) * step + w;
            if (total > room)
            {
                _handScroll = Math.Clamp(_handScroll, 0, total - room);
                _handStart = -_handScroll;
                HandText.Text = $"HAND · {n} · scroll for more";
            }
            else
            {
                _handScroll = 0;
                _handStart = (room - total) / 2;                    // centered in front of you
            }
            _handStep = step;
            double mid = (n - 1) / 2.0;
            for (int i = 0; i < n; i++)
            {
                var c = Me.Hand[i];
                double t = n == 1 ? 0 : (i - mid) / Math.Max(1, mid);          // -1 … 1 across the fan
                double left = _handStart + i * step;
                bool lifted = c.Id == _hoverId && _drag?.Started != true;
                FrameworkElement v;
                if (lifted)
                {
                    // Up out of the fan, upright, twice the size, kept inside the table.
                    double bw = GrownWidth(w), bh = bw / CardAspect;
                    v = CardVisual(c, bw, bh);
                    double x = Math.Clamp(left + w / 2 - bw / 2, -HandArea.Margin.Left + 8, room + HandArea.Margin.Right - bw - 8);
                    Canvas.SetLeft(v, x);
                    Canvas.SetTop(v, HandArea.Height - bh - 6);
                    v.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.7 };
                    Panel.SetZIndex(v, 1000);
                }
                else
                {
                    v = CardVisual(c, w, h);
                    Canvas.SetLeft(v, left);
                    Canvas.SetTop(v, t * t * h * 0.10 + 4);                     // the ends of the fan sit a little lower
                    v.RenderTransform = new RotateTransform(t * Math.Min(12, 2.4 * n), w / 2, h * 1.6);
                    Panel.SetZIndex(v, i);
                }
                if (Me.BottomPending > 0 || _discardDown > 0) v.Cursor = Cursors.Hand;
                if (c.IsModal) FacePicture(c.Card, true);                       // the back's picture ready for the face choice
                Hook(v, c, Zone.Hand);
                HandArea.Children.Add(v);
            }
        }

        /// <summary>The mouse left the hand: the lifted card goes back into the fan.</summary>
        private void HandRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_drag?.Started == true || Find(_hoverId) is not { } held || Me.ZoneOf(held) != Zone.Hand) return;
            if (HandRow.IsMouseOver) return;
            _hoverId = 0;
            QueueRender();
        }

        // ── The card under the mouse, grown ──────────────────────────────
        /// <summary>
        /// The card under the mouse at 200% of its size on the table, upright,
        /// centered on it (kept in the window). It doesn't catch the mouse, so
        /// pointing moves on as before. The hand lifts its own card instead.
        /// </summary>
        private void ShowHover(GameCard? c)
        {
            HoverLayer.Children.Clear();
            if (!S.ShowCardPanel || c == null || _drag?.Started == true || ZoomLayer.Visibility == Visibility.Visible) return;
            if (_fan.Any(f => f.Card.Id == c.Id)) return;                       // the fan already shows it big enough
            var zone = Me.ZoneOf(c);
            if (zone == Zone.Hand) return;
            if (!_visuals.TryGetValue(c.Id, out var v)) return;
            ShowGrown(c, v);
        }

        /// <summary>A card grown over where it sits (<paramref name="v"/>: its picture on the table), upright, kept in the window.</summary>
        private void ShowGrown(GameCard c, FrameworkElement v)
        {
            HoverLayer.Children.Clear();
            if (!S.ShowCardPanel || _drag?.Started == true || ZoomLayer.Visibility == Visibility.Visible) return;
            if (!v.IsVisible || !v.IsDescendantOf(Root)) return;
            Rect at;
            try { at = v.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, v.ActualWidth, v.ActualHeight)); }
            catch (InvalidOperationException) { return; }
            double baseW = c.Tapped ? Math.Min(at.Width, at.Height) : at.Width;     // a tapped card lies sideways
            double bw = GrownWidth(baseW), bh = bw / CardAspect;                       // readable: big enough to read the card
            var big = InSleeveOf(c.Seat, () => CardVisual(c, bw, bh));            // face down: its own player's sleeve
            big.RenderTransform = null;                                          // upright, to read
            big.IsHitTestVisible = false;
            big.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.75 };
            double x = Math.Clamp(at.X + at.Width / 2 - bw / 2, 4, Math.Max(4, Root.ActualWidth - bw - 4));
            double y = Math.Clamp(at.Y + at.Height / 2 - bh / 2, 4, Math.Max(4, Root.ActualHeight - bh - 4));
            Canvas.SetLeft(big, x);
            Canvas.SetTop(big, y);
            HoverLayer.Children.Add(big);
        }

        /// <summary>
        /// The mouse left a card on the table: its grown copy goes. A redraw swaps
        /// the card's picture for a new one under a still mouse (WPF calls that
        /// "leaving"), so this asks where the mouse really is, once things settle.
        /// </summary>
        private void HoverLeft(int id)
        {
            if (HoverLayer.Children.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_visuals.TryGetValue(id, out var v) && PointerOn(v)) return;
                if (Find(_hoverId) is { } hc && _visuals.TryGetValue(hc.Id, out var hv) && PointerOn(hv)) return;
                if (OppHovered() is { } oc && _oppVisuals.TryGetValue(oc.Id, out var ov) && PointerOn(ov)) return;    // onto their card
                HoverLayer.Children.Clear();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>Is the mouse inside this card's outline on the table (whatever WPF's mouse-over says)?</summary>
        private bool PointerOn(FrameworkElement v)
        {
            if (!v.IsVisible || !v.IsDescendantOf(Root)) return false;
            try
            {
                var r = v.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, v.ActualWidth, v.ActualHeight));
                return r.Contains(Mouse.GetPosition(Root));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // ── The message bar ──────────────────────────────────────────────
        private void BtnLogSize_Click(object sender, RoutedEventArgs e)
        {
            bool small = LogRowDef.ActualHeight < 80;
            LogRowDef.Height = new GridLength(small ? 220 : 40);
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        }

        /// <summary>
        /// Closed (one line): the line and the buttons sit in the middle of the bar.
        /// Expanded (by the button or by dragging): the messages fill it from the top,
        /// the buttons stay at the top right. The button reads Expand or Collapse to match.
        /// </summary>
        private void LogBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool small = e.NewSize.Height < 80;
            LogList.VerticalAlignment = small ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            LogButtons.VerticalAlignment = small ? VerticalAlignment.Center : VerticalAlignment.Top;
            BtnLogSize.Content = small ? "Expand" : "Collapse";
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        }

        // The arrows sit side by side (no scroll bar squeezed into one line); a line at a time.
        private void BtnLogUp_Click(object sender, RoutedEventArgs e) => LogScroller()?.LineUp();
        private void BtnLogDown_Click(object sender, RoutedEventArgs e) => LogScroller()?.LineDown();

        /// <summary>The scroller inside the message list.</summary>
        private ScrollViewer? LogScroller()
        {
            DependencyObject? d = LogList;
            while (d != null && d is not ScrollViewer)
                d = VisualTreeHelper.GetChildrenCount(d) > 0 ? VisualTreeHelper.GetChild(d, 0) : null;
            return d as ScrollViewer;
        }

        private void BtnLogCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, LogList.Items.Cast<object>().Select(o => o?.ToString()))); }
            catch { /* the clipboard was busy */ }
        }

        private void BtnLogClear_Click(object sender, RoutedEventArgs e) => LogList.Items.Clear();

        // ── The zoom: just the card; a two-faced card turns over ─────────
        private GameCard? _zoomCard;
        private bool _zoomBack;

        private void BtnZoomTurn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_zoomCard == null) return;
            _zoomBack = !_zoomBack;
            Zoom(_zoomCard);
        }
    }
}
