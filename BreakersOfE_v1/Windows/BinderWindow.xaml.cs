using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    // ── Pocket data — one per DB entry ──────────────────────────────────────
    public class BinderPocket
    {
        public int EntryId { get; set; }
        public bool IsWantList { get; set; }
        public int PoolId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public int Quantity { get; set; }
        public string LocalImagePath { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public decimal? Price { get; set; }   // AskingPrice / OfferPrice
        public decimal? MarketPrice { get; set; }
        public string Condition { get; set; } = string.Empty;

        public string PriceDisplay =>
            Price.HasValue ? $"${Price.Value:F2}"
            : MarketPrice.HasValue ? $"~${MarketPrice.Value:F2}"
            : string.Empty;
    }

    public partial class BinderWindow : Window
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const int CardsPerPage = 9;   // 3×3

        // ── State ────────────────────────────────────────────────────────────
        private bool _showingHave = true;
        private int _currentPage = 0;

        private List<BinderPocket> _haveCards = new();
        private List<BinderPocket> _wantCards = new();

        // ── Constructor ──────────────────────────────────────────────────────
        public BinderWindow()
        {
            InitializeComponent();
            LoadData();
            RenderPage();
        }

        // ── Data loading ─────────────────────────────────────────────────────
        private void LoadData()
        {
            _haveCards = LoadHaveCards();
            _wantCards = LoadWantCards();
        }

        private static List<BinderPocket> LoadHaveCards()
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.TradeBinderEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return new();

            var pockets = new List<BinderPocket>();
            foreach (var e in entries)
            {
                pockets.Add(new BinderPocket
                {
                    EntryId = e.TradeBinderEntryId,
                    IsWantList = false,
                    PoolId = e.PoolId,
                    Name = e.Name,
                    SetCode = e.SetCode,
                    IsFoil = e.IsFoil,
                    Quantity = e.Quantity,
                    LocalImagePath = e.LocalImagePath,
                    ImageNormalUrl = e.ImageNormalUrl,
                    Price = e.AskingPrice,
                    MarketPrice = e.IsFoil ? e.PriceUsdFoil : e.PriceUsd,
                    Condition = e.Condition
                });
            }
            // Sort: same card name groups together — foil after non-foil
            pockets.Sort((a, b) =>
            {
                int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (n != 0) return n;
                return a.IsFoil.CompareTo(b.IsFoil);
            });
            return pockets;
        }

        private static List<BinderPocket> LoadWantCards()
        {
            using var cdb = new CollectionDbContext();
            var entries = cdb.WantListEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return new();

            var pockets = new List<BinderPocket>();
            foreach (var e in entries)
            {
                pockets.Add(new BinderPocket
                {
                    EntryId = e.WantListEntryId,
                    IsWantList = true,
                    PoolId = e.PoolId,
                    Name = e.Name,
                    SetCode = e.SetCode,
                    IsFoil = e.IsFoil,
                    Quantity = e.Quantity,
                    LocalImagePath = e.LocalImagePath,
                    ImageNormalUrl = e.ImageNormalUrl,
                    Price = e.OfferPrice,
                    MarketPrice = e.IsFoil ? e.PriceUsdFoil : e.PriceUsd,
                    Condition = string.Empty
                });
            }
            pockets.Sort((a, b) =>
            {
                int n = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (n != 0) return n;
                return a.IsFoil.CompareTo(b.IsFoil);
            });
            return pockets;
        }

        // ── Rendering ────────────────────────────────────────────────────────
        private void RenderPage()
        {
            var cards = _showingHave ? _haveCards : _wantCards;

            int total = cards.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)CardsPerPage));
            _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

            PageIndicatorText.Text = $"Page {_currentPage + 1} of {totalPages}";
            CardCountText.Text = $"{total} card{(total == 1 ? "" : "s")}";

            BtnFirst.IsEnabled = _currentPage > 0;
            BtnPrev.IsEnabled = _currentPage > 0;
            BtnNext.IsEnabled = _currentPage < totalPages - 1;

            int start = _currentPage * CardsPerPage;
            var pageCards = cards.Skip(start).Take(CardsPerPage).ToList();

            CardGrid.Children.Clear();

            for (int i = 0; i < CardsPerPage; i++)
            {
                if (i < pageCards.Count)
                    CardGrid.Children.Add(MakePocket(pageCards[i]));
                else
                    CardGrid.Children.Add(MakeEmptyPocket());
            }
        }

        // ── Pocket builder ───────────────────────────────────────────────────
        private UIElement MakePocket(BinderPocket pocket)
        {
            var outer = new Border
            {
                Margin = new Thickness(6),
                Cursor = Cursors.Hand,
                ToolTip = $"{pocket.Name}{(pocket.IsFoil ? " ✦ Foil" : "")}" +
                          $"\n{pocket.SetCode}" +
                          $"{(!string.IsNullOrEmpty(pocket.Condition) ? "\n" + pocket.Condition : "")}" +
                          $"\n{pocket.PriceDisplay}"
            };

            var stack = new StackPanel();
            outer.Child = stack;

            // ── Sleeve (card image area) ─────────────────────────────────────
            var sleeve = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0xC8, 0xBC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xA0, 0x90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(3)
            };
            stack.Children.Add(sleeve);

            // Canvas for image + badges
            var canvas = new Canvas { Width = 94, Height = 131 };
            sleeve.Child = canvas;

            // Card image
            UIElement cardImg = MakeCardImage(
                pocket.LocalImagePath, pocket.Name, pocket.ImageNormalUrl);
            Canvas.SetLeft(cardImg, 0);
            Canvas.SetTop(cardImg, 0);
            canvas.Children.Add(cardImg);

            // ── Foil badge — rainbow triangle top-right ──────────────────────
            if (pocket.IsFoil)
            {
                var foilBadge = MakeFoilTriangle();
                Canvas.SetRight(foilBadge, 0);
                Canvas.SetTop(foilBadge, 0);
                canvas.Children.Add(foilBadge);
            }

            // ── Qty badge — dark pill bottom-left ───────────────────────────
            if (pocket.Quantity > 0)
            {
                var qtyBadge = MakeQtyBadge(pocket.Quantity);
                Canvas.SetLeft(qtyBadge, 4);
                Canvas.SetBottom(qtyBadge, 4);
                canvas.Children.Add(qtyBadge);
            }

            // ── Card name ───────────────────────────────────────────────────
            stack.Children.Add(new TextBlock
            {
                Text = pocket.Name,
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x18, 0x10)),
                Margin = new Thickness(0, 3, 0, 0),
                MaxWidth = 94
            });

            // ── Price ────────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(pocket.PriceDisplay))
                stack.Children.Add(new TextBlock
                {
                    Text = pocket.PriceDisplay,
                    FontSize = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x44)),
                    Margin = new Thickness(0, 1, 0, 0)
                });

            // ── Mouse events ─────────────────────────────────────────────────
            outer.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2) ShowEnlarged(pocket);
            };
            outer.MouseRightButtonDown += (s, e) =>
            {
                ShowPocketContextMenu(pocket, outer);
                e.Handled = true;
            };

            return outer;
        }

        // ── Foil rainbow triangle badge ──────────────────────────────────────
        private static UIElement MakeFoilTriangle()
        {
            const double size = 22;
            var poly = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, 0),
                    new Point(size, 0),
                    new Point(size, size)
                },
                Fill = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0xFF, 0x00, 0x80), 0.0),  // pink
                        new GradientStop(Color.FromRgb(0xFF, 0xA5, 0x00), 0.2),  // orange
                        new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.4),  // yellow
                        new GradientStop(Color.FromRgb(0x00, 0xDD, 0x44), 0.6),  // green
                        new GradientStop(Color.FromRgb(0x00, 0xAA, 0xFF), 0.8),  // blue
                        new GradientStop(Color.FromRgb(0xAA, 0x00, 0xFF), 1.0),  // purple
                    },
                    new Point(0, 0), new Point(1, 1)),
                Opacity = 0.85
            };
            return poly;
        }

        // ── Qty pill badge ───────────────────────────────────────────────────
        private static UIElement MakeQtyBadge(int qty)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = $"×{qty}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                }
            };
        }

        // ── Card image ───────────────────────────────────────────────────────
        private static UIElement MakeCardImage(
            string localPath, string name, string imageUrl = "")
        {
            // 1. Use the explicit local path if it exists
            string? resolved = (!string.IsNullOrEmpty(localPath)
                && File.Exists(localPath)) ? localPath : null;

            // 2. Otherwise check the main app's CardImages cache folder
            //    (same naming convention used when adding to the collection)
            if (resolved == null && !string.IsNullOrEmpty(name))
            {
                string safeName = string.Concat(
                    name.Split(System.IO.Path.GetInvalidFileNameChars()));
                string cached = System.IO.Path.Combine(
                    Services.AppFolderService.CardImagesFolder, $"{safeName}.jpg");
                if (File.Exists(cached))
                    resolved = cached;
            }

            // 3. If still nothing but we have a Scryfall URL, download and cache it
            if (resolved == null && !string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    string safeName = string.Concat(
                        name.Split(System.IO.Path.GetInvalidFileNameChars()));
                    string cached = System.IO.Path.Combine(
                        Services.AppFolderService.CardImagesFolder, $"{safeName}.jpg");
                    if (!File.Exists(cached))
                    {
                        using var http = new System.Net.Http.HttpClient();
                        http.Timeout = TimeSpan.FromSeconds(10);
                        var bytes = http.GetByteArrayAsync(imageUrl)
                            .GetAwaiter().GetResult();
                        File.WriteAllBytes(cached, bytes);
                    }
                    if (File.Exists(cached))
                        resolved = cached;
                }
                catch { /* download failed — fall through to placeholder */ }
            }

            if (resolved != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(resolved, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 94;
                    bmp.EndInit();
                    bmp.Freeze();
                    return new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill,
                        Width = 94,
                        Height = 131
                    };
                }
                catch { }
            }
            return MakeFallbackCard(name);
        }

        private static Border MakeFallbackCard(string name) =>
            new Border
            {
                Width = 94,
                Height = 131,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x1A, 0x2A, 0x5E),
                    Color.FromRgb(0x0E, 0x18, 0x3A),
                    new Point(0, 0), new Point(1, 1)),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(4)
                }
            };

        private static UIElement MakeEmptyPocket() =>
            new Border
            {
                Margin = new Thickness(6),
                Width = 112,
                Height = 165,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0xAA, 0xA0, 0x90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(40, 0xCC, 0xC8, 0xBC))
            };

        // ── Context menu ─────────────────────────────────────────────────────
        private void ShowPocketContextMenu(BinderPocket pocket, FrameworkElement anchor)
        {
            var menu = new ContextMenu();

            var view = new MenuItem { Header = $"🔍 View Card — {pocket.Name}" };
            view.Click += (s, e) => ShowEnlarged(pocket);
            menu.Items.Add(view);

            menu.Items.Add(new Separator());

            var remove = new MenuItem
            {
                Header = pocket.IsWantList ? "✕ Remove from Want List"
                                               : "✕ Remove from Trade Binder",
                Foreground = Brushes.IndianRed
            };
            remove.Click += (s, e) =>
            {
                if (MessageBox.Show($"Remove '{pocket.Name}'?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;
                RemoveEntry(pocket);
            };
            menu.Items.Add(remove);

            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void RemoveEntry(BinderPocket pocket)
        {
            using var db = new CollectionDbContext();
            if (pocket.IsWantList)
            {
                var e = db.WantListEntries.FirstOrDefault(
                    x => x.WantListEntryId == pocket.EntryId);
                if (e != null)
                {
                    if (e.Quantity > 1) e.Quantity--;
                    else db.WantListEntries.Remove(e);
                    db.SaveChanges();
                }
            }
            else
            {
                var e = db.TradeBinderEntries.FirstOrDefault(
                    x => x.TradeBinderEntryId == pocket.EntryId);
                if (e != null)
                {
                    if (e.Quantity > 1) e.Quantity--;
                    else db.TradeBinderEntries.Remove(e);
                    db.SaveChanges();
                }
            }
            LoadData();
            RenderPage();
        }

        // ── Enlarged card view ───────────────────────────────────────────────
        private void ShowEnlarged(BinderPocket pocket)
        {
            var win = new BinderCardWindow(pocket) { Owner = this };
            win.ShowDialog();
        }


        // ── Tab handlers ─────────────────────────────────────────────────────
        private void BtnTabHave_Click(object sender, RoutedEventArgs e)
        {
            _showingHave = true;
            _currentPage = 0;
            BtnTabHave.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            BtnTabHave.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x0E, 0x08));
            BtnTabWant.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x28, 0x10));
            BtnTabWant.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            RenderPage();
        }

        private void BtnTabWant_Click(object sender, RoutedEventArgs e)
        {
            _showingHave = false;
            _currentPage = 0;
            BtnTabWant.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            BtnTabWant.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x0E, 0x08));
            BtnTabHave.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x28, 0x10));
            BtnTabHave.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            RenderPage();
        }

        // ── Navigation ───────────────────────────────────────────────────────
        private void BtnFirst_Click(object sender, RoutedEventArgs e)
        { _currentPage = 0; RenderPage(); }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        { if (_currentPage > 0) { _currentPage--; RenderPage(); } }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var cards = _showingHave ? _haveCards : _wantCards;
            int totalPages = Math.Max(1,
                (int)Math.Ceiling(cards.Count / (double)CardsPerPage));
            if (_currentPage < totalPages - 1) { _currentPage++; RenderPage(); }
        }

        // ── Keyboard navigation ──────────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Right || e.Key == Key.PageDown)
                BtnNext_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Left || e.Key == Key.PageUp)
                BtnPrev_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Home)
                BtnFirst_Click(this, new RoutedEventArgs());
        }
    }
}