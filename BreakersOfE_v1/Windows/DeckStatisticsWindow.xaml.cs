using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    // -- Helper row models ─────────────────────────────────────────────────────
    public class EditionRow
    {
        public string SetName { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class ProbabilityCardRow
    {
        public string Name { get; set; } = string.Empty;
        public int DeckQty { get; set; }
        public int DesiredQty { get; set; }
    }

    public class ManaSlotsRow
    {
        public string Name { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string Cost { get; set; } = string.Empty;
        public string Importance { get; set; } = "Average";
    }

    public class AdvancedStatRow
    {
        public string Stat { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string CMC { get; set; } = string.Empty;
        public string ManaProduct { get; set; } = string.Empty;
        public string RuleCount { get; set; } = string.Empty;
    }

    public partial class DeckStatisticsWindow : Window
    {
        private readonly Deck _deck;
        private List<DeckCard> _library = new(); // for Start Hand simulation

        // -- Color palettes ────────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> MtgColors = new()
        {
            { "White",      Color.FromRgb(0xF5, 0xF0, 0xC0) },
            { "Blue",       Color.FromRgb(0x14, 0x6B, 0xD5) },
            { "Black",      Color.FromRgb(0x55, 0x55, 0x55) },
            { "Red",        Color.FromRgb(0xD3, 0x21, 0x2D) },
            { "Green",      Color.FromRgb(0x00, 0x73, 0x3E) },
            { "Multicolor", Color.FromRgb(0xD4, 0xAF, 0x37) },
            { "Colorless",  Color.FromRgb(0xC0, 0xBE, 0xB5) },
        };

        private static readonly Dictionary<string, Color> RarityColors = new()
        {
            { "Common",   Color.FromRgb(0x99, 0x99, 0x99) },
            { "Uncommon", Color.FromRgb(0x70, 0x90, 0xA0) },
            { "Rare",     Color.FromRgb(0xC0, 0x90, 0x30) },
            { "Mythic",   Color.FromRgb(0xE0, 0x6B, 0x1A) },
            { "Special",  Color.FromRgb(0xB8, 0x4A, 0xA4) },
            { "Other",    Color.FromRgb(0x88, 0x88, 0x88) },
        };

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xE0,0x50,0x3A), Color.FromRgb(0x3A,0x7E,0xD8),
            Color.FromRgb(0xE0,0x9A,0x2A), Color.FromRgb(0x3A,0xB8,0x6A),
            Color.FromRgb(0x8A,0x4A,0xC8), Color.FromRgb(0xD8,0x6A,0xB0),
            Color.FromRgb(0x4A,0xC8,0xD8), Color.FromRgb(0xB8,0xB8,0x30),
            Color.FromRgb(0x88,0x44,0x22), Color.FromRgb(0x22,0x88,0x88),
            Color.FromRgb(0xCC,0x44,0x88), Color.FromRgb(0x44,0xAA,0x44),
            Color.FromRgb(0x66,0x66,0xCC), Color.FromRgb(0xAA,0x66,0x00),
            Color.FromRgb(0x00,0xAA,0x66), Color.FromRgb(0xCC,0x66,0x66),
        };

        private static readonly (string Name, char Symbol, Color Fill)[] ManaColorDefs =
        {
            ("White",     'W', Color.FromRgb(0xF5,0xF0,0xC0)),
            ("Blue",      'U', Color.FromRgb(0x14,0x6B,0xD5)),
            ("Black",     'B', Color.FromRgb(0x55,0x55,0x55)),
            ("Red",       'R', Color.FromRgb(0xD3,0x21,0x2D)),
            ("Green",     'G', Color.FromRgb(0x00,0x73,0x3E)),
            ("Colorless", 'C', Color.FromRgb(0xC0,0xBE,0xB5)),
        };

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public DeckStatisticsWindow(Deck deck)
        {
            InitializeComponent();
            _deck = deck;
            Title = $"Statistics — {deck.Name}";

            Loaded += (s, e) => RefreshAllTabs();
            SizeChanged += (s, e) => RefreshCurrentTab();
        }

        private readonly HashSet<int> _initializedTabs = new();

        private void RefreshAllTabs()
        {
            RefreshCurrentTab();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshCurrentTab();
        }

        private void RefreshCurrentTab()
        {
            int idx = MainTabControl.SelectedIndex;

            // Canvas tabs always redraw (they check size and return early if not ready)
            // Use Dispatcher to ensure layout has completed first
            switch (idx)
            {
                case 0:
                case 1:
                case 2:
                case 4:
                case 5:
                case 6:
                    switch (MainTabControl.SelectedIndex)
                    {
                        case 0: DrawColorsTab(); break;
                        case 1: DrawRarityTab(); break;
                        case 2: DrawCardTypeTab(); break;
                        case 4: DrawManaCurveTab(); break;
                        case 5: DrawManaProductTab(); break;
                        case 6: DrawPowerAnalyserTab(); break;
                    }
                    break;

                // Data tabs — populate once on first visit
                case 3: if (_initializedTabs.Add(3)) PopulateEditionsTab(); break;
                case 7: if (_initializedTabs.Add(7)) PopulateProbabilityTab(); break;
                case 8: if (_initializedTabs.Add(8)) PopulateManaSlotsTab(); break;
                case 9: if (_initializedTabs.Add(9)) InitStartHandTab(); break;
                case 10: if (_initializedTabs.Add(10)) PopulateAdvancedTab(); break;
            }
        }

        private void CardTypeDetailCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => DrawCardTypeTab();

        private void PieCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Canvas just got a real size — draw the current tab
            if (e.NewSize.Width > 10 && e.NewSize.Height > 10)
                RefreshCurrentTab();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ================================================================
        // SHARED — DRAW PIE CHART
        // ================================================================
        private static void DrawPieChart(Canvas canvas, WrapPanel legend,
            Dictionary<string, int> data, List<Color> colors)
        {
            if (canvas == null || legend == null) return;
            canvas.Children.Clear();
            legend.Children.Clear();

            if (data == null || data.Count == 0 || data.Values.Sum() == 0) return;

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double cx = w / 2;
            double cy = h / 2 - 10;
            double rx = Math.Min(cx - 40, cy - 30);
            double ry = rx * 0.55;
            double depth = rx * 0.18;

            int total = data.Values.Sum();
            double startAngle = -Math.PI / 2;
            var keys = data.Keys.ToList();
            var values = data.Values.ToList();

            // 3D edges (back to front)
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                double sweep = 2 * Math.PI * values[i] / total;
                double mid = startAngle + sweep / 2;
                if (Math.Sin(mid) > -0.1)
                {
                    double x1 = cx + rx * Math.Cos(startAngle);
                    double y1 = cy + ry * Math.Sin(startAngle);
                    double x2 = cx + rx * Math.Cos(startAngle + sweep);
                    double y2 = cy + ry * Math.Sin(startAngle + sweep);
                    var dark = new SolidColorBrush(DarkenColor(colors[i], 0.5));

                    if (Math.Sin(startAngle) > -0.1)
                        canvas.Children.Add(new Polygon
                        {
                            Fill = dark,
                            Points = new PointCollection
                            {
                                new(x1, y1), new(x1, y1+depth),
                                new(cx, cy+depth), new(cx, cy)
                            }
                        });
                    if (Math.Sin(startAngle + sweep) > -0.1)
                        canvas.Children.Add(new Polygon
                        {
                            Fill = dark,
                            Points = new PointCollection
                            {
                                new(x2, y2), new(x2, y2+depth),
                                new(cx, cy+depth), new(cx, cy)
                            }
                        });
                    canvas.Children.Add(new System.Windows.Shapes.Path
                    {
                        Fill = dark,
                        Data = BuildArcGeometry(cx, cy, rx, ry, startAngle, sweep, depth)
                    });
                }
                startAngle += sweep;
            }

            // Top slices
            startAngle = -Math.PI / 2;
            for (int i = 0; i < keys.Count; i++)
            {
                double sweep = 2 * Math.PI * values[i] / total;
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(colors[i]),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Data = BuildSliceGeometry(cx, cy, rx, ry, startAngle, sweep)
                });

                double mid = startAngle + sweep / 2;
                double pct = Math.Round(100.0 * values[i] / total);
                if (pct >= 2)
                {
                    double lx = cx + (rx * 0.70) * Math.Cos(mid);
                    double ly = cy + (ry * 0.70) * Math.Sin(mid);
                    var lbl = MakeLabel($"{keys[i]} {pct} %", 10);
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lbl, lx - lbl.DesiredSize.Width / 2);
                    Canvas.SetTop(lbl, ly - lbl.DesiredSize.Height / 2);
                    canvas.Children.Add(lbl);
                }
                startAngle += sweep;
            }

            // Legend
            for (int i = 0; i < keys.Count; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 6, 2) };
                item.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(colors[i]),
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                item.Children.Add(new TextBlock
                {
                    Text = $"{values[i]} {keys[i]}",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                legend.Children.Add(item);
            }
        }

        private static Border MakeLabel(string text, double fontSize)
            => new()
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0xFF, 0xFF, 0xE0)),
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(3, 1, 3, 1),
                Child = new TextBlock { Text = text, FontSize = fontSize, Foreground = Brushes.Black }
            };

        private static Geometry BuildSliceGeometry(double cx, double cy,
            double rx, double ry, double startAngle, double sweep)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();
            double endAngle = startAngle + sweep;
            double x1 = cx + rx * Math.Cos(startAngle);
            double y1 = cy + ry * Math.Sin(startAngle);
            double x2 = cx + rx * Math.Cos(endAngle);
            double y2 = cy + ry * Math.Sin(endAngle);
            ctx.BeginFigure(new Point(cx, cy), true, true);
            ctx.LineTo(new Point(x1, y1), true, false);
            ctx.ArcTo(new Point(x2, y2), new Size(rx, ry), 0,
                sweep > Math.PI, SweepDirection.Clockwise, true, false);
            return geo;
        }

        private static Geometry BuildArcGeometry(double cx, double cy,
            double rx, double ry, double startAngle, double sweep, double depth)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();
            double s = Math.Max(startAngle, 0);
            double e = Math.Min(startAngle + sweep, Math.PI);
            if (s >= e) return geo;
            double x1 = cx + rx * Math.Cos(s);
            double y1 = cy + ry * Math.Sin(s);
            double x2 = cx + rx * Math.Cos(e);
            double y2 = cy + ry * Math.Sin(e);
            ctx.BeginFigure(new Point(x1, y1), true, true);
            ctx.ArcTo(new Point(x2, y2), new Size(rx, ry), 0,
                (e - s) > Math.PI, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(x2, y2 + depth), true, false);
            ctx.ArcTo(new Point(x1, y1 + depth), new Size(rx, ry), 0,
                (e - s) > Math.PI, SweepDirection.Counterclockwise, true, false);
            return geo;
        }

        private static Color DarkenColor(Color c, double f) =>
            Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

        // ================================================================
        // TAB 1 — COLORS
        // ================================================================
        private void DrawColorsTab()
        {
            if (ColorsPieCanvas == null || ColorsLegendPanel == null) return;
            // Defer until canvas has been laid out and has real dimensions
            if (ColorsPieCanvas.ActualWidth < 10)
            {
                ColorsPieCanvas.Dispatcher.BeginInvoke(
                    new Action(DrawColorsTab),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            var cards = GetMainCards();
            var data = new Dictionary<string, int>();
            foreach (var card in cards)
            {
                string key = GetColorCategory(card.ColorIdentity);
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }
            var order = new[] { "White", "Blue", "Black", "Red", "Green", "Multicolor", "Colorless" };
            var sorted = order.Where(k => data.ContainsKey(k)).ToDictionary(k => k, k => data[k]);
            var colors = sorted.Keys.Select(k => MtgColors.TryGetValue(k, out var c) ? c
                : Color.FromRgb(0x88, 0x88, 0x88)).ToList();
            DrawPieChart(ColorsPieCanvas, ColorsLegendPanel, sorted, colors);
        }

        private static string GetColorCategory(string ci)
        {
            var chars = ci.Where(c => "WUBRG".Contains(c)).Distinct().ToList();
            if (chars.Count == 0) return "Colorless";
            if (chars.Count > 1) return "Multicolor";
            return chars[0] switch
            {
                'W' => "White",
                'U' => "Blue",
                'B' => "Black",
                'R' => "Red",
                'G' => "Green",
                _ => "Colorless"
            };
        }

        // ================================================================
        // TAB 2 — RARITY
        // ================================================================
        private void DrawRarityTab()
        {
            if (RarityPieCanvas == null || RarityLegendPanel == null) return;
            var data = new Dictionary<string, int>();
            foreach (var card in GetMainCards())
            {
                string key = card.Rarity?.ToLower() switch
                {
                    "common" => "Common",
                    "uncommon" => "Uncommon",
                    "rare" => "Rare",
                    "mythic" => "Mythic",
                    "special" => "Special",
                    _ => "Other"
                };
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }
            var order = new[] { "Common", "Uncommon", "Rare", "Mythic", "Special", "Other" };
            var sorted = order.Where(k => data.ContainsKey(k)).ToDictionary(k => k, k => data[k]);
            var colors = sorted.Keys.Select(k => RarityColors.TryGetValue(k, out var c) ? c
                : Color.FromRgb(0x88, 0x88, 0x88)).ToList();
            DrawPieChart(RarityPieCanvas, RarityLegendPanel, sorted, colors);
        }

        // ================================================================
        // TAB 3 — CARD TYPE
        // ================================================================
        private void DrawCardTypeTab()
        {
            if (CardTypePieCanvas == null || CardTypeLegendPanel == null) return;
            if (CardTypeDetailCombo?.SelectedIndex == null) return;
            var cards = GetMainCards();
            Dictionary<string, int> data;

            switch (CardTypeDetailCombo.SelectedIndex)
            {
                case 0: // General
                    data = new Dictionary<string, int>
                    {
                        ["Land"] = cards.Where(c => c.IsLand).Sum(c => c.TotalQuantity),
                        ["Creature"] = cards.Where(c => c.IsCreature && !c.IsLand).Sum(c => c.TotalQuantity),
                        ["Spell"] = cards.Where(c => !c.IsCreature && !c.IsLand).Sum(c => c.TotalQuantity),
                    };
                    data = data.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        new List<Color>
                        {
                            Color.FromRgb(0xE0,0x7A,0x2A),
                            Color.FromRgb(0xAA,0x22,0x22),
                            Color.FromRgb(0x22,0x55,0xCC)
                        }.Take(data.Count).ToList());
                    break;
                case 1: // Main
                    data = GetMainTypeBreakdown(cards);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        data.Keys.Select((_, i) => Palette[i % Palette.Length]).ToList());
                    break;
                default: // Detailed
                    data = GetDetailedTypeBreakdown(cards);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        data.Keys.Select((_, i) => Palette[i % Palette.Length]).ToList());
                    break;
            }
        }

        private static Dictionary<string, int> GetMainTypeBreakdown(List<DeckCard> cards)
        {
            var data = new Dictionary<string, int>();
            foreach (var c in cards)
            {
                string key = GetMainType(c.TypeLine);
                data.TryGetValue(key, out int ex);
                data[key] = ex + c.TotalQuantity;
            }
            return data.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static string GetMainType(string tl)
        {
            if (string.IsNullOrWhiteSpace(tl)) return "Other";
            var t = tl.ToLower();
            if (t.Contains("basic land")) return "Basic Land";
            if (t.Contains("land")) return "Land";
            if (t.Contains("artifact creature")) return "Artifact Creature";
            if (t.Contains("enchantment creature")) return "Enchantment Creature";
            if (t.Contains("legendary creature")) return "Legendary Creature";
            if (t.Contains("creature")) return "Creature";
            if (t.Contains("planeswalker")) return "Planeswalker";
            if (t.Contains("instant")) return "Instant";
            if (t.Contains("sorcery")) return "Sorcery";
            if (t.Contains("artifact")) return "Artifact";
            if (t.Contains("enchantment")) return "Enchantment";
            if (t.Contains("battle")) return "Battle";
            return "Other";
        }

        private static Dictionary<string, int> GetDetailedTypeBreakdown(List<DeckCard> cards)
        {
            var data = new Dictionary<string, int>();
            foreach (var c in cards)
            {
                string key = string.IsNullOrWhiteSpace(c.TypeLine) ? "Other" : c.TypeLine;
                if (key.Length > 38) key = key[..35] + "...";
                data.TryGetValue(key, out int ex);
                data[key] = ex + c.TotalQuantity;
            }
            return data.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ================================================================
        // TAB 4 — EDITIONS
        // ================================================================
        private void PopulateEditionsTab()
        {
            if (EditionsGrid == null) return;
            EditionsGrid.ItemsSource = GetMainCards()
                .GroupBy(c => new { c.SetCode, c.SetName })
                .Select(g => new EditionRow
                {
                    SetCode = g.Key.SetCode,
                    SetName = string.IsNullOrEmpty(g.Key.SetName) ? g.Key.SetCode : g.Key.SetName,
                    Quantity = g.Sum(c => c.TotalQuantity)
                })
                .OrderBy(r => r.Quantity).ToList();
        }

        // ================================================================
        // TAB 5 — MANA ANALYSER
        // ================================================================
        private void DrawManaCurveTab()
        {
            if (ManaCurveCanvas == null) return;
            var canvas = ManaCurveCanvas;
            canvas.Children.Clear();
            ManaStatsPanel.Children.Clear();
            ManalyserPanel.Children.Clear();
            AvgCostPanel.Children.Clear();
            AvgCostPanel.Children.Add(new TextBlock
            {
                Text = "Average Cost:",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var nonLands = GetMainCards().Where(c => !c.IsLand).ToList();
            var buckets = new int[11];
            foreach (var card in nonLands)
                buckets[Math.Min(10, (int)card.ManaValue)] += card.TotalQuantity;

            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR, chartH = h - padT - padB;
            int maxVal = Math.Max(1, buckets.Max());
            double barGroupW = chartW / 11, barW = barGroupW * 0.62, barOff = barGroupW * 0.19;

            AddCanvasText(canvas, "Mana Curve", padL + chartW / 2, 10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT, X2 = padL, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT + chartH, X2 = padL + chartW, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });

            for (int g = 1; g <= 4; g++)
            {
                double yg = padT + chartH - chartH * g / 4.0;
                canvas.Children.Add(new Line
                {
                    X1 = padL,
                    Y1 = yg,
                    X2 = padL + chartW,
                    Y2 = yg,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    StrokeThickness = 0.5
                });
                AddCanvasText(canvas, ((int)Math.Round(maxVal * g / 4.0)).ToString(), padL - 4, yg, 9, FontWeights.Normal, HorizontalAlignment.Right);
            }

            var linePoints = new PointCollection();
            for (int i = 0; i < 11; i++)
            {
                double x = padL + i * barGroupW + barOff;
                double barH = chartH * buckets[i] / maxVal;
                double y = padT + chartH - barH;

                if (buckets[i] > 0)
                {
                    var rect = new Rectangle
                    {
                        Width = barW,
                        Height = Math.Max(1, barH),
                        Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)),
                        Stroke = new SolidColorBrush(Color.FromRgb(0x99, 0x11, 0x11)),
                        StrokeThickness = 0.5
                    };
                    Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
                    canvas.Children.Add(rect);

                    var lbl = MakeLabel(buckets[i].ToString(), 10);
                    lbl.Measure(new Size(200, 200));
                    Canvas.SetLeft(lbl, x + barW / 2 - lbl.DesiredSize.Width / 2);
                    Canvas.SetTop(lbl, y - lbl.DesiredSize.Height - 1);
                    canvas.Children.Add(lbl);
                }
                else
                    AddCanvasText(canvas, "0", x + barW / 2, padT + chartH - 14, 9, FontWeights.Normal, HorizontalAlignment.Center);

                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(), x + barW / 2, padT + chartH + 4, 10, FontWeights.Normal, HorizontalAlignment.Center);
                linePoints.Add(new Point(x + barW / 2, buckets[i] > 0 ? y : padT + chartH));
            }
            canvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x88, 0xCC)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });

            // Stats + Manalyser
            int total = nonLands.Sum(c => c.TotalQuantity);
            double totalMv = nonLands.Sum(c => c.ManaValue * c.TotalQuantity);
            double avgCmc = total > 0 ? totalMv / total : 0;

            // Per-color mana pip counts
            var pipTotals = new double[ManaColorDefs.Length];
            foreach (var card in nonLands)
                for (int ci = 0; ci < ManaColorDefs.Length; ci++)
                    pipTotals[ci] += CountPips(card.ManaCost, ManaColorDefs[ci].Symbol) * card.TotalQuantity;

            double totalPips = pipTotals.Sum();

            // Manalyser panel — pip icons per color
            for (int ci = 0; ci < ManaColorDefs.Length; ci++)
            {
                int pips = (int)pipTotals[ci];
                if (pips == 0) continue;
                var panel = new WrapPanel { Width = 120 };
                for (int p = 0; p < Math.Min(pips, 60); p++)
                    panel.Children.Add(new Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Margin = new Thickness(1),
                        Fill = new SolidColorBrush(ManaColorDefs[ci].Fill),
                        Stroke = Brushes.Gray,
                        StrokeThickness = 0.5
                    });
                panel.Children.Add(new TextBlock
                {
                    Text = $"({pips})",
                    FontSize = 9,
                    Width = 120,
                    TextAlignment = TextAlignment.Center
                });
                ManalyserPanel.Children.Add(panel);
            }

            // Mana breakdown stats
            for (int ci = 0; ci < ManaColorDefs.Length; ci++)
            {
                if (pipTotals[ci] < 0.01) continue;
                double pct = totalPips > 0 ? pipTotals[ci] / totalPips * 100 : 0;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 1, 8, 1) };
                row.Children.Add(new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(ManaColorDefs[ci].Fill),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{pipTotals[ci]:F2} ({pct:F2}%)",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                ManaStatsPanel.Children.Add(row);
            }
            ManaStatsPanel.Children.Add(new TextBlock
            {
                Text = $"  Total Cards: {total}    Total Mana: {totalMv:F2}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Average cost per color
            for (int ci = 0; ci < ManaColorDefs.Length; ci++)
            {
                if (pipTotals[ci] < 0.01) continue;
                double avg = total > 0 ? pipTotals[ci] / total : 0;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(ManaColorDefs[ci].Fill),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock { Text = $"{avg:F2}", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                AvgCostPanel.Children.Add(row);
            }
        }

        // ================================================================
        // TAB 6 — MANA PRODUCT
        // ================================================================
        private void DrawManaProductTab()
        {
            if (ManaProductCanvas == null) return;
            var canvas = ManaProductCanvas;
            canvas.Children.Clear();
            ManaProductLegendPanel.Children.Clear();

            var cards = GetMainCards();
            var buckets = new double[11, ManaColorDefs.Length];
            foreach (var card in cards)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                for (int ci = 0; ci < ManaColorDefs.Length; ci++)
                    buckets[cmc, ci] += CountPips(card.ManaCost, ManaColorDefs[ci].Symbol) * card.TotalQuantity;
            }

            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR, chartH = h - padT - padB;
            double maxStack = 0;
            for (int i = 0; i < 11; i++) { double s = 0; for (int j = 0; j < ManaColorDefs.Length; j++) s += buckets[i, j]; maxStack = Math.Max(maxStack, s); }
            if (maxStack < 1) maxStack = 1;

            AddCanvasText(canvas, "Mana Product", padL + chartW / 2, 10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT, X2 = padL, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT + chartH, X2 = padL + chartW, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });

            double barGroupW = chartW / 11, barW = barGroupW * 0.55, barOff = barGroupW * 0.225;
            for (int i = 0; i < 11; i++)
            {
                double x = padL + i * barGroupW + barOff, stackBottom = padT + chartH, totalH = 0;
                for (int j = 0; j < ManaColorDefs.Length; j++) totalH += buckets[i, j];

                if (totalH < 0.01)
                    AddCanvasText(canvas, "0", x + barW / 2, padT + chartH - 14, 9, FontWeights.Normal, HorizontalAlignment.Center);
                else
                {
                    for (int j = ManaColorDefs.Length - 1; j >= 0; j--)
                    {
                        if (buckets[i, j] < 0.01) continue;
                        double segH = chartH * buckets[i, j] / maxStack;
                        stackBottom -= segH;
                        var rect = new Rectangle
                        {
                            Width = barW,
                            Height = Math.Max(1, segH),
                            Fill = new SolidColorBrush(ManaColorDefs[j].Fill),
                            Stroke = Brushes.White,
                            StrokeThickness = 0.5
                        };
                        Canvas.SetLeft(rect, x); Canvas.SetTop(rect, stackBottom);
                        canvas.Children.Add(rect);
                        if (segH > 14)
                        {
                            var lbl = MakeLabel(((int)buckets[i, j]).ToString(), 9);
                            lbl.Measure(new Size(200, 200));
                            Canvas.SetLeft(lbl, x + barW / 2 - lbl.DesiredSize.Width / 2);
                            Canvas.SetTop(lbl, stackBottom + segH / 2 - lbl.DesiredSize.Height / 2);
                            canvas.Children.Add(lbl);
                        }
                    }
                    var tLbl = MakeLabel(((int)totalH).ToString(), 10);
                    tLbl.Measure(new Size(200, 200));
                    Canvas.SetLeft(tLbl, x + barW / 2 - tLbl.DesiredSize.Width / 2);
                    Canvas.SetTop(tLbl, stackBottom - tLbl.DesiredSize.Height - 1);
                    canvas.Children.Add(tLbl);
                }
                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(), x + barW / 2, padT + chartH + 4, 10, FontWeights.Normal, HorizontalAlignment.Center);
            }

            ManaProductLegendPanel.Children.Add(new TextBlock { Text = "LEGEND", FontWeight = FontWeights.Bold, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var (name, _, fill) in ManaColorDefs)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(fill), Stroke = Brushes.Gray, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                ManaProductLegendPanel.Children.Add(row);
            }
        }

        // ================================================================
        // TAB 7 — POWER ANALYSER
        // ================================================================
        private void DrawPowerAnalyserTab()
        {
            if (PowerAnalyserCanvas == null) return;
            var canvas = PowerAnalyserCanvas;
            canvas.Children.Clear();
            PowerLegendPanel.Children.Clear();

            var creatures = GetMainCards().Where(c => c.IsCreature && !c.IsLand).ToList();
            var powerBuckets = new double[11];
            var toughnessBuckets = new double[11];
            foreach (var card in creatures)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                if (double.TryParse(card.Power, out double p)) powerBuckets[cmc] += p * card.TotalQuantity;
                if (double.TryParse(card.Toughness, out double t)) toughnessBuckets[cmc] += t * card.TotalQuantity;
            }

            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR, chartH = h - padT - padB;
            double maxVal = Math.Max(1, Math.Max(powerBuckets.Max(), toughnessBuckets.Max()));

            AddCanvasText(canvas, "Power Analyser", padL + chartW / 2, 10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT, X2 = padL, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = padL, Y1 = padT + chartH, X2 = padL + chartW, Y2 = padT + chartH, Stroke = Brushes.Black, StrokeThickness = 1 });
            for (int g = 1; g <= 4; g++)
            {
                double yg = padT + chartH - chartH * g / 4.0;
                canvas.Children.Add(new Line { X1 = padL, Y1 = yg, X2 = padL + chartW, Y2 = yg, Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), StrokeThickness = 0.5 });
            }

            double barGroupW = chartW / 11, halfW = barGroupW * 0.28, gap = 2;
            var powerColor = Color.FromRgb(0xCC, 0x33, 0x33);
            var toughColor = Color.FromRgb(0x33, 0xAA, 0xCC);

            for (int i = 0; i < 11; i++)
            {
                double centerX = padL + i * barGroupW + barGroupW / 2;
                DrawBar(canvas, centerX - halfW - gap / 2, padT + chartH - chartH * powerBuckets[i] / maxVal, halfW, chartH * powerBuckets[i] / maxVal, powerColor, ((int)powerBuckets[i]).ToString());
                DrawBar(canvas, centerX + gap / 2, padT + chartH - chartH * toughnessBuckets[i] / maxVal, halfW, chartH * toughnessBuckets[i] / maxVal, toughColor, ((int)toughnessBuckets[i]).ToString());
                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(), centerX, padT + chartH + 4, 10, FontWeights.Normal, HorizontalAlignment.Center);
            }

            PowerLegendPanel.Children.Add(new TextBlock { Text = "LEGEND", FontWeight = FontWeights.Bold, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var (name, color) in new[] { ("Power", powerColor), ("Toughness", toughColor) })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(new Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(color), Stroke = Brushes.Gray, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                PowerLegendPanel.Children.Add(row);
            }
        }

        private static void DrawBar(Canvas canvas, double x, double y,
            double w, double h, Color color, string label)
        {
            if (h < 0.5)
            {
                var zero = MakeLabel("0", 9);
                zero.Measure(new Size(200, 200));
                Canvas.SetLeft(zero, x + w / 2 - zero.DesiredSize.Width / 2);
                Canvas.SetTop(zero, y - zero.DesiredSize.Height);
                canvas.Children.Add(zero);
                return;
            }
            var rect = new Rectangle { Width = w, Height = Math.Max(1, h), Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(DarkenColor(color, 0.7)), StrokeThickness = 0.5 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
            var lbl = MakeLabel(label, 9);
            lbl.Measure(new Size(200, 200));
            Canvas.SetLeft(lbl, x + w / 2 - lbl.DesiredSize.Width / 2);
            Canvas.SetTop(lbl, y - lbl.DesiredSize.Height - 1);
            canvas.Children.Add(lbl);
        }

        // ================================================================
        // TAB 8 — PROBABILITY
        // ================================================================
        private ObservableCollection<ProbabilityCardRow> _probRows = new();

        private void PopulateProbabilityTab()
        {
            if (ProbabilityGrid == null) return;
            _probRows.Clear();
            foreach (var card in GetMainCards().OrderBy(c => c.Name))
                _probRows.Add(new ProbabilityCardRow
                {
                    Name = card.Name,
                    DeckQty = card.TotalQuantity,
                    DesiredQty = 0
                });
            ProbabilityGrid.ItemsSource = _probRows;
            ProbabilityGrid.CellEditEnding += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(UpdateProbabilityBars),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            UpdateProbabilityBars();
        }

        private void ProbabilityParam_Changed(object sender, RoutedEventArgs e)
            => UpdateProbabilityBars();

        private void TurnBox_TextChanged(object sender, TextChangedEventArgs e)
            => UpdateProbabilityBars();

        private void UpdateProbabilityBars()
        {
            if (ProbBarsPanel == null) return;
            ProbBarsPanel.Children.Clear();

            int deckSize = _deck?.Cards?.Sum(c => c.TotalQuantity) ?? 0;
            if (deckSize == 0) return;

            bool useAnd = RadioAnd?.IsChecked == true;
            bool atLeast = RadioAtLeast?.IsChecked == true;

            if (!int.TryParse(TurnBox?.Text, out int maxTurn) || maxTurn < 0)
                maxTurn = 10;

            var selected = _probRows.Where(r => r.DesiredQty > 0).ToList();

            for (int turn = 0; turn <= maxTurn; turn++)
            {
                int handSize = 7 + turn; // cards seen by turn N (opening hand + draws)
                handSize = Math.Min(handSize, deckSize);

                double prob = selected.Count == 0 ? 0
                    : useAnd
                        ? CalcComboProbability(deckSize, handSize, selected, atLeast)
                        : CalcOrProbability(deckSize, handSize, selected, atLeast);

                // Vertical bar
                var bar = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Width = 36,
                    Margin = new Thickness(1, 0, 1, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // The bar (height 0-60 px proportional to prob)
                double barH = 60 * prob;
                bar.Children.Add(new Border
                {
                    Height = 60,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = Brushes.Transparent,
                    Child = new Rectangle
                    {
                        Width = 30,
                        Height = Math.Max(1, barH),
                        Fill = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC)),
                        VerticalAlignment = VerticalAlignment.Bottom
                    }
                });
                // Pct label
                bar.Children.Add(new TextBlock
                {
                    Text = $"{prob * 100:F0}%",
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    LayoutTransform = new RotateTransform(-90),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                // Turn label
                bar.Children.Add(new TextBlock
                {
                    Text = $"T{turn}",
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                ProbBarsPanel.Children.Add(bar);
            }
        }

        // Hypergeometric: P(drawing at least/exactly desired of each card type)
        private static double CalcComboProbability(int N, int n,
            List<ProbabilityCardRow> cards, bool atLeast)
        {
            // AND: probability of getting all desired quantities simultaneously
            // Use hypergeometric for each independently (simplified — assumes independence)
            double prob = 1.0;
            foreach (var card in cards)
            {
                int K = card.DeckQty; // successes in population
                int k = card.DesiredQty; // desired draws
                double p = atLeast
                    ? 1 - HypergeoCDF(N, K, n, k - 1)
                    : HypergeoExact(N, K, n, k);
                prob *= p;
            }
            return Math.Min(1, Math.Max(0, prob));
        }

        private static double CalcOrProbability(int N, int n,
            List<ProbabilityCardRow> cards, bool atLeast)
        {
            // OR: P(at least one condition met) = 1 - P(none met)
            double noneProb = 1.0;
            foreach (var card in cards)
            {
                int K = card.DeckQty;
                int k = card.DesiredQty;
                double p = atLeast
                    ? 1 - HypergeoCDF(N, K, n, k - 1)
                    : HypergeoExact(N, K, n, k);
                noneProb *= (1 - p);
            }
            return Math.Min(1, Math.Max(0, 1 - noneProb));
        }

        // Hypergeometric CDF: P(X <= x)
        private static double HypergeoCDF(int N, int K, int n, int x)
        {
            double sum = 0;
            for (int i = 0; i <= x; i++)
                sum += HypergeoExact(N, K, n, i);
            return Math.Min(1, sum);
        }

        // Hypergeometric PMF: P(X = k)
        private static double HypergeoExact(int N, int K, int n, int k)
        {
            if (k < 0 || k > K || k > n || (n - k) > (N - K)) return 0;
            return Math.Exp(LogComb(K, k) + LogComb(N - K, n - k) - LogComb(N, n));
        }

        private static double LogComb(int n, int k)
        {
            if (k < 0 || k > n) return double.NegativeInfinity;
            if (k == 0 || k == n) return 0;
            double result = 0;
            for (int i = 0; i < k; i++)
                result += Math.Log(n - i) - Math.Log(i + 1);
            return result;
        }

        private void BtnDescribe_Click(object sender, RoutedEventArgs e)
        {
            var selected = _probRows.Where(r => r.DesiredQty > 0).ToList();
            if (selected.Count == 0) { MessageBox.Show("Set Desired Qty > 0 for at least one card.", "Probability"); return; }

            bool useAnd = RadioAnd?.IsChecked == true;
            bool atLeast = RadioAtLeast?.IsChecked == true;
            int deckSize = _deck?.Cards?.Sum(c => c.TotalQuantity) ?? 0;

            var sb = new StringBuilder();
            sb.AppendLine($"Operator: {(useAnd ? "AND" : "OR")}   Mode: {(atLeast ? "At Least" : "Exactly")}");
            sb.AppendLine($"Deck size: {deckSize}  Cards in combo: {selected.Count}");
            sb.AppendLine();
            foreach (var r in selected)
                sb.AppendLine($"  {r.Name}: {r.DesiredQty} of {r.DeckQty} copies");

            MessageBox.Show(sb.ToString(), "Probability Description",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClearProb_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _probRows) r.DesiredQty = 0;
            ProbabilityGrid.Items.Refresh();
            UpdateProbabilityBars();
        }

        // ================================================================
        // TAB 9 — MANA SLOTS (George Baxter Analysis)
        // ================================================================
        private void PopulateManaSlotsTab()
        {
            if (ManaSlotsGrid == null || ManaSlotsPanel == null || TotalSlotsText == null) return;
            var cards = GetMainCards().Where(c => !c.IsLand).ToList();
            int total = cards.Sum(c => c.TotalQuantity);

            // Count weighted pips per color
            var pipCounts = new double[ManaColorDefs.Length];
            foreach (var card in cards)
                for (int ci = 0; ci < ManaColorDefs.Length; ci++)
                    pipCounts[ci] += CountPips(card.ManaCost, ManaColorDefs[ci].Symbol) * card.TotalQuantity;

            double totalPips = pipCounts.Sum();
            // Total mana slots = deck size * average CMC
            double avgCmc = total > 0 ? cards.Sum(c => c.ManaValue * c.TotalQuantity) / total : 0;
            int lands = GetMainCards().Count(c => c.IsLand);
            double totalSlots = totalPips > 0 ? totalPips : lands;

            // Rows
            ManaSlotsGrid.ItemsSource = GetMainCards()
                .OrderBy(c => c.Name)
                .Select(c => new ManaSlotsRow
                {
                    Name = c.Name,
                    Qty = c.TotalQuantity,
                    Cost = ManaCostToText(c.ManaCost),
                    Importance = "Average"
                }).ToList();

            // Right panel
            ManaSlotsPanel.Children.Clear();
            double runningTotal = 0;
            for (int ci = 0; ci < ManaColorDefs.Length; ci++)
            {
                if (pipCounts[ci] < 0.01) continue;
                double slots = totalPips > 0 ? pipCounts[ci] / totalPips * totalSlots : 0;
                double pct = totalSlots > 0 ? slots / totalSlots * 100 : 0;
                runningTotal += slots;

                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                row.Children.Add(new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(ManaColorDefs[ci].Fill),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{slots:F2} ({pct:F2}%)",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                ManaSlotsPanel.Children.Add(row);
            }
            TotalSlotsText.Text = $"Total slots: {runningTotal:F2}";
        }

        private static string ManaCostToText(string manaCost)
        {
            if (string.IsNullOrEmpty(manaCost)) return string.Empty;
            var sb = new StringBuilder();
            bool inBrace = false;
            foreach (char c in manaCost)
            {
                if (c == '{') { inBrace = true; continue; }
                if (c == '}') { inBrace = false; continue; }
                if (inBrace) sb.Append(c);
            }
            return sb.ToString();
        }

        private void BtnManaSlotsHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "George Baxter Analysis calculates how many mana sources of each color " +
                "you need in your deck to reliably cast your spells.\n\n" +
                "It counts the colored mana pips in each card's casting cost, " +
                "weights them by quantity, and determines the proportion of each " +
                "color needed in your mana base.\n\n" +
                "The 'Needed Mana Slots' shows how many lands of each color type " +
                "you should ideally have.",
                "Mana Slots Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ================================================================
        // TAB 10 — START HAND
        // ================================================================
        private List<DeckCard> _shuffledLibrary = new();
        private List<DeckCard> _hand = new();
        private List<DeckCard> _play = new();
        private List<DeckCard> _discard = new();
        private static readonly Random _rng = new();

        private void InitStartHandTab()
        {
            if (DeckListBox == null || HandListBox == null) return;
            BuildLibrary();
            Shuffle();
            DrawOpeningHand();
        }

        private void BuildLibrary()
        {
            _shuffledLibrary.Clear();
            foreach (var card in _deck?.Cards ?? new())
            {
                // Commander stays in command zone — not in library
                if (card.IsCommander) continue;
                for (int i = 0; i < card.TotalQuantity; i++)
                    _shuffledLibrary.Add(card);
            }
        }

        private void Shuffle()
        {
            for (int i = _shuffledLibrary.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_shuffledLibrary[i], _shuffledLibrary[j]) = (_shuffledLibrary[j], _shuffledLibrary[i]);
            }
        }

        private void DrawOpeningHand()
        {
            if (!int.TryParse(HandSizeBox?.Text, out int size) || size < 0) size = 7;
            _hand.Clear();
            _play.Clear();
            _discard.Clear();

            for (int i = 0; i < size && _shuffledLibrary.Count > 0; i++)
            {
                _hand.Add(_shuffledLibrary[0]);
                _shuffledLibrary.RemoveAt(0);
            }
            RefreshHandUI();
        }

        private void RefreshHandUI()
        {
            DeckListBox.ItemsSource = null;
            HandListBox.ItemsSource = null;
            PlayListBox.ItemsSource = null;
            DiscardListBox.ItemsSource = null;

            DeckListBox.ItemsSource = _shuffledLibrary
                .Select((c, i) => $"{i + 1}  {c.Name} ({(int)c.ManaValue})").ToList();
            HandListBox.ItemsSource = _hand
                .Select((c, i) => $"{i + 1}  {c.Name} ({(int)c.ManaValue})").ToList();
            PlayListBox.ItemsSource = _play
                .Select((c, i) => $"{i + 1}  {c.Name}").ToList();
            DiscardListBox.ItemsSource = _discard
                .Select((c, i) => $"{i + 1}  {c.Name}").ToList();
        }

        private void BtnNewHand_Click(object sender, RoutedEventArgs e)
        {
            BuildLibrary();
            // Return hand/play/discard back to library
            _hand.ForEach(c => _shuffledLibrary.Add(c));
            _play.ForEach(c => _shuffledLibrary.Add(c));
            _discard.ForEach(c => _shuffledLibrary.Add(c));
            Shuffle();
            DrawOpeningHand();
        }

        private void BtnMulligan_Click(object sender, RoutedEventArgs e)
        {
            // Return hand to library, reshuffle, draw one fewer
            _hand.ForEach(c => _shuffledLibrary.Add(c));
            _hand.Clear();
            Shuffle();
            if (!int.TryParse(HandSizeBox?.Text, out int size)) size = 7;
            int newSize = Math.Max(0, size - 1);
            HandSizeBox!.Text = newSize.ToString();
            for (int i = 0; i < newSize && _shuffledLibrary.Count > 0; i++)
            {
                _hand.Add(_shuffledLibrary[0]);
                _shuffledLibrary.RemoveAt(0);
            }
            RefreshHandUI();
        }

        private void BtnDrawCard_Click(object sender, RoutedEventArgs e)
        {
            if (_shuffledLibrary.Count == 0) { MessageBox.Show("Library is empty!", "Draw"); return; }
            _hand.Add(_shuffledLibrary[0]);
            _shuffledLibrary.RemoveAt(0);
            RefreshHandUI();
        }

        private void BtnDrawAll_Click(object sender, RoutedEventArgs e)
        {
            while (_shuffledLibrary.Count > 0)
            {
                _hand.Add(_shuffledLibrary[0]);
                _shuffledLibrary.RemoveAt(0);
            }
            RefreshHandUI();
        }

        private void BtnHandSetup_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Setup: adjust hand size or deck configuration before dealing a new hand.",
                "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MoveCard(ListBox from, List<DeckCard> fromList, List<DeckCard> toList)
        {
            if (from.SelectedIndex < 0 || from.SelectedIndex >= fromList.Count) return;
            var card = fromList[from.SelectedIndex];
            fromList.RemoveAt(from.SelectedIndex);
            toList.Add(card);
            RefreshHandUI();
        }

        private void BtnDeckToHand_Click(object sender, RoutedEventArgs e)
            => MoveCard(DeckListBox, _shuffledLibrary, _hand);
        private void BtnHandToDeck_Click(object sender, RoutedEventArgs e)
            => MoveCard(HandListBox, _hand, _shuffledLibrary);
        private void BtnHandToPlay_Click(object sender, RoutedEventArgs e)
            => MoveCard(HandListBox, _hand, _play);
        private void BtnPlayToHand_Click(object sender, RoutedEventArgs e)
            => MoveCard(PlayListBox, _play, _hand);
        private void BtnHandToDiscard_Click(object sender, RoutedEventArgs e)
            => MoveCard(HandListBox, _hand, _discard);
        private void BtnDiscardToHand_Click(object sender, RoutedEventArgs e)
            => MoveCard(DiscardListBox, _discard, _hand);

        // ================================================================
        // TAB 11 — ADVANCED STATISTICS
        // ================================================================
        private void PopulateAdvancedTab()
        {
            if (AdvancedGrid == null) return;
            var cards = GetMainCards();

            var powers = cards.SelectMany(c => Enumerable.Repeat(c.Power, c.TotalQuantity))
                                   .Where(v => double.TryParse(v, out _))
                                   .Select(v => double.Parse(v)).ToList();
            var toughnesses = cards.SelectMany(c => Enumerable.Repeat(c.Toughness, c.TotalQuantity))
                                   .Where(v => double.TryParse(v, out _))
                                   .Select(v => double.Parse(v)).ToList();
            var cmcs = cards.SelectMany(c => Enumerable.Repeat(c.ManaValue, c.TotalQuantity)).ToList();
            var manaProd = cards.Where(c => !c.IsLand)
                                   .SelectMany(c => Enumerable.Repeat(
                                       (double)CountColoredPips(c.ManaCost), c.TotalQuantity)).ToList();
            var ruleWords = cards.SelectMany(c =>
                                   Enumerable.Repeat(
                                       (double)(c.OracleText?.Split('\n').Length ?? 0),
                                       c.TotalQuantity)).ToList();

            var rows = new List<AdvancedStatRow>();
            void Add(string name, Func<List<double>, string> fn)
                => rows.Add(new AdvancedStatRow
                {
                    Stat = name,
                    Power = powers.Count > 0 ? fn(powers) : "—",
                    Toughness = toughnesses.Count > 0 ? fn(toughnesses) : "—",
                    CMC = fn(cmcs),
                    ManaProduct = manaProd.Count > 0 ? fn(manaProd) : "—",
                    RuleCount = ruleWords.Count > 0 ? fn(ruleWords) : "—"
                });

            int N = cards.Sum(c => c.TotalQuantity);
            Add("Count (N)", d => d.Count.ToString());
            Add("Missing (N*)", d => (N - d.Count).ToString());
            Add("Arithmetic Mean", d => Mean(d).F3());
            Add("Trimmed Mean (5%)", d => TrimmedMean(d, 0.05).F3());
            Add("Harmonic Mean", d => HarmonicMean(d).F3());
            Add("Geometric Mean", d => GeometricMean(d).F3());
            Add("MSSD", d => MSSD(d).F3());
            Add("Mode", d => Mode(d).F3());
            Add("Mode Frequency", d => ModeFreq(d).ToString());
            Add("Standard Deviation", d => StdDev(d, false).F3());
            Add("Population Std Dev", d => StdDev(d, true).F3());
            Add("Sum", d => d.Sum().F3());
            Add("Sum of Squares", d => d.Sum(x => x * x).F3());
            Add("Variance", d => Variance(d, false).F3());
            Add("Total Variance", d => (Variance(d, false) * d.Count).F3());
            Add("Population Variance", d => Variance(d, true).F3());
            Add("Minimum", d => d.Min().F3());
            Add("Quartile 1 (Q1)", d => Quartile(d, 0.25).F3());
            Add("Median", d => Quartile(d, 0.50).F3());
            Add("Quartile 3 (Q3)", d => Quartile(d, 0.75).F3());
            Add("Maximum", d => d.Max().F3());
            Add("Range", d => (d.Max() - d.Min()).F3());
            Add("Inter-quartile Range (IQR)", d => (Quartile(d, 0.75) - Quartile(d, 0.25)).F3());
            Add("Mid-Range", d => ((d.Max() + d.Min()) / 2).F3());
            Add("Skewness", d => Skewness(d).F3());
            Add("Kurtosis", d => Kurtosis(d).F3());

            AdvancedGrid.ItemsSource = rows;
            AdvancedGrid.AlternationCount = 2;
        }

        // Stat helpers
        private static double Mean(List<double> d) => d.Average();
        private static double Variance(List<double> d, bool pop)
        {
            double m = Mean(d);
            double ss = d.Sum(x => (x - m) * (x - m));
            return ss / (pop ? d.Count : Math.Max(1, d.Count - 1));
        }
        private static double StdDev(List<double> d, bool pop) => Math.Sqrt(Variance(d, pop));
        private static double TrimmedMean(List<double> d, double trim)
        {
            var s = d.OrderBy(x => x).ToList();
            int cut = (int)(s.Count * trim);
            var trimmed = s.Skip(cut).Take(s.Count - 2 * cut).ToList();
            return trimmed.Count > 0 ? trimmed.Average() : 0;
        }
        private static double HarmonicMean(List<double> d)
        {
            var pos = d.Where(x => x > 0).ToList();
            return pos.Count > 0 ? pos.Count / pos.Sum(x => 1.0 / x) : 0;
        }
        private static double GeometricMean(List<double> d)
        {
            var pos = d.Where(x => x > 0).ToList();
            return pos.Count > 0 ? Math.Exp(pos.Sum(x => Math.Log(x)) / pos.Count) : 0;
        }
        private static double MSSD(List<double> d)
        {
            if (d.Count < 2) return 0;
            var s = d.OrderBy(x => x).ToList();
            return s.Zip(s.Skip(1), (a, b) => (b - a) * (b - a)).Average();
        }
        private static double Mode(List<double> d)
            => d.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        private static int ModeFreq(List<double> d)
            => d.GroupBy(x => x).Max(g => g.Count());
        private static double Quartile(List<double> d, double p)
        {
            var s = d.OrderBy(x => x).ToList();
            double idx = (s.Count - 1) * p;
            int lo = (int)idx, hi = Math.Min(lo + 1, s.Count - 1);
            return s[lo] + (idx - lo) * (s[hi] - s[lo]);
        }
        private static double Skewness(List<double> d)
        {
            if (d.Count < 3) return 0;
            double m = Mean(d), sd = StdDev(d, false);
            if (sd == 0) return 0;
            return d.Sum(x => Math.Pow((x - m) / sd, 3)) * d.Count / ((d.Count - 1.0) * (d.Count - 2.0));
        }
        private static double Kurtosis(List<double> d)
        {
            if (d.Count < 4) return 0;
            double m = Mean(d), sd = StdDev(d, false);
            if (sd == 0) return 0;
            int n = d.Count;
            double kurt = d.Sum(x => Math.Pow((x - m) / sd, 4));
            return n * (n + 1) / ((double)(n - 1) * (n - 2) * (n - 3)) * kurt
                 - 3.0 * (n - 1) * (n - 1) / ((double)(n - 2) * (n - 3));
        }

        private static int CountColoredPips(string manaCost)
        {
            int count = 0;
            bool inBrace = false;
            foreach (char c in manaCost ?? string.Empty)
            {
                if (c == '{') { inBrace = true; continue; }
                if (c == '}') { inBrace = false; continue; }
                if (inBrace && "WUBRG".Contains(c)) count++;
            }
            return count;
        }

        // ================================================================
        // SHARED HELPERS
        // ================================================================
        private List<DeckCard> GetMainCards() =>
            _deck?.Cards?
                .Where(c => c != null && c.Category != DeckCardCategory.Sideboard)
                .ToList() ?? new List<DeckCard>();

        private static int CountPips(string manaCost, char symbol)
        {
            int count = 0; bool inBrace = false;
            foreach (char c in manaCost ?? string.Empty)
            {
                if (c == '{') { inBrace = true; continue; }
                if (c == '}') { inBrace = false; continue; }
                if (inBrace && c == symbol) count++;
            }
            return count;
        }

        private static void AddCanvasText(Canvas canvas, string text,
            double cx, double cy, double fontSize, FontWeight weight,
            HorizontalAlignment align)
        {
            var tb = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight, Foreground = Brushes.Black };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = align == HorizontalAlignment.Center ? cx - tb.DesiredSize.Width / 2
                     : align == HorizontalAlignment.Right ? cx - tb.DesiredSize.Width
                     : cx;
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, cy);
            canvas.Children.Add(tb);
        }
    }

    // Extension for formatting
    internal static class DoubleExt
    {
        public static string F3(this double d) =>
            double.IsNaN(d) || double.IsInfinity(d) ? "—" : d.ToString("F3");
    }
}