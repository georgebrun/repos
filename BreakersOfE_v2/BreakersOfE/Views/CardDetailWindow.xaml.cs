using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    public partial class CardDetailWindow : FluentWindow
    {
        private object _card;
        private bool _showingBack;

        // Navigation support
        private IList<object>? _allItems;
        private int _currentIndex;

        public CardDetailWindow(object card, Window? owner = null,
            IList<object>? allItems = null, int currentIndex = -1)
        {
            InitializeComponent();
            _card = card;
            _allItems = allItems;
            _currentIndex = currentIndex;
            if (owner != null) Owner = owner;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
                if (e.Key == System.Windows.Input.Key.Left) BtnPrev_Click(s, e);
                if (e.Key == System.Windows.Input.Key.Right) BtnNext_Click(s, e);
            };
            Populate();
        }

        /// <summary>Event raised when prev/next navigates to a different card.</summary>
        public event Action<object>? CardChanged;

        private string Get(string prop) =>
            _card.GetType().GetProperty(prop)?.GetValue(_card)?.ToString() ?? "";

        private void Populate()
        {
            _showingBack = false;

            // Name
            CardName.Text = Get("Name");
            Title = Get("Name");

            // Type, Color, MV
            CardType.Text = Get("TypeLine");
            CardColor.Text = FormatColors(Get("Colors"), Get("ColorIdentity"));
            CardMV.Text = Get("ManaValue");

            // Rarity badge
            string rarity = Get("Rarity");
            RarityText.Text = rarity;
            RarityBadge.Background = GetRarityBadgeBrush(rarity);

            // P/T or Loyalty
            string p = Get("Power"), t = Get("Toughness"), loy = Get("LoyaltyOrDefense");
            if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(t))
            {
                PTLabel.Text = "POWER";
                CardPT.Text = $"{p}/{t}";
                PTLabel.Visibility = Visibility.Visible;
                CardPT.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(loy))
            {
                PTLabel.Text = "LOYALTY";
                CardPT.Text = loy;
                PTLabel.Visibility = Visibility.Visible;
                CardPT.Visibility = Visibility.Visible;
            }
            else
            {
                PTLabel.Visibility = Visibility.Collapsed;
                CardPT.Visibility = Visibility.Collapsed;
            }

            // Artist, Finishes, Number
            CardArtist.Text = Get("Artist");
            bool isFoil = bool.TryParse(Get("IsFoil"), out var f) && f;
            bool isNonFoil = bool.TryParse(Get("IsNonFoil"), out var nf) && nf;
            var finishes = new List<string>();
            if (isNonFoil) finishes.Add("Non-Foil");
            if (isFoil) finishes.Add("Foil");
            CardFinishes.Text = finishes.Count > 0 ? string.Join(" · ", finishes) : "—";
            CardNumber.Text = Get("CollectorNumber");

            // Mana cost symbols
            ManaCostPanel.Items.Clear();
            string manaCost = Get("ManaCost");
            if (!string.IsNullOrEmpty(manaCost))
            {
                var converter = new Services.ManaCostConverter();
                var symbols = converter.Convert(manaCost, typeof(object), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                if (symbols is System.Collections.IEnumerable items)
                    foreach (var sym in items)
                        ManaCostPanel.Items.Add(sym);
            }

            // Prices
            string usd = Get("PriceUsd");
            string foilPrice = Get("PriceUsdFoil");
            CardPriceMain.Text = !string.IsNullOrEmpty(usd) ? $"${usd}" :
                                 !string.IsNullOrEmpty(foilPrice) ? $"${foilPrice}" : "—";
            var details = new List<string>();
            if (!string.IsNullOrEmpty(usd)) details.Add($"Non-Foil: ${usd}");
            if (!string.IsNullOrEmpty(foilPrice)) details.Add($"Foil: ${foilPrice}");
            CardPriceDetails.Text = string.Join("  ·  ", details);

            // Oracle + Flavor
            CardOracle.Text = Get("OracleText");
            string flavor = Get("FlavorText");
            CardFlavor.Text = flavor;
            FlavorHeader.Visibility = string.IsNullOrEmpty(flavor)
                ? Visibility.Collapsed : Visibility.Visible;
            FlavorBorder.Visibility = FlavorHeader.Visibility;

            // Set info
            string setSymbolPath = Get("SetSymbolPath");
            if (!string.IsNullOrEmpty(setSymbolPath))
            {
                var converter = new Services.ImageSourceConverter();
                var img = converter.Convert(
                    new object[] { setSymbolPath, rarity },
                    typeof(ImageSource), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                SetSymbol.Source = img as ImageSource;
            }
            else SetSymbol.Source = null;

            CardSet.Text = Get("SetName");
            CardSetDetail.Text = $"{Get("SetCode").ToUpper()} · #{Get("CollectorNumber")}";

            // Format legality badges
            PopulateLegality();

            // Card image
            LoadImage(Get("ImageNormalUrl"),
                    Services.ImageCacheService.GetCachedPath(Get("ScryfallId")) ?? Get("LocalImagePath"));
            string backUrl = Get("ImageBackUrl");
            BtnFlipFace.Visibility = !string.IsNullOrEmpty(backUrl)
                ? Visibility.Visible : Visibility.Collapsed;

            // Hide rulings from previous card
            RulingsHeader.Visibility = Visibility.Collapsed;
            RulingsBorder.Visibility = Visibility.Collapsed;
            RulingsPanel.Children.Clear();
        }

        // ── Format legality badges ──────────────────────────────────────
        private void PopulateLegality()
        {
            LegalityPanel.Children.Clear();
            string json = Get("LegalitiesJson");
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject()
                    .OrderBy(p => p.Name))
                {
                    string status = prop.Value.GetString()?.ToLower() ?? "";
                    if (status != "legal" && status != "restricted") continue;

                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(0, 0, 4, 4),
                        Background = status == "restricted"
                            ? new SolidColorBrush(Color.FromRgb(0x30, 0x80, 0xD0))
                            : new SolidColorBrush(Color.FromRgb(0x28, 0x80, 0x40)),
                        Child = new System.Windows.Controls.TextBlock
                        {
                            Text = FormatName(prop.Name),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Colors.White)
                        }
                    };
                    LegalityPanel.Children.Add(badge);
                }
            }
            catch { }
        }

        private static string FormatName(string s)
            => string.Concat(s.Select((c, i) =>
                i == 0 ? char.ToUpper(c) : c == '_' ? ' ' : c));

        // ── Image loading ───────────────────────────────────────────────
        private void LoadImage(string url, string localPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    CardImage.Source = bmp;
                    return;
                }
                if (!string.IsNullOrEmpty(url))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(url, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    CardImage.Source = bmp;
                }
                else CardImage.Source = null;
            }
            catch { CardImage.Source = null; }
        }

        // ── Rarity badge color ──────────────────────────────────────────
        private static SolidColorBrush GetRarityBadgeBrush(string rarity)
            => rarity?.ToLower() switch
            {
                "common" => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                "uncommon" => new SolidColorBrush(Color.FromRgb(0x60, 0x70, 0x80)),
                "rare" => new SolidColorBrush(Color.FromRgb(0xB8, 0x8A, 0x00)),
                "mythic" => new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x18)),
                "special" => new SolidColorBrush(Color.FromRgb(0x80, 0x40, 0xB0)),
                "bonus" => new SolidColorBrush(Color.FromRgb(0x80, 0x40, 0xB0)),
                _ => new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            };

        private static string FormatColors(string colors, string ci)
        {
            string c = !string.IsNullOrEmpty(colors) ? colors : ci;
            if (string.IsNullOrWhiteSpace(c)) return "Colorless";
            return c.Replace(",", ", ");
        }

        // ── Button handlers ─────────────────────────────────────────────
        private void BtnFlipFace_Click(object sender, RoutedEventArgs e)
        {
            _showingBack = !_showingBack;
            if (_showingBack)
            {
                LoadImage(Get("ImageBackUrl"), Get("LocalImageBackPath"));
                BtnFlipFace.Content = "🔄 Show Front Face";
            }
            else
            {
                LoadImage(Get("ImageNormalUrl"),
                    Services.ImageCacheService.GetCachedPath(Get("ScryfallId")) ?? Get("LocalImagePath"));
                BtnFlipFace.Content = "🔄 Show Back Face";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Prev / Next navigation ──────────────────────────────────────
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems == null || _allItems.Count == 0) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _allItems.Count - 1;
            _card = _allItems[_currentIndex];
            Populate();
            CardChanged?.Invoke(_card);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems == null || _allItems.Count == 0) return;
            _currentIndex++;
            if (_currentIndex >= _allItems.Count) _currentIndex = 0;
            _card = _allItems[_currentIndex];
            Populate();
            CardChanged?.Invoke(_card);
        }

        // ── Rulings (fetched on demand) ─────────────────────────────────
        private async void BtnRulings_Click(object sender, RoutedEventArgs e)
        {
            // Toggle if already showing
            if (RulingsBorder.Visibility == Visibility.Visible)
            {
                RulingsHeader.Visibility = Visibility.Collapsed;
                RulingsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            RulingsPanel.Children.Clear();
            RulingsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Loading rulings...",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
            });
            RulingsHeader.Visibility = Visibility.Visible;
            RulingsBorder.Visibility = Visibility.Visible;

            string sid = Get("ScryfallId");
            string oid = Get("OracleId");
            var rulings = await Services.RulingsService.GetRulingsAsync(sid, oid);

            RulingsPanel.Children.Clear();
            if (rulings.Count == 0)
            {
                RulingsPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "No rulings available.",
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
                });
                return;
            }

            foreach (var (date, text) in rulings)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = date,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextFillColorTertiaryBrush")
                });
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
                });
                RulingsPanel.Children.Add(sp);
            }
        }
    }
}