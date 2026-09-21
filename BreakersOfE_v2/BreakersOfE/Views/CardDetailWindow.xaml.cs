using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    public partial class CardDetailWindow : FluentWindow
    {
        private readonly object _card;
        private bool _showingBack;

        /// <summary>
        /// Opens a detail popup for any card-like object (PoolCard, TokenCard, etc).
        /// Uses reflection so it works across all pool table types.
        /// </summary>
        public CardDetailWindow(object card, Window? owner = null)
        {
            InitializeComponent();
            _card = card;
            if (owner != null) Owner = owner;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                    Close();
            };
            Populate();
        }

        private string Get(string prop) =>
            _card.GetType().GetProperty(prop)?.GetValue(_card)?.ToString() ?? "";

        private void Populate()
        {
            CardName.Text = Get("Name");
            CardType.Text = Get("TypeLine");
            CardSet.Text = $"{Get("SetName")} ({Get("SetCode")})";
            CardCollectorNumber.Text = Get("CollectorNumber");
            CardRarity.Text = Get("Rarity");
            CardArtist.Text = Get("Artist");
            CardOracle.Text = Get("OracleText");
            CardFlavor.Text = Get("FlavorText");

            // P/T or Loyalty
            string p = Get("Power"), t = Get("Toughness"), loy = Get("LoyaltyOrDefense");
            if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(t))
            {
                PTLabel.Text = "POWER / TOUGHNESS";
                CardPT.Text = $"{p}/{t}";
            }
            else if (!string.IsNullOrEmpty(loy))
            {
                PTLabel.Text = "LOYALTY / DEFENSE";
                CardPT.Text = loy;
            }
            else
            {
                PTLabel.Visibility = Visibility.Collapsed;
                CardPT.Visibility = Visibility.Collapsed;
            }

            // Finishes
            bool isFoil = bool.TryParse(Get("IsFoil"), out var f) && f;
            bool isNonFoil = bool.TryParse(Get("IsNonFoil"), out var nf) && nf;
            var finishes = new List<string>();
            if (isFoil) finishes.Add("Foil");
            if (isNonFoil) finishes.Add("Non-Foil");
            CardFinishes.Text = finishes.Count > 0
                ? string.Join(" · ", finishes) : "Unknown";

            // Prices
            string usd = Get("PriceUsd") is string pu && !string.IsNullOrEmpty(pu) ? $"USD:  ${pu}" : "";
            string foilP = Get("PriceUsdFoil") is string pf && !string.IsNullOrEmpty(pf) ? $"USD Foil: ${pf}" : "";
            CardPrices.Text = string.Join("\n",
                new[] { usd, foilP }.Where(s => !string.IsNullOrEmpty(s)));

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
                SetSymbol.Source = img as ImageSource;
            }

            // Card image
            LoadImage(Get("ImageNormalUrl"), Get("LocalImagePath"));

            // Back face
            string backUrl = Get("ImageBackUrl");
            BtnFlipFace.Visibility = !string.IsNullOrEmpty(backUrl)
                ? Visibility.Visible : Visibility.Collapsed;
        }

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
                else
                {
                    CardImage.Source = null;
                }
            }
            catch { CardImage.Source = null; }
        }

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
                LoadImage(Get("ImageNormalUrl"), Get("LocalImagePath"));
                BtnFlipFace.Content = "🔄 Show Back Face";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}