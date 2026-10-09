using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BreakersOfE.Views.Controls
{
    /// <summary>One stop of the Program Tour: what it says, and the part of the window it outlines (null: none).</summary>
    public sealed record TourStep(string Title, string Text, Func<FrameworkElement?> Target);

    /// <summary>
    /// The Program Tour (Settings → Restart Tour shows it again): the main
    /// window dimmed, one part at a time outlined, and a box beside it with
    /// Back / Next, Skip Tour, ✕ and Do Not Show Again. ← / → / Enter move,
    /// Esc closes.
    /// </summary>
    public partial class TourOverlay : UserControl
    {
        private IReadOnlyList<TourStep> _steps = Array.Empty<TourStep>();
        private int _at;

        /// <summary>The tour ended: finished (the last stop's Finish), and Do Not Show Again ticked.</summary>
        public event Action<bool, bool>? Ended;

        public bool IsRunning => Visibility == Visibility.Visible;

        public TourOverlay()
        {
            InitializeComponent();
            PreviewKeyDown += (_, e) =>
            {
                if (!IsRunning) return;
                switch (e.Key)
                {
                    case Key.Escape: End(finished: false); e.Handled = true; break;
                    case Key.Right: Go(_at + 1); e.Handled = true; break;
                    case Key.Left: Go(_at - 1); e.Handled = true; break;
                    // Enter: Next — unless a button or the checkbox has the focus (they take Enter themselves).
                    case Key.Enter or Key.Return when Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase:
                        Go(_at + 1); e.Handled = true; break;
                }
            };
        }

        public void Start(IReadOnlyList<TourStep> steps, bool doNotShowTicked)
        {
            if (steps.Count == 0) return;
            _steps = steps;
            ChkDontShow.IsChecked = doNotShowTicked;
            Visibility = Visibility.Visible;
            Go(0);
            Dispatcher.BeginInvoke(new Action(() => BtnNext.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void Go(int index)
        {
            if (index < 0) return;
            if (index >= _steps.Count) { End(finished: true); return; }
            _at = index;
            var step = _steps[index];
            StepText.Text = $"Step {index + 1} of {_steps.Count}";
            TitleText.Text = step.Title;
            BodyText.Text = step.Text;
            BtnBack.IsEnabled = index > 0;
            BtnNext.Content = index == _steps.Count - 1 ? "Finish" : "Next  ▶";
            // Let the window settle (a section may have just opened), then outline the part.
            Dispatcher.BeginInvoke(new Action(Place), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void End(bool finished)
        {
            Visibility = Visibility.Collapsed;
            Ended?.Invoke(finished, ChkDontShow.IsChecked == true);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e) => Go(_at + 1);
        private void BtnBack_Click(object sender, RoutedEventArgs e) => Go(_at - 1);
        private void BtnClose_Click(object sender, RoutedEventArgs e) => End(finished: false);
        private void Overlay_SizeChanged(object sender, SizeChangedEventArgs e) { if (IsRunning) Place(); }

        /// <summary>Dim everything but the step's part, outline it, and put the box beside it.</summary>
        private void Place()
        {
            if (!IsRunning || _steps.Count == 0) return;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var full = new Rect(0, 0, w, h);
            Rect? part = Bounds(_steps[_at].Target());

            if (part is { } r)
            {
                r.Inflate(4, 4);
                r.Intersect(full);
                Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(full), new RectangleGeometry(r, 6, 6));
                Ring.Margin = new Thickness(r.X, r.Y, 0, 0);
                Ring.Width = r.Width;
                Ring.Height = r.Height;
                Ring.Visibility = Visibility.Visible;
            }
            else
            {
                Dim.Data = new RectangleGeometry(full);
                Ring.Visibility = Visibility.Collapsed;
            }

            Callout.Measure(new Size(Callout.Width, double.PositiveInfinity));
            double cw = Callout.Width, ch = Callout.DesiredSize.Height;
            const double gap = 16, edge = 8;
            double x, y;
            if (part is not { } p)
            {
                x = (w - cw) / 2;
                y = (h - ch) / 2;
            }
            else if (p.Right + gap + cw <= w - edge)          // right of it (the side menu's parts)
            {
                x = p.Right + gap;
                y = p.Top;
            }
            else if (p.Left - gap - cw >= edge)               // left of it
            {
                x = p.Left - gap - cw;
                y = p.Top;
            }
            else                                              // a big part (the page): inside, bottom right
            {
                x = p.Right - cw - gap;
                y = p.Bottom - ch - gap;
            }
            x = Math.Clamp(x, edge, Math.Max(edge, w - cw - edge));
            y = Math.Clamp(y, edge, Math.Max(edge, h - ch - edge));
            Callout.Margin = new Thickness(x, y, 0, 0);
        }

        /// <summary>Where a part of the window is, in the overlay; null when it isn't on screen.</summary>
        private Rect? Bounds(FrameworkElement? element)
        {
            if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
            try
            {
                return element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                return null;     // not in this window
            }
        }
    }
}
