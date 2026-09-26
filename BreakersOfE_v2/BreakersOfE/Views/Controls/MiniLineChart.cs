using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreakersOfE.Views.Controls
{
    /// <summary>One line on a <see cref="MiniLineChart"/>.</summary>
    public sealed class ChartSeries
    {
        public string Name { get; init; } = "";
        public Brush Stroke { get; init; } = Brushes.DodgerBlue;
        /// <summary>One value per x label; null = no point there.</summary>
        public IReadOnlyList<decimal?> Values { get; init; } = Array.Empty<decimal?>();
    }

    /// <summary>
    /// Small read-only line chart (price history): dollar axis on the left,
    /// date labels along the bottom, a dot per point with a tooltip. Redraws
    /// itself when resized. Set data with <see cref="SetData"/>.
    /// </summary>
    public class MiniLineChart : Canvas
    {
        private IReadOnlyList<string> _labels = Array.Empty<string>();
        private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();

        private const double LeftPad = 58;     // y-axis labels
        private const double BottomPad = 20;   // x-axis labels
        private const double TopPad = 8;
        private const double RightPad = 12;

        public MiniLineChart()
        {
            ClipToBounds = true;
            SizeChanged += (_, _) => Redraw();
        }

        public void SetData(IReadOnlyList<string> xLabels, IReadOnlyList<ChartSeries> series)
        {
            _labels = xLabels;
            _series = series;
            Redraw();
        }

        private Brush TextBrush =>
            TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

        private void Redraw()
        {
            Children.Clear();
            double w = ActualWidth, h = ActualHeight;
            int n = _labels.Count;
            if (w < LeftPad + RightPad + 20 || h < TopPad + BottomPad + 20 || n == 0) return;

            var all = _series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (all.Count == 0) return;

            decimal min = all.Min(), max = all.Max();
            if (max == min)
            {
                decimal pad = Math.Max(min * 0.1m, 0.10m);
                min -= pad; max += pad;
            }
            else
            {
                decimal pad = (max - min) * 0.1m;
                min -= pad; max += pad;
            }
            if (min < 0) min = 0;

            double plotW = w - LeftPad - RightPad;
            double plotH = h - TopPad - BottomPad;
            double X(int i) => LeftPad + (n == 1 ? plotW / 2 : plotW * i / (n - 1));
            double Y(decimal v) => TopPad + plotH * (double)((max - v) / (max - min));

            var grid = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));

            // Horizontal grid lines + dollar labels (bottom, middle, top)
            for (int k = 0; k <= 2; k++)
            {
                decimal v = min + (max - min) * k / 2;
                double y = Y(v);
                Children.Add(new Line { X1 = LeftPad, X2 = w - RightPad, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
                var lbl = new TextBlock { Text = Money(v), FontSize = 10, Foreground = TextBrush, Width = LeftPad - 6, TextAlignment = TextAlignment.Right };
                SetLeft(lbl, 0);
                SetTop(lbl, y - 7);
                Children.Add(lbl);
            }

            // Date labels (thinned when crowded)
            int step = Math.Max(1, (int)Math.Ceiling(n * 62.0 / plotW));
            for (int i = 0; i < n; i++)
            {
                if (i % step != 0 && i != n - 1) continue;
                var lbl = new TextBlock { Text = _labels[i], FontSize = 10, Foreground = TextBrush, Width = 60, TextAlignment = TextAlignment.Center };
                SetLeft(lbl, X(i) - 30);
                SetTop(lbl, h - BottomPad + 3);
                Children.Add(lbl);
            }

            // Lines + dots
            foreach (var s in _series)
            {
                var line = new Polyline { Stroke = s.Stroke, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                for (int i = 0; i < n && i < s.Values.Count; i++)
                {
                    if (s.Values[i] is not decimal v) continue;
                    line.Points.Add(new Point(X(i), Y(v)));
                }
                if (line.Points.Count > 1) Children.Add(line);

                for (int i = 0; i < n && i < s.Values.Count; i++)
                {
                    if (s.Values[i] is not decimal v) continue;
                    var dot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = s.Stroke,
                        ToolTip = string.IsNullOrEmpty(s.Name) ? $"{_labels[i]}: {Money(v)}" : $"{s.Name} · {_labels[i]}: {Money(v)}",
                    };
                    SetLeft(dot, X(i) - 4);
                    SetTop(dot, Y(v) - 4);
                    Children.Add(dot);
                }
            }
        }

        private static string Money(decimal v) => v >= 1000m ? $"${v:N0}" : $"${v:N2}";
    }
}