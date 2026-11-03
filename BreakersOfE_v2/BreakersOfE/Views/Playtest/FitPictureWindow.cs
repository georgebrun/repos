using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakersOfE.Playtest;
using BreakersOfE.Services;

namespace BreakersOfE.Views.Playtest
{
    /// <summary>
    /// Fit…: the frame stays the same (shaped like the battlefield, or a card
    /// for sleeves) and the picture moves underneath it. Drag the picture,
    /// scroll (or the slider) to make it bigger or smaller: smaller than the
    /// frame leaves the table color around it. The picture past the frame
    /// shows dimmed, for a sense of where it is. Returns the framing, or null
    /// when cancelled.
    /// </summary>
    internal sealed class FitPictureWindow : Window
    {
        private const double MaxFrameW = 640, MaxFrameH = 400, Pad = 70;

        private readonly BitmapSource _src;
        private readonly Framing _f;
        private readonly double _fw, _fh;
        private readonly Canvas _canvas = new() { ClipToBounds = true, Cursor = Cursors.SizeAll };
        private readonly Image _img = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Slider _zoom = new() { Width = 260, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _zoomText = new() { Width = 54, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        private Point? _dragFrom;
        private bool _ok;
        private bool _setting;

        public static Framing? Ask(Window owner, string path, double aspect, Framing start, string title, Brush gap)
        {
            var src = ImageCacheService.LoadBitmap(path, 1600);
            if (src == null) return null;
            var w = new FitPictureWindow(owner, src, aspect, start, title, gap);
            w.ShowDialog();
            return w._ok ? w._f : null;
        }

        private FitPictureWindow(Window owner, BitmapSource src, double aspect, Framing start, string title, Brush gap)
        {
            _src = src;
            _f = start;
            Owner = owner;
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

            // The frame: as big as fits, in its own shape. Around it, room to see the picture past its edges.
            aspect = Math.Clamp(aspect, 0.1, 10);
            _fw = MaxFrameW;
            _fh = _fw / aspect;
            if (_fh > MaxFrameH) { _fh = MaxFrameH; _fw = _fh * aspect; }
            _canvas.Width = _fw + 2 * Pad;
            _canvas.Height = _fh + 2 * Pad;
            _canvas.Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
            var frameRect = new Rect(Pad, Pad, _fw, _fh);

            var under = new System.Windows.Shapes.Rectangle { Width = _fw, Height = _fh, Fill = gap, IsHitTestVisible = false };
            Canvas.SetLeft(under, Pad);
            Canvas.SetTop(under, Pad);
            _canvas.Children.Add(under);                                     // where the picture doesn't reach
            _img.Source = src;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            _canvas.Children.Add(_img);
            _canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), IsHitTestVisible = false,
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, _canvas.Width, _canvas.Height)), new RectangleGeometry(frameRect)),
            });
            var border = new System.Windows.Shapes.Rectangle { Width = _fw, Height = _fh, Stroke = Brushes.Gold, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(border, Pad);
            Canvas.SetTop(border, Pad);
            _canvas.Children.Add(border);

            _canvas.MouseLeftButtonDown += (_, e) => { _dragFrom = e.GetPosition(_canvas); _canvas.CaptureMouse(); };
            _canvas.MouseLeftButtonUp += (_, _) => { _dragFrom = null; _canvas.ReleaseMouseCapture(); };
            _canvas.MouseMove += (_, e) =>
            {
                if (_dragFrom == null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(_canvas);
                var d = p - _dragFrom.Value;
                _dragFrom = p;
                _f.Move(d.X, d.Y, src.PixelWidth, src.PixelHeight, _fw, _fh);
                Draw();
            };
            _canvas.MouseWheel += (_, e) =>
            {
                SetZoom(_f.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
                e.Handled = true;
            };

            // The slider runs on a log scale: 10% … 100% … 600% feels even.
            _zoom.Minimum = Math.Log(Framing.MinZoom);
            _zoom.Maximum = Math.Log(Framing.MaxZoom);
            _zoom.ValueChanged += (_, e) => { if (!_setting) SetZoom(Math.Exp(e.NewValue)); };

            var hint = new TextBlock
            {
                Text = "The gold frame is your " + (title.Contains("sleeve", StringComparison.OrdinalIgnoreCase) ? "card back" : "battlefield") +
                       ". Drag the picture to move it; scroll (or the slider) to make it bigger or smaller. Smaller than the frame leaves the table around it.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = _canvas.Width, Margin = new Thickness(0, 0, 0, 8), FontSize = 12,
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            zoomRow.Children.Add(new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            zoomRow.Children.Add(_zoom);
            zoomRow.Children.Add(_zoomText);
            zoomRow.Children.Add(Btn("Fill", () => { _f.CenterX = 0.5; _f.CenterY = 0.5; SetZoom(1); },
                "Fill the frame, centered (some of the picture may be cut off)"));
            zoomRow.Children.Add(Btn("Whole Picture", () => { _f.CenterX = 0.5; _f.CenterY = 0.5; SetZoom(WholeZoom()); },
                "Show the whole picture, centered (the table shows around it)"));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var use = new Button { Content = "Use This", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            use.Click += (_, _) => { _ok = true; Close(); };
            var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), IsCancel = true };
            buttons.Children.Add(use);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(hint);
            root.Children.Add(new Border { Child = _canvas, HorizontalAlignment = HorizontalAlignment.Center });
            root.Children.Add(zoomRow);
            root.Children.Add(buttons);
            Content = root;
            SetZoom(_f.Zoom);
        }

        private static Button Btn(string text, Action act, string tip)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(10, 0, 0, 0), ToolTip = tip };
            b.Click += (_, _) => act();
            return b;
        }

        /// <summary>The size at which the whole picture just fits inside the frame.</summary>
        private double WholeZoom()
        {
            double fill = Math.Max(_fw / _src.PixelWidth, _fh / _src.PixelHeight);
            double fit = Math.Min(_fw / _src.PixelWidth, _fh / _src.PixelHeight);
            return Math.Clamp(fit / fill, Framing.MinZoom, 1);
        }

        private void SetZoom(double z)
        {
            _f.Zoom = Math.Clamp(z, Framing.MinZoom, Framing.MaxZoom);
            _setting = true;
            _zoom.Value = Math.Log(_f.Zoom);
            _setting = false;
            _f.Move(0, 0, _src.PixelWidth, _src.PixelHeight, _fw, _fh);    // keep a piece of it in the frame
            Draw();
        }

        /// <summary>The picture where the framing puts it.</summary>
        private void Draw()
        {
            var (l, t, w, h) = _f.Place(_src.PixelWidth, _src.PixelHeight, _fw, _fh);
            _img.Width = w;
            _img.Height = h;
            Canvas.SetLeft(_img, Pad + l);
            Canvas.SetTop(_img, Pad + t);
            _zoomText.Text = $"{_f.Zoom * 100:0}%";
        }
    }
}
