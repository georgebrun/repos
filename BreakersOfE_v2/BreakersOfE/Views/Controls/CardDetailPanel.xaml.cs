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
    /// (the View tables, grid and gallery, and the Edit pages). The host page calls
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

            // Finishes the printing exists in (Scryfall "finishes")
            bool isFoil = bool.TryParse(Get("IsFoil"), out var f) && f;
            bool isNonFoil = bool.TryParse(Get("IsNonFoil"), out var nf) && nf;
            bool isEtched = bool.TryParse(Get("IsEtched"), out var et) && et;
            DetailFinishes.Text = Models.CardFinish.AvailableText(isNonFoil, isFoil, isEtched);

            // Prices, one per finish
            string usd = Get("PriceUsd") is string pu && !string.IsNullOrEmpty(pu) ? $"USD:  ${pu}" : "";
            string foilP = Get("PriceUsdFoil") is string pf && !string.IsNullOrEmpty(pf) ? $"USD Foil: ${pf}" : "";
            string etchedP = Get("PriceUsdEtched") is string pe && !string.IsNullOrEmpty(pe) ? $"USD Etched: ${pe}" : "";
            // Online (MTGO pool / collection): the price in event tickets.
            string tixP = Get("IsOnMtgo") == "True" && Get("PriceTix") is string pt && !string.IsNullOrEmpty(pt)
                ? $"MTGO: {pt} tix" : "";
            DetailPrices.Text = string.Join("\n",
                new[] { usd, foilP, etchedP, tixP }.Where(s => !string.IsNullOrEmpty(s)));

            // Your decks with this card, any printing (the deck files, cached).
            try
            {
                var uses = Services.DeckIndexService.DecksUsing(Get("Name"));
                DetailInDecks.Text = uses.Count == 0
                    ? "Not in any of your decks"
                    : string.Join("\n", uses.Select(u => u.Text));
            }
            catch
            {
                DetailInDecks.Text = "";
            }

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
                    // Common's black symbol is for the light table rows; here it sits on the window.
                    new object[] { setSymbolPath, string.Equals(rarity, "common", StringComparison.OrdinalIgnoreCase) ? "ondark" : rarity },
                    typeof(ImageSource), null!,
                    System.Globalization.CultureInfo.CurrentCulture);
                DetailSetSymbol.Source = img as ImageSource;
            }
            else
            {
                DetailSetSymbol.Source = null;
            }

            // Card image (shared ScryfallId cache first)
            ShowPicture(back: false);

            // Back face button
            string backUrl = Get("ImageBackUrl");
            BtnShowBackFace.Visibility = !string.IsNullOrEmpty(backUrl)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private int _picVersion;

        /// <summary>The card's picture (front or back): card back while it comes, "Scryfall Fail" if none.</summary>
        private void ShowPicture(bool back)
        {
            int v = ++_picVersion;
            _ = back
                ? Services.CardPictures.ShowAsync(DetailCardImage, null, Get("ImageBackUrl"), Get("LocalImageBackPath"), () => v == _picVersion)
                : Services.CardPictures.ShowAsync(DetailCardImage, Get("ScryfallId"), Get("ImageNormalUrl"), Get("LocalImagePath"), () => v == _picVersion);
        }

        private void BtnShowBackFace_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;

            _showingBack = !_showingBack;
            if (_showingBack)
            {
                ShowPicture(back: true);
                BtnShowBackFace.Content = "🔄 Show Front Face";
            }
            else
            {
                ShowPicture(back: false);
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
            _picVersion++;                          // a picture still on its way is no longer wanted
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
            DetailInDecks.Text = "";
            DetailManaCost.Items.Clear();
            BtnShowBackFace.Visibility = Visibility.Collapsed;
        }
    }
}
