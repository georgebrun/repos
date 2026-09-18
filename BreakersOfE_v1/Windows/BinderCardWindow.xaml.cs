using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    public partial class BinderCardWindow : Window
    {
        public BinderCardWindow(BinderPocket pocket)
        {
            InitializeComponent();

            Title = pocket.Name;

            // ── Card image ───────────────────────────────────────────────────
            const double W = 213;
            const double H = 297;

            UIElement cardImg;
            if (!string.IsNullOrEmpty(pocket.LocalImagePath) &&
                File.Exists(pocket.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pocket.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)W;
                    bmp.EndInit();
                    bmp.Freeze();
                    cardImg = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill,
                        Width = W,
                        Height = H
                    };
                }
                catch { cardImg = MakeFallback(pocket.Name, W, H); }
            }
            else
                cardImg = MakeFallback(pocket.Name, W, H);

            Canvas.SetLeft(cardImg, 0);
            Canvas.SetTop(cardImg, 0);
            CardCanvas.Children.Add(cardImg);

            // ── Finish pill — top right ────────────────────────────────────
            if (pocket.IsFoil)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(210, 0xCC, 0x99, 0x00)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    Child = new TextBlock
                    {
                        Text = "F",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    },
                    IsHitTestVisible = false
                };
                Canvas.SetRight(pill, 4);
                Canvas.SetTop(pill, 4);
                CardCanvas.Children.Add(pill);
            }

            // ── Qty badge — bottom left ──────────────────────────────────────
            if (pocket.Quantity > 0)
            {
                var qtyBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(210, 0x1A, 0x1A, 0x1A)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 4, 10, 4),
                    Child = new TextBlock
                    {
                        Text = $"×{pocket.Quantity}",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    }
                };
                Canvas.SetLeft(qtyBadge, 6);
                Canvas.SetBottom(qtyBadge, 6);
                CardCanvas.Children.Add(qtyBadge);
            }

            // ── Text fields ──────────────────────────────────────────────────
            NameText.Text = pocket.Name;

            string foilTag = pocket.IsFoil ? "  ✦ Foil" : string.Empty;
            SetText.Text = pocket.SetCode + foilTag;

            if (!string.IsNullOrEmpty(pocket.Condition))
            {
                ConditionText.Text = pocket.Condition;
                ConditionText.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(pocket.PriceDisplay))
            {
                PriceText.Text = pocket.PriceDisplay;
                PriceText.Visibility = Visibility.Visible;
            }

            // Close on click or Escape
            MouseLeftButtonDown += (s, e) => Close();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Return) Close();
            };
        }

        private static Border MakeFallback(string name, double w, double h) =>
            new Border
            {
                Width = w,
                Height = h,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x1A, 0x2A, 0x5E),
                    Color.FromRgb(0x0E, 0x18, 0x3A),
                    new Point(0, 0), new Point(1, 1)),
                CornerRadius = new CornerRadius(6),
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8)
                }
            };
    }
}