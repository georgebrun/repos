using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

        private string Get(string prop) => CardDetailText.Get(_card, prop);

        /// <summary>Show a card, or clear the panel when card is null.</summary>
        public void ShowCard(object? card)
        {
            _card = card;
            _showingBack = false;
            BtnShowBackFace.Content = CardDetailText.ShowBack;
            if (card == null) { Clear(); return; }

            DetailSet.Text = $"{Get("SetName")} ({Get("SetCode")})";
            DetailCollectorNumber.Text = Get("CollectorNumber");
            DetailRarity.Text = Get("Rarity");
            DetailArtist.Text = Get("Artist");

            // Name, type, cost, stats and text: the front side of a double-faced card
            ShowSide(CardDetailText.FirstSide(card));

            // Finishes and prices, one line per finish
            DetailFinishes.Text = CardDetailText.Finishes(card);
            DetailPrices.Text = string.Join("\n", CardDetailText.Prices(card));

            // Your decks with this card, any printing (the deck files, cached).
            try
            {
                var uses = Services.DeckIndexService.DecksUsing(Get("Name"));
                DetailInDecks.Text = uses.Count == 0
                    ? CardDetailText.NotInDecks(otherDecksOnly: false)
                    : string.Join("\n", uses.Select(u => u.Text));
            }
            catch
            {
                DetailInDecks.Text = "";
            }

            // Set symbol (rarity-tinted)
            DetailSetSymbol.Source = CardDetailText.SetSymbol(card);

            // Card image (shared ScryfallId cache first) and the back face button
            ShowPicture(back: false);
            BtnShowBackFace.Visibility = CardDetailText.HasBack(card) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>One side's name, type, mana cost, stats and text (-1 = the whole card).</summary>
        private void ShowSide(int side)
        {
            if (_card == null) return;
            var face = CardDetailText.Face(_card, side);
            DetailName.Text = face.Name;
            DetailType.Text = face.TypeLine;
            Services.ManaCostConverter.Fill(DetailManaCost, face.ManaCost);
            DetailOracle.Text = face.OracleText;
            DetailFlavor.Text = face.FlavorText;
            DetailPTLabel.Text = face.Stats?.Label ?? "";
            DetailPT.Text = face.Stats?.Value ?? "";
            DetailPTLabel.Visibility = DetailPT.Visibility = face.Stats != null ? Visibility.Visible : Visibility.Collapsed;
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
            ShowPicture(back: _showingBack);
            ShowSide(_showingBack ? 1 : 0);
            BtnShowBackFace.Content = _showingBack ? CardDetailText.ShowFront : CardDetailText.ShowBack;
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
