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

        /// <summary>Opened from a deck: that deck's file, left out of the deck list.</summary>
        private readonly string? _currentDeckPath;

        public CardDetailWindow(object card, Window? owner = null,
            IList<object>? allItems = null, int currentIndex = -1,
            string? currentDeckPath = null)
        {
            InitializeComponent();
            _currentDeckPath = currentDeckPath;
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

        private string Get(string prop) => CardDetailText.Get(_card, prop);

        private void Populate()
        {
            _showingBack = false;
            BtnFlipFace.Content = CardDetailText.ShowBack;  // (kept "Show Front Face" after Prev / Next)
            _rulingsVersion++;                              // rulings still loading belong to the old card

            // Window title: the whole card's name
            Title = Get("Name");

            // Name, type, cost, stats and text: the front side of a double-faced card
            ShowSide(CardDetailText.FirstSide(_card));

            // Color, MV
            CardColor.Text = FormatColors(Get("Colors"), Get("ColorIdentity"));
            CardMV.Text = Get("ManaValue");

            // Rarity badge
            string rarity = Get("Rarity");
            RarityText.Text = rarity;
            RarityBadge.Background = GetRarityBadgeBrush(rarity);

            // Artist, Finishes, Number
            CardArtist.Text = Get("Artist");
            CardFinishes.Text = CardDetailText.Finishes(_card);
            CardNumber.Text = Get("CollectorNumber");

            // Prices
            string usd = Get("PriceUsd");
            string foilPrice = Get("PriceUsdFoil");
            string etchedPrice = Get("PriceUsdEtched");
            // Online (MTGO pool / collection): the price in event tickets.
            string tix = CardDetailText.Tix(_card);
            CardPriceMain.Text = !string.IsNullOrEmpty(usd) ? $"${usd}" :
                                 !string.IsNullOrEmpty(foilPrice) ? $"${foilPrice}" :
                                 !string.IsNullOrEmpty(etchedPrice) ? $"${etchedPrice}" :
                                 !string.IsNullOrEmpty(tix) ? $"{tix} tix" : "—";
            CardPriceDetails.Text = string.Join("  ·  ", CardDetailText.Prices(_card));

            // Set info
            SetSymbol.Source = CardDetailText.SetSymbol(_card);

            CardSet.Text = Get("SetName");
            CardSetDetail.Text = $"{Get("SetCode").ToUpper()} · #{Get("CollectorNumber")}";

            // Format legality badges
            PopulateLegality();

            // Card image
            ShowPicture(back: false);
            BtnFlipFace.Visibility = CardDetailText.HasBack(_card) ? Visibility.Visible : Visibility.Collapsed;

            // Hide rulings from previous card
            RulingsHeader.Visibility = Visibility.Collapsed;
            RulingsBorder.Visibility = Visibility.Collapsed;
            RulingsPanel.Children.Clear();

            // Price history (last 5 snapshots)
            ShowPriceHistory();

            // Cards shared between decks: every deck that uses this card
            ShowDeckUses();
        }

        /// <summary>One side's name, type, mana cost, stats and text (-1 = the whole card).</summary>
        private void ShowSide(int side)
        {
            var face = CardDetailText.Face(_card, side);
            CardName.Text = face.Name;
            CardType.Text = face.TypeLine;
            Services.ManaCostConverter.Fill(ManaCostPanel, face.ManaCost);
            PTLabel.Text = face.Stats?.Label ?? "";
            CardPT.Text = face.Stats?.Value ?? "";
            PTLabel.Visibility = CardPT.Visibility = face.Stats != null ? Visibility.Visible : Visibility.Collapsed;
            CardOracle.Text = face.OracleText;
            CardFlavor.Text = face.FlavorText;
            FlavorHeader.Visibility = face.FlavorText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            FlavorBorder.Visibility = FlavorHeader.Visibility;
        }

        /// <summary>
        /// Every deck that uses this card (any printing). Opened from a deck:
        /// only the OTHER decks, so it matches that deck's Other Decks column.
        /// </summary>
        private void ShowDeckUses()
        {
            bool fromDeck = !string.IsNullOrEmpty(_currentDeckPath);
            var uses = Services.DeckIndexService.DecksUsing(Get("Name"), _currentDeckPath);

            string title = fromDeck ? "IN YOUR OTHER DECKS" : "IN YOUR DECKS";
            DecksHeader.Text = uses.Count == 0 ? $"▸ {title}" : $"▸ {title} ({uses.Count})";
            DecksList.ItemsSource = uses;

            DecksEmpty.Text = CardDetailText.NotInDecks(otherDecksOnly: fromDeck);
            DecksEmpty.Visibility = uses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Price history ───────────────────────────────────────────────
        /// <summary>One row under the price chart.</summary>
        public sealed class PriceHistoryRow
        {
            public string Date { get; init; } = "";
            public string NonFoil { get; init; } = "";
            public string Foil { get; init; } = "";
        }

        private static readonly Brush NonFoilBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xA0, 0xFF));
        /// <summary>Text in a theme colour, kept in step when Light / Dark changes.</summary>
        private static System.Windows.Controls.TextBlock Themed(System.Windows.Controls.TextBlock tb, string brushKey)
        {
            tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brushKey);
            return tb;
        }

        private static Brush FoilBrush =>                                     // gold that reads in Light and Dark
            Application.Current.TryFindResource("BoeGoldBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x00));
        private static readonly Brush EtchedBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x8C, 0xFF));

        private void ShowPriceHistory()
        {
            var points = Services.PriceHistoryService.GetCardHistory(Get("ScryfallId"), 5);

            if (points.Count == 0)
            {
                PriceChart.Visibility = Visibility.Collapsed;
                PriceHistoryChange.Visibility = Visibility.Collapsed;
                PriceHistoryList.ItemsSource = null;
                PriceHistoryEmpty.Text = "No price history for this card yet. Prices are saved each time you run Update Database.";
                PriceHistoryEmpty.Visibility = Visibility.Visible;
                return;
            }

            var labels = points.Select(p => p.Date.ToString("MMM d")).ToList();
            var series = new List<Controls.ChartSeries>();
            var changes = new List<string>();

            void Add(string name, Brush brush, List<decimal?> values)
            {
                if (!values.Any(v => v.HasValue)) return;
                series.Add(new Controls.ChartSeries { Name = name, Stroke = brush, Values = values });

                // First → last known value
                var known = values.Select((v, i) => (v, i)).Where(x => x.v.HasValue).ToList();
                if (known.Count < 2) return;
                decimal a = known[0].v!.Value, b = known[^1].v!.Value, d = b - a;
                string sign = d > 0 ? "+" : d < 0 ? "−" : "";
                string pct = a != 0m ? $", {sign}{Math.Abs(d / a * 100m):0.0}%" : "";
                changes.Add($"{name}: ${a:F2} → ${b:F2} ({sign}${Math.Abs(d):F2}{pct})");
            }

            Add("Non-Foil", NonFoilBrush, points.Select(p => p.Usd).ToList());
            Add("Foil", FoilBrush, points.Select(p => p.UsdFoil).ToList());
            Add("Etched", EtchedBrush, points.Select(p => p.UsdEtched).ToList());

            PriceChart.SetData(labels, series);
            PriceChart.Visibility = series.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            string since = points.Count > 1 ? $"   (since {points[0].Date:yyyy-MM-dd})" : "";
            PriceHistoryChange.Text = changes.Count > 0 ? string.Join("\n", changes) + since : "";
            PriceHistoryChange.Visibility = changes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Newest first in the list
            PriceHistoryList.ItemsSource = points.AsEnumerable().Reverse().Select(p => new PriceHistoryRow
            {
                Date = p.Date.ToString("yyyy-MM-dd"),
                NonFoil = p.Usd.HasValue ? $"${p.Usd.Value:F2}" : "—",
                Foil = string.Join("  ", new[]
                {
                    p.UsdFoil.HasValue ? $"F ${p.UsdFoil.Value:F2}" : "",
                    p.UsdEtched.HasValue ? $"E ${p.UsdEtched.Value:F2}" : "",
                }.Where(x => x.Length > 0)),
            }).ToList();

            PriceHistoryEmpty.Text = points.Count == 1
                ? "Only one snapshot so far. The line starts after your next Update Database on another day."
                : "";
            PriceHistoryEmpty.Visibility = points.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
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
        private int _picVersion;

        /// <summary>The card's picture (front or back): card back while it comes, "Scryfall Fail" if none.</summary>
        private void ShowPicture(bool back)
        {
            int v = ++_picVersion;
            _ = back
                ? Services.CardPictures.ShowAsync(CardImage, null, Get("ImageBackUrl"), Get("LocalImageBackPath"), () => v == _picVersion)
                : Services.CardPictures.ShowAsync(CardImage, Get("ScryfallId"), Get("ImageNormalUrl"), Get("LocalImagePath"), () => v == _picVersion);
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
            ShowPicture(back: _showingBack);
            ShowSide(_showingBack ? 1 : 0);
            BtnFlipFace.Content = _showingBack ? CardDetailText.ShowFront : CardDetailText.ShowBack;
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
        private int _rulingsVersion;

        private async void BtnRulings_Click(object sender, RoutedEventArgs e)
        {
            try { await ShowRulingsAsync(); }
            catch (Exception ex)
            {
                RulingsPanel.Children.Clear();
                RulingsPanel.Children.Add(Themed(new System.Windows.Controls.TextBlock
                {
                    Text = $"Couldn't load the rulings: {ex.Message}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }, "TextFillColorSecondaryBrush"));
            }
        }

        private async System.Threading.Tasks.Task ShowRulingsAsync()
        {
            // Toggle if already showing
            if (RulingsBorder.Visibility == Visibility.Visible)
            {
                RulingsHeader.Visibility = Visibility.Collapsed;
                RulingsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            RulingsPanel.Children.Clear();
            RulingsPanel.Children.Add(Themed(new System.Windows.Controls.TextBlock
            {
                Text = "Loading rulings…",
                FontSize = 12
            }, "TextFillColorSecondaryBrush"));
            RulingsHeader.Visibility = Visibility.Visible;
            RulingsBorder.Visibility = Visibility.Visible;

            string sid = Get("ScryfallId");
            string oid = Get("OracleId");
            int version = _rulingsVersion;
            var rulings = await Services.RulingsService.GetRulingsAsync(sid, oid);
            if (version != _rulingsVersion) return;           // Prev / Next moved on meanwhile

            RulingsPanel.Children.Clear();
            if (rulings.Count == 0)
            {
                RulingsPanel.Children.Add(Themed(new System.Windows.Controls.TextBlock
                {
                    Text = "No rulings available.",
                    FontSize = 12,
                    FontStyle = FontStyles.Italic
                }, "TextFillColorSecondaryBrush"));
                return;
            }

            foreach (var (date, text) in rulings)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(Themed(new System.Windows.Controls.TextBlock
                {
                    Text = date,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }, "TextFillColorTertiaryBrush"));
                sp.Children.Add(Themed(new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }, "TextFillColorPrimaryBrush"));
                RulingsPanel.Children.Add(sp);
            }
        }
    }
}