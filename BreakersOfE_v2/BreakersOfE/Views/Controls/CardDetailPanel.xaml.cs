using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Views.Controls
{
    /// <summary>
    /// The left-side card detail panel, shared by every page that shows cards
    /// (grid, gallery, and later the collection pages). The host page calls
    /// <see cref="ShowCard"/> when its selection changes; the panel raises
    /// <see cref="ImageDoubleClicked"/> so the host can open the detail window.
    /// Works for any card type (PoolCard, TokenCard, …) via reflection.
    /// </summary>
    public partial class CardDetailPanel : UserControl
    {
        private object? _card;
        private bool _showingBack;

        /// <summary>Raised when the user double-clicks the card image.</summary>
        public event EventHandler? ImageDoubleClicked;

        public CardDetailPanel()
        {
            InitializeComponent();
        }

        private string Get(string prop) =>
            _card?.GetType().GetProperty(prop)?.GetValue(_card)?.ToString() ?? "";

        /// <summary>Show a card, or clear the panel when card is null.</summary>
        public void ShowCard(object? card)
        {
            _card = card;
            _showingBack = false;
            BtnShowBackFace.Content = "🔄 Show Back Face";
            if (card == null) { Clear(); return; }

            DetailName.Text = Get("Name");
            DetailType.Text = Get("TypeLine");
            DetailSet.Text = $"{Get("SetName")} ({Get("SetCode")})";
            DetailCollectorNumber.Text = Get("CollectorNumber");
            DetailRarity.Text = Get("Rarity");
            DetailArtist.Text = Get("Artist");

            // Oracle + flavor
            DetailOracle.Text = Get("OracleText");
            DetailFlavor.Text = Get("FlavorText");

            // P/T or Loyalty
            string p = Get("Power"), t = Get("Toughness"), loy = Get("LoyaltyOrDefense");
            if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(t))
            {
                DetailPTLabel.Text = "POWER / TOUGHNESS";
                DetailPT.Text = $"{p}/{t}";
                DetailPTLabel.Visibility = Visibility.Visible;
                DetailPT.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrEmpty(loy))
            {
                DetailPTLabel.Text = "LOYALTY / DEFENSE";
                DetailPT.Text = loy;
                DetailPTLabel.Visibility = Visibility.Visible;
                DetailPT.Visibility = Visibility.Visible;
            }
            else
            {
                DetailPTLabel.Visibility = Visibility.Collapsed;
                DetailPT.Visibility = Visibility.Collapsed;
            }

            // Finishes
            bool isFoil = bool.TryParse(Get("IsFoil"), out var f) && f;
            bool isNonFoil = bool.TryParse(Get("IsNonFoil"), out var nf) && nf;
            var finishes = new List<string>();
            if (isFoil) finishes.Add("Foil");
            if (isNonFoil) finishes.Add("Non-Foil");
            DetailFinishes.Text = finishes.Count > 0
                ? string.Join(" · ", finishes) : "Unknown";

            // Prices
            string usd = Get("PriceUsd") is string pu && !string.IsNullOrEmpty(pu) ? $"USD:  ${pu}" : "";
            string foilP = Get("PriceUsdFoil") is string pf && !string.IsNullOrEmpty(pf) ? $"USD Foil: ${pf}" : "";
            DetailPrices.Text = string.Join("\n",
                new[] { usd, foilP }.Where(s => !string.IsNullOrEmpty(s)));

            // Mana cost symbols
            DetailManaCost.Items.Clear();
            string manaCost = Get("ManaCost");
            if (!string.IsNullOrEmpty(manaCost))
            {
                var converter = new Services.ManaCostConverter();
                var symbols = converter.Convert(manaCost, typeof(object), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                if (symbols is System.Collections.IEnumerable items)
                    foreach (var sym in items)
                        DetailManaCost.Items.Add(sym);
            }

            // Set symbol (rarity-tinted)
            string setSymbolPath = Get("SetSymbolPath");
            string rarity = Get("Rarity");
            if (!string.IsNullOrEmpty(setSymbolPath))
            {
                var converter = new Services.ImageSourceConverter();
                var img = converter.Convert(
                    new object[] { setSymbolPath, rarity },
                    typeof(ImageSource), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                DetailSetSymbol.Source = img as ImageSource;
            }
            else
            {
                DetailSetSymbol.Source = null;
            }

            // Card image (shared ScryfallId cache first)
            LoadCardImage(Get("ImageNormalUrl"),
                Services.ImageCacheService.GetCachedPath(Get("ScryfallId")) ?? Get("LocalImagePath"));

            // Back face button
            string backUrl = Get("ImageBackUrl");
            BtnShowBackFace.Visibility = !string.IsNullOrEmpty(backUrl)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadCardImage(string url, string localPath)
        {
            try
            {
                // Try local first
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    DetailCardImage.Source = bmp;
                    return;
                }

                // Fall back to URL
                if (!string.IsNullOrEmpty(url))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(url, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    DetailCardImage.Source = bmp;
                }
                else
                {
                    DetailCardImage.Source = null;
                }
            }
            catch { DetailCardImage.Source = null; }
        }

        private void BtnShowBackFace_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;

            _showingBack = !_showingBack;
            if (_showingBack)
            {
                LoadCardImage(Get("ImageBackUrl"), Get("LocalImageBackPath"));
                BtnShowBackFace.Content = "🔄 Show Front Face";
            }
            else
            {
                LoadCardImage(Get("ImageNormalUrl"),
                    Services.ImageCacheService.GetCachedPath(Get("ScryfallId")) ?? Get("LocalImagePath"));
                BtnShowBackFace.Content = "🔄 Show Back Face";
            }
        }

        private void DetailImage_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2 || _card == null) return;
            ImageDoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        private void Clear()
        {
            DetailCardImage.Source = null;
            DetailName.Text = "";
            DetailType.Text = "";
            DetailSet.Text = "";
            DetailSetSymbol.Source = null;
            DetailCollectorNumber.Text = "";
            DetailRarity.Text = "";
            DetailPT.Text = "";
            DetailPTLabel.Visibility = Visibility.Collapsed;
            DetailPT.Visibility = Visibility.Collapsed;
            DetailOracle.Text = "";
            DetailFlavor.Text = "";
            DetailArtist.Text = "";
            DetailFinishes.Text = "";
            DetailPrices.Text = "";
            DetailManaCost.Items.Clear();
            BtnShowBackFace.Visibility = Visibility.Collapsed;
        }
    }
}
