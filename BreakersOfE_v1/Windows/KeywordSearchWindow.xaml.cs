using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Windows
{
    public partial class KeywordSearchWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────────
        private List<PoolCard> _pool = new();
        private HashSet<string> _collectionScryfallIds = new(System.StringComparer.OrdinalIgnoreCase);
        private HashSet<int> _collectionPoolIds = new();
        private readonly HashSet<string> _selectedKeywords = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressFilter = false;

        // ── Constructor ───────────────────────────────────────────────────────
        public KeywordSearchWindow()
        {
            InitializeComponent();
            Loaded += KeywordSearchWindow_Loaded;
        }

        // ── Precomputed tree data (built on background thread) ─────────────
        private List<(string CategoryName, List<(string Name, string Definition, bool InPool)>)>
            _treeData = new();
        private volatile bool _poolLoaded = false;

        private async void KeywordSearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ResultCountText.Text = "Loading collection...";

            // ── Phase 1: collection + keyword tree (fast) ─────────────────
            await Task.Run(() =>
            {
                // Pre-init keyword service on background thread
                _ = MtgKeywordService.All;

                using var cdb = new CollectionDbContext();
                var entries = cdb.CollectionEntries.AsNoTracking()
                    .Select(e => new { e.ScryfallId, e.PoolId })
                    .ToList();
                _collectionScryfallIds = new HashSet<string>(
                    entries.Where(e => !string.IsNullOrEmpty(e.ScryfallId))
                           .Select(e => e.ScryfallId),
                    System.StringComparer.OrdinalIgnoreCase);
                _collectionPoolIds = new HashSet<int>(
                    entries.Where(e => e.PoolId > 0)
                           .Select(e => e.PoolId));

                BuildTreeData();
            });

            // Show tree + enable UI immediately with collection
            BuildKeywordTreeUI();
            RbCollection.IsChecked = true;
            ApplyFilter();
            IsEnabled = true;
            ResultCountText.Text += "  —  Loading card pool in background...";

            // ── Phase 2: pool in background (large, non-blocking) ─────────
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                _pool = db.PoolCards.AsNoTracking()
                    .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                    .ToList();
                _poolLoaded = true;
            });

            // Pool ready — update status and enable pool source
            RbPool.IsEnabled = true;
            var currentCount = RbCollection.IsChecked == true
                ? _collectionScryfallIds.Count : _pool.Count;
            ResultCountText.Text = $"{currentCount:N0} card{(currentCount == 1 ? "" : "s")}  —  Pool loaded ({_pool.Count:N0} cards)";
            if (RbPool.IsChecked == true) ApplyFilter();
        }

        // ── Data loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds the keyword tree DATA on a background thread — no UI elements.
        /// </summary>
        private void BuildTreeData()
        {
            // Gather keywords actually present in pool from DB (fast query)
            using var db = new AppDbContext();
            var rawKeywords = db.PoolCards.AsNoTracking()
                .Where(c => c.Keywords != null && c.Keywords != "")
                .Select(c => c.Keywords)
                .ToList();

            var poolKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kwList in rawKeywords)
                foreach (var kw in kwList.Split('|',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    poolKeywords.Add(kw);

            _treeData = new();
            foreach (var (categoryName, keywords) in MtgKeywordService.ByCategory)
            {
                var items = new List<(string Name, string Definition, bool InPool)>();
                foreach (var kw in keywords)
                {
                    bool inPool = poolKeywords.Contains(kw.Name);
                    items.Add((kw.Name, kw.Definition, inPool));
                }
                if (items.Count > 0)
                    _treeData.Add((categoryName, items));
            }
        }

        /// <summary>
        /// Creates UI elements from pre-built tree data. Runs on UI thread but
        /// does no DB queries or heavy computation.
        /// </summary>
        private void BuildKeywordTreeUI()
        {
            KeywordTree.Items.Clear();

            foreach (var (categoryName, items) in _treeData)
            {
                var catItem = new TreeViewItem
                {
                    Header = $"▶ {categoryName}",
                    IsExpanded = false,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(2)
                };

                foreach (var (name, definition, inPool) in items)
                {
                    var cb = new CheckBox
                    {
                        Content = name,
                        IsEnabled = inPool,
                        Opacity = inPool ? 1.0 : 0.4,
                        Margin = new Thickness(0, 1, 0, 1),
                        ToolTip = definition,
                        Tag = name
                    };
                    cb.Checked += Keyword_CheckChanged;
                    cb.Unchecked += Keyword_CheckChanged;

                    var kwItem = new TreeViewItem
                    {
                        Header = cb,
                        Padding = new Thickness(0)
                    };
                    catItem.Items.Add(kwItem);
                }

                KeywordTree.Items.Add(catItem);
            }
        }

        private void Keyword_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string name) return;
            if (cb.IsChecked == true) _selectedKeywords.Add(name);
            else _selectedKeywords.Remove(name);
            ApplyFilter();
        }

        // ── Filter ────────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            if (_suppressFilter) return;

            // Guard against calls during InitializeComponent before controls exist
            if (RbCollection == null || RbAnd == null || ResultsGrid == null) return;

            bool useCollection = RbCollection.IsChecked == true;
            bool andLogic = RbAnd.IsChecked == true;

            // Selected color identity letters
            var selColors = new List<char>();
            if (BtnW.IsChecked == true) selColors.Add('W');
            if (BtnU.IsChecked == true) selColors.Add('U');
            if (BtnB.IsChecked == true) selColors.Add('B');
            if (BtnR.IsChecked == true) selColors.Add('R');
            if (BtnG.IsChecked == true) selColors.Add('G');
            if (BtnC.IsChecked == true) selColors.Add('C');

            bool atMost = RbColorAtMost.IsChecked == true;
            bool includes = RbColorIncludes.IsChecked == true;
            bool exact = RbColorExact.IsChecked == true;

            // Legality filters
            bool needStandard = ChkStandard.IsChecked == true;
            bool needPioneer = ChkPioneer.IsChecked == true;
            bool needModern = ChkModern.IsChecked == true;
            bool needLegacy = ChkLegacy.IsChecked == true;
            bool needVintage = ChkVintage.IsChecked == true;
            bool needCommander = ChkCommander.IsChecked == true;
            bool needPauper = ChkPauper.IsChecked == true;
            bool anyLegality = needStandard || needPioneer || needModern ||
                                 needLegacy || needVintage || needCommander || needPauper;

            // Keyword filter text (for filtering the tree display)
            string kwSearch = TxtKeywordSearch.Text.Trim();

            IEnumerable<PoolCard> filtered;

            if (useCollection)
            {
                filtered = FilterPoolCards(
                    _pool.Where(pc =>
                        (!string.IsNullOrEmpty(pc.ScryfallId) && _collectionScryfallIds.Contains(pc.ScryfallId)) ||
                        (pc.PoolId > 0 && _collectionPoolIds.Contains(pc.PoolId))),
                    andLogic, selColors, atMost, includes, exact,
                    anyLegality, needStandard, needPioneer, needModern,
                    needLegacy, needVintage, needCommander, needPauper);
            }
            else
            {
                filtered = FilterPoolCards(
                    _pool,
                    andLogic, selColors, atMost, includes, exact,
                    anyLegality, needStandard, needPioneer, needModern,
                    needLegacy, needVintage, needCommander, needPauper);
            }

            var results = filtered.ToList();
            for (int i = 0; i < results.Count; i++) results[i].RowIndex = i;

            ResultsGrid.ItemsSource = results;
            ResultCountText.Text = $"{results.Count:N0} card{(results.Count == 1 ? "" : "s")}";

            UpdateActionButtons();
        }

        private IEnumerable<PoolCard> FilterPoolCards(
            IEnumerable<PoolCard> source,
            bool andLogic,
            List<char> selColors,
            bool atMost, bool includes, bool exact,
            bool anyLegality,
            bool needStandard, bool needPioneer, bool needModern,
            bool needLegacy, bool needVintage, bool needCommander, bool needPauper)
        {
            return source.Where(card =>
            {
                // ── Keyword filter ───────────────────────────────────────────
                if (_selectedKeywords.Count > 0)
                {
                    var cardKws = MtgKeywordService.GetCardKeywords(
                        card.Keywords, card.OracleText)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    bool kwMatch = andLogic
                        ? _selectedKeywords.All(k => cardKws.Contains(k))
                        : _selectedKeywords.Any(k => cardKws.Contains(k));

                    if (!kwMatch) return false;
                }

                // ── Color identity filter ────────────────────────────────────
                if (selColors.Count > 0)
                {
                    var cardCI = card.ColorIdentity
                        .Where(ch => "WUBRG".Contains(char.ToUpper(ch)))
                        .Select(char.ToUpper)
                        .ToHashSet();

                    bool colorMatch;
                    if (atMost)
                        // Card's colors must ALL be within selected set
                        colorMatch = cardCI.All(c => selColors.Contains(c));
                    else if (includes)
                        // Card must contain ALL selected colors
                        colorMatch = selColors.All(c => cardCI.Contains(c));
                    else // exact
                        // Card's colors == selected colors exactly
                        colorMatch = cardCI.Count == selColors.Count &&
                                     selColors.All(c => cardCI.Contains(c));

                    // Handle colorless filter
                    if (selColors.Contains('C') && cardCI.Count == 0)
                        colorMatch = true;

                    if (!colorMatch) return false;
                }

                // ── Legality filter ──────────────────────────────────────────
                if (anyLegality)
                {
                    if (needStandard && !card.IsLegalStandard) return false;
                    if (needPioneer && !card.IsLegalPioneer) return false;
                    if (needModern && !card.IsLegalModern) return false;
                    if (needLegacy && !card.IsLegalLegacy) return false;
                    if (needVintage && !card.IsLegalVintage) return false;
                    if (needCommander &&
                        GetLegalityRaw(card, "commander") != "legal") return false;
                    if (needPauper &&
                        GetLegalityRaw(card, "pauper") != "legal") return false;
                }

                return true;
            });
        }

        private static string GetLegalityRaw(PoolCard card, string format)
        {
            if (string.IsNullOrWhiteSpace(card.LegalitiesJson)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(card.LegalitiesJson);
                if (doc.RootElement.TryGetProperty(format, out var v))
                    return v.GetString()?.ToLower() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void Source_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Logic_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Color_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Legality_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void TxtKeywordSearch_Changed(object sender, TextChangedEventArgs e)
        {
            // Filter the keyword tree to matching entries
            string q = TxtKeywordSearch.Text.Trim();
            foreach (TreeViewItem cat in KeywordTree.Items)
            {
                int visible = 0;
                foreach (TreeViewItem kwItem in cat.Items)
                {
                    if (kwItem.Header is CheckBox cb)
                    {
                        bool show = string.IsNullOrEmpty(q) ||
                                    cb.Tag?.ToString()?.Contains(q,
                                        StringComparison.OrdinalIgnoreCase) == true;
                        kwItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                        if (show) visible++;
                    }
                }
                cat.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (visible > 0 && !string.IsNullOrEmpty(q))
                    cat.IsExpanded = true;
            }
        }

        private void BtnClearKeywords_Click(object sender, RoutedEventArgs e)
        {
            _selectedKeywords.Clear();
            foreach (TreeViewItem cat in KeywordTree.Items)
                foreach (TreeViewItem kwItem in cat.Items)
                    if (kwItem.Header is CheckBox cb)
                        cb.IsChecked = false;
            ApplyFilter();
        }

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilter = true;

            // Reset source
            if (_poolLoaded)
                RbPool.IsChecked = true;
            else
                RbCollection.IsChecked = true;

            // Reset keywords
            _selectedKeywords.Clear();
            foreach (TreeViewItem cat in KeywordTree.Items)
            {
                cat.IsExpanded = false;
                cat.Visibility = Visibility.Visible;
                foreach (TreeViewItem kwItem in cat.Items)
                {
                    kwItem.Visibility = Visibility.Visible;
                    if (kwItem.Header is CheckBox cb) cb.IsChecked = false;
                }
            }
            TxtKeywordSearch.Text = string.Empty;

            // Reset logic
            RbAnd.IsChecked = true;

            // Reset colors
            BtnW.IsChecked = BtnU.IsChecked = BtnB.IsChecked =
            BtnR.IsChecked = BtnG.IsChecked = BtnC.IsChecked = false;
            RbColorAtMost.IsChecked = true;

            // Reset legality
            ChkStandard.IsChecked = ChkPioneer.IsChecked = ChkModern.IsChecked =
            ChkLegacy.IsChecked = ChkVintage.IsChecked = ChkCommander.IsChecked =
            ChkPauper.IsChecked = false;

            _suppressFilter = false;
            ApplyFilter();
        }

        private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;

            DetailName.Text = pc.Name;
            DetailType.Text = pc.TypeLine;
            DetailSet.Text = $"{pc.SetCode} — {pc.SetName}";
            DetailOracle.Text = pc.OracleText;
            DetailPT.Text = string.IsNullOrEmpty(pc.Power) ? string.Empty
                                : $"{pc.Power} / {pc.Toughness}";
            DetailPrices.Text = pc.PriceUsd.HasValue
                                ? $"${pc.PriceUsd:F2}" + (pc.PriceUsdFoil.HasValue
                                    ? $"  Foil: ${pc.PriceUsdFoil:F2}" : string.Empty)
                                : string.Empty;

            // Mana cost as plain text
            DetailManaCostPanel.Children.Clear();
            if (!string.IsNullOrEmpty(pc.ManaCost))
                DetailManaCostPanel.Children.Add(new TextBlock
                {
                    Text = pc.ManaCost,
                    FontSize = 12
                });

            // Load image — local first, URL fallback
            BitmapImage? bmp = null;
            if (!string.IsNullOrEmpty(pc.LocalImagePath) &&
                System.IO.File.Exists(pc.LocalImagePath))
            {
                try
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pc.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                catch { bmp = null; }
            }

            if (bmp == null && !string.IsNullOrEmpty(pc.ImageNormalUrl))
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    using var cts = new System.Threading.CancellationTokenSource(
                        TimeSpan.FromSeconds(8));
                    var bytes = await http.GetByteArrayAsync(pc.ImageNormalUrl, cts.Token);
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new System.IO.MemoryStream(bytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                catch { bmp = null; }
            }

            if (bmp != null)
            {
                CardImage.Source = bmp;
            }
            else
            {
                string fallback = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources", "Images", "image_unavailable.png");
                if (System.IO.File.Exists(fallback))
                {
                    try
                    {
                        var fb = new BitmapImage();
                        fb.BeginInit();
                        fb.UriSource = new Uri(fallback, UriKind.Absolute);
                        fb.CacheOption = BitmapCacheOption.OnLoad;
                        fb.EndInit();
                        fb.Freeze();
                        CardImage.Source = fb;
                    }
                    catch { CardImage.Source = null; }
                }
                else
                    CardImage.Source = null;
            }
        }

        private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click same as single click — already handled by SelectionChanged
        }

        private void UpdateActionButtons()
        {
            bool hasSelection = ResultsGrid.SelectedItem != null;
            bool useCollection = RbCollection.IsChecked == true;
            BtnAddToDeck.IsEnabled = hasSelection;
            BtnAddToCollection.IsEnabled = hasSelection && !useCollection;
            BtnAddToWantList.IsEnabled = hasSelection;
        }

        // ── Action buttons ────────────────────────────────────────────────────
        private void BtnAddToDeck_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            // Signal parent window to add to active deck
            (Owner as MainWindow)?.AddPoolCardToActiveDeck(pc);
            MessageBox.Show($"'{pc.Name}' added to active deck.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            using var cdb = new CollectionDbContext();
            var existing = !string.IsNullOrEmpty(pc.ScryfallId)
                ? cdb.CollectionEntries.FirstOrDefault(c => c.ScryfallId == pc.ScryfallId)
                : cdb.CollectionEntries.FirstOrDefault(c => c.PoolId == pc.PoolId);
            if (existing == null)
                cdb.CollectionEntries.Add(new CollectionEntry
                {
                    PoolId = pc.PoolId,
                    ScryfallId = pc.ScryfallId,
                    OracleId = pc.OracleId,
                    Name = pc.Name,
                    ManaCost = pc.ManaCost,
                    ManaValue = pc.ManaValue,
                    TypeLine = pc.TypeLine,
                    OracleText = pc.OracleText,
                    FlavorText = pc.FlavorText,
                    Power = pc.Power,
                    Toughness = pc.Toughness,
                    LoyaltyOrDefense = pc.LoyaltyOrDefense,
                    Colors = pc.Colors,
                    ColorIdentity = pc.ColorIdentity,
                    SetCode = pc.SetCode,
                    SetName = pc.SetName,
                    SetType = pc.SetType,
                    CollectorNumber = pc.CollectorNumber,
                    Rarity = pc.Rarity,
                    Artist = pc.Artist,
                    ImageSmallUrl = pc.ImageSmallUrl,
                    ImageNormalUrl = pc.ImageNormalUrl,
                    ImageBackUrl = pc.ImageBackUrl,
                    LocalImagePath = pc.LocalImagePath,
                    LocalImageBackPath = pc.LocalImageBackPath,
                    Layout = pc.Layout,
                    IsFoilAvailable = pc.IsFoil,
                    IsNonFoilAvailable = pc.IsNonFoil,
                    IsToken = pc.IsToken,
                    IsMeld = pc.IsMeld,
                    ReleasedAt = pc.ReleasedAt,
                    LegalitiesJson = pc.LegalitiesJson,
                    IsFavorite = pc.IsFavorite,
                    Keywords = pc.Keywords,
                    PriceUsd = pc.PriceUsd,
                    PriceUsdFoil = pc.PriceUsdFoil,
                    PriceUsdEtched = pc.PriceUsdEtched,
                    PriceEur = pc.PriceEur,
                    PriceEurFoil = pc.PriceEurFoil,
                    PriceTix = pc.PriceTix,
                    PricesJson = pc.PricesJson,
                    Quantity = 1,
                    Condition = "Near Mint",
                    Language = "English",
                    DateAdded = DateTime.Now,
                    DateModified = DateTime.Now
                });
            else
                existing.Quantity++;
            cdb.SaveChanges();
            MessageBox.Show($"'{pc.Name}' added to collection.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddToWantList_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            using var cdb = new CollectionDbContext();
            var existing = !string.IsNullOrEmpty(pc.ScryfallId)
                ? cdb.WantListEntries.FirstOrDefault(w => w.ScryfallId == pc.ScryfallId)
                : cdb.WantListEntries.FirstOrDefault(w => w.PoolId == pc.PoolId);
            if (existing == null)
                cdb.WantListEntries.Add(new WantListEntry
                {
                    PoolId = pc.PoolId,
                    ScryfallId = pc.ScryfallId,
                    Name = pc.Name,
                    SetCode = pc.SetCode,
                    SetName = pc.SetName,
                    CollectorNumber = pc.CollectorNumber,
                    TypeLine = pc.TypeLine,
                    OracleText = pc.OracleText,
                    FlavorText = pc.FlavorText,
                    ManaCost = pc.ManaCost,
                    ManaValue = pc.ManaValue,
                    ColorIdentity = pc.ColorIdentity,
                    Colors = pc.Colors,
                    Rarity = pc.Rarity,
                    Artist = pc.Artist,
                    Power = pc.Power,
                    Toughness = pc.Toughness,
                    IsFoilAvailable = pc.IsFoil,
                    IsNonFoilAvailable = pc.IsNonFoil,
                    PriceUsd = pc.PriceUsd,
                    PriceUsdFoil = pc.PriceUsdFoil,
                    ImageNormalUrl = pc.ImageNormalUrl,
                    LocalImagePath = pc.LocalImagePath,
                    LegalitiesJson = pc.LegalitiesJson,
                    Quantity = 1,
                    DateAdded = DateTime.Now
                });
            else
                existing.Quantity++;
            cdb.SaveChanges();
            MessageBox.Show($"'{pc.Name}' added to Want List.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDictionary_Click(object sender, RoutedEventArgs e)
        {
            var win = new KeywordDictionaryWindow { Owner = this };
            win.ShowDialog();
        }
    }
}