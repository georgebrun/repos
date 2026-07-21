using BreakersOfE.Models;
using BreakersOfE.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    public partial class TabletopWindow : Window
    {
        // ================================================================
        // STATE
        // ================================================================
        private int _yourLife = 40;
        private int _oppLife = 40;
        private int _yourPoison = 0;
        private int _oppPoison = 0;

        // Modern mechanics state
        private string _monarchHolder = "";  // "" = nobody, "you" or "opp"
        private string _initiativeHolder = ""; // "" = nobody, "you" or "opp"
        private int _yourEnergy = 0;
        private int _oppEnergy = 0;
        private int _ringLevel = 0;   // 0=none, 1-4 tempts
        private string _ringBearer = "";  // card name of ring-bearer
        private int _yourCmdDmg = 0;
        private int _oppCmdDmg = 0;
        private int _turnCounter = 1;
        private int _currentPhase = 0;
        private bool _stackVisible = false;
        private bool _tableRotated = false;

        // Game state
        private List<DeckCard> _yourLibrary = new();
        private List<DeckCard> _oppLibrary = new();
        private List<DeckCard> _yourHand = new();
        private List<DeckCard> _oppHand = new();
        private List<DeckCard> _yourGrave = new();
        private List<DeckCard> _oppGrave = new();
        private List<DeckCard> _yourExile = new();
        private List<DeckCard> _oppExile = new();
        private List<DeckCard> _yourBattlefield = new();
        private List<DeckCard> _oppBattlefield = new();
        private DeckCard? _yourCommander = null;
        private List<BattlefieldCard> _yourField = new();
        private List<BattlefieldCard> _oppField = new();
        private Window? _enlargedCardWindow = null;
        private List<LinkedExile> _linkedExiles = new();
        private readonly ObservableCollection<string> _gameLog = new();
        private DeckCard? _oppCommander = null;
        private int _yourCmdTax = 0;
        private int _oppCmdTax = 0;
        private CommanderLocation _yourCmdLocation = CommanderLocation.CommandZone;
        private CommanderLocation _oppCmdLocation = CommanderLocation.CommandZone;
        private string _yourPlayerName = "Player 1";
        private string _oppPlayerName = "Player 2";

        private const string SavedGameKey = "Tabletop_SavedGame";
        private static readonly System.Security.Cryptography.RandomNumberGenerator _cryptoRng
            = System.Security.Cryptography.RandomNumberGenerator.Create();

        // Returns a cryptographically random integer in [0, maxExclusive)
        private static int CryptoNext(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            // Rejection sampling to avoid modulo bias
            uint range = (uint)maxExclusive;
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint result;
            byte[] buf = new byte[4];
            do
            {
                _cryptoRng.GetBytes(buf);
                result = BitConverter.ToUInt32(buf, 0);
            } while (result >= limit);
            return (int)(result % range);
        }

        private static readonly string[] PhaseNames =
            { "Untap", "Upkeep", "Draw", "Main1", "Combat", "Main2", "End" };

        private readonly Deck? _yourDeck;
        private readonly Deck? _oppDeck;
        private readonly List<Deck> _allOpenDecks;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public TabletopWindow(List<Deck>? openDecks = null)
        {
            InitializeComponent();
            _allOpenDecks = openDecks ?? new List<Deck>();
            _yourDeck = _allOpenDecks.FirstOrDefault();
            _oppDeck = _allOpenDecks.Count > 1 ? _allOpenDecks[1] : null;

            Loaded += TabletopWindow_Loaded;

            // Close enlarged card view and collapse hand when clicking elsewhere
            MouseDown += (s, e) =>
            {
                if (_enlargedCardWindow != null && e.Source != _enlargedCardWindow)
                {
                    _enlargedCardWindow.Close();
                    _enlargedCardWindow = null;
                }

                // Collapse hand if expanded and click is outside the hand area
                if (_handExpanded && e.Source is FrameworkElement fe)
                {
                    var src = e.OriginalSource as DependencyObject;
                    bool inHand = false;
                    var cur = src;
                    while (cur != null)
                    {
                        if (cur == YourHandCanvas || cur == HandPeekLabel
                            || cur == YourHandBorder)
                        { inHand = true; break; }
                        cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
                    }
                    if (!inHand)
                    {
                        _handExpanded = false;
                        _handAnimTarget = HandCollapsedH;
                        _handAnimCurrent = YourHandRow.Height.Value;
                        HandPeekLabel.Visibility = Visibility.Visible;
                        _handAnimTimer?.Stop();
                        _handAnimTimer = new System.Windows.Threading.DispatcherTimer
                        { Interval = TimeSpan.FromMilliseconds(16) };
                        _handAnimTimer.Tick += (ts, _) =>
                        {
                            double diff = HandCollapsedH - _handAnimCurrent;
                            if (Math.Abs(diff) < 0.5)
                            {
                                YourHandRow.Height = new GridLength(HandCollapsedH);
                                _handAnimTimer.Stop();
                                return;
                            }
                            _handAnimCurrent += diff * 0.25;
                            YourHandRow.Height = new GridLength(_handAnimCurrent);
                        };
                        _handAnimTimer.Start();
                    }
                }
            };
        }

        private void TabletopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LogListBox.ItemsSource = _gameLog;
            LoadSettings();
            LoadPlaymats();
            ShowNewGameDialog();
        }

        // ================================================================
        // NEW GAME DIALOG
        // ================================================================
        private void ShowNewGameDialog()
        {
            var dlg = new NewGameDialog(_allOpenDecks) { Owner = this };
            if (dlg.ShowDialog() != true)
            {
                Close();
                return;
            }

            _yourPlayerName = dlg.Player1Name;
            _oppPlayerName = dlg.Player2Name;
            _yourLife = dlg.StartingLife;
            _oppLife = dlg.StartingLife;
            _yourPoison = _oppPoison = 0;
            _yourCmdDmg = _oppCmdDmg = 0;
            _turnCounter = 1;
            _currentPhase = 0;
            _mulliganCount = 0;
            _gameOverShown = false;
            _monarchHolder = _initiativeHolder = _ringBearer = "";
            _yourEnergy = _oppEnergy = _ringLevel = 0;
            _yourCmdLocation = CommanderLocation.CommandZone;
            _oppCmdLocation = CommanderLocation.CommandZone;

            YourNameLabel.Text = _yourPlayerName;
            OppNameLabel.Text = _oppPlayerName;
            UpdateMulliganDisplay();

            SetupPlayerLibrary(dlg.Player1Deck, isYour: true);
            SetupPlayerLibrary(dlg.Player2Deck, isYour: false);

            UpdateLifeDisplays();
            UpdatePhaseDisplay();
            UpdateZoneCounts();
            RenderLibrary(isYour: true);
            RenderLibrary(isYour: false);
            RenderHand(isYour: true);
            RenderHand(isYour: false);
            RenderCommander(isYour: true);
            RenderCommander(isYour: false);
            UpdateHandCounts();
            UpdateManaPoolSummary();
            UpdateModernMechanicsDisplay();

            SaveSetting(SavedGameKey, string.Empty);

            // Download missing card images in background
            _ = CacheCardImagesAsync(dlg.Player1Deck);
            _ = CacheCardImagesAsync(dlg.Player2Deck);

            _gameLog.Clear();
            AddLog("=== New Game Started ===");
            AddLog($"Player 1: {dlg.Player1Deck?.Name ?? "Unknown"}");
            AddLog($"Player 2: {dlg.Player2Deck?.Name ?? "Unknown"}");

            // Offer opening mulligan immediately (proper MTG flow)
            Dispatcher.BeginInvoke(new Action(OfferOpeningMulligan),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ── Opening mulligan — shown automatically after hand is dealt ──
        private void OfferOpeningMulligan()
        {
            _mulliganCount = 0;
            ShowMulliganWindow();
        }

        // Shows the MulliganWindow for the current hand without reshuffling first.
        // If the player mulligans again, DoMulligan handles the reshuffle + redraw.
        private void ShowMulliganWindow()
        {
            bool isCommander = _yourCommander != null;

            var dlg = new MulliganWindow(
                new List<DeckCard>(_yourHand),
                _yourLibrary,
                _mulliganCount,
                isCommander)
            { Owner = this };

            if (dlg.ShowDialog() != true) return;

            if (dlg.MulliganedAgain)
            {
                DoMulligan(); // reshuffle, draw 7, open window again
                return;
            }

            // Keep — apply put-back cards to bottom of library
            _yourHand.Clear();
            foreach (var card in dlg.FinalHand)
                _yourHand.Add(card);
            foreach (var card in dlg.BottomCards)
                _yourLibrary.Add(card);

            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        // ================================================================
        // IMAGE CACHING — download missing card images for tabletop use
        // ================================================================
        private static string GetImageCacheFolder() =>
            Services.AppFolderService.CardImagesFolder;

        private static string CardBackPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "MTG_Back.png");

        private static string ImageUnavailablePath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "image_unavailable.png");

        private async System.Threading.Tasks.Task CacheCardImagesAsync(Deck? deck)
        {
            if (deck == null) return;
            var mainFolder = AppFolderService.CardImagesFolder;
            var cacheFolder = GetImageCacheFolder();
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            foreach (var card in deck.Cards)
            {
                // Already have a valid local image
                if (!string.IsNullOrEmpty(card.LocalImagePath)
                    && System.IO.File.Exists(card.LocalImagePath))
                    continue;

                // Search main CardImages folder first (same naming as main app)
                string safeName = string.Concat(
                    card.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                string mainPath = System.IO.Path.Combine(mainFolder, $"{safeName}.jpg");
                if (System.IO.File.Exists(mainPath))
                {
                    card.LocalImagePath = mainPath;
                }
                else if (!string.IsNullOrEmpty(card.ImageNormalUrl))
                {
                    // Download from Scryfall
                    string cachePath = System.IO.Path.Combine(cacheFolder, $"{safeName}.jpg");
                    if (!System.IO.File.Exists(cachePath))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(card.ImageNormalUrl);
                            await System.IO.File.WriteAllBytesAsync(cachePath, bytes);
                        }
                        catch { continue; }
                    }
                    card.LocalImagePath = cachePath;
                }
                else continue;

                // Download back face if DFC
                if (!string.IsNullOrEmpty(card.ImageBackUrl))
                {
                    string backMain = System.IO.Path.Combine(mainFolder, $"{safeName}_back.jpg");
                    string backCache = System.IO.Path.Combine(cacheFolder, $"{safeName}_back.jpg");
                    string backPath = System.IO.File.Exists(backMain) ? backMain : backCache;

                    if (!System.IO.File.Exists(backPath))
                    {
                        try
                        {
                            var backBytes = await http.GetByteArrayAsync(card.ImageBackUrl);
                            await System.IO.File.WriteAllBytesAsync(backCache, backBytes);
                            backPath = backCache;
                        }
                        catch { backPath = ""; }
                    }
                    if (!string.IsNullOrEmpty(backPath) && System.IO.File.Exists(backPath))
                        card.LocalImageBackPath = backPath;
                }

                // Re-render on UI thread if this card is visible
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_yourHand.Any(c => c.Name == card.Name)
                        || _oppHand.Any(c => c.Name == card.Name))
                    {
                        RenderHand(isYour: true);
                        RenderHand(isYour: false);
                    }
                    if (_yourCommander?.Name == card.Name)
                        RenderCommander(isYour: true);
                    if (_oppCommander?.Name == card.Name)
                        RenderCommander(isYour: false);
                });
            }
        }

        // ================================================================
        // LIBRARY SETUP
        // ================================================================
        private void SetupPlayerLibrary(Deck? deck, bool isYour)
        {
            var library = isYour ? _yourLibrary : _oppLibrary;
            var hand = isYour ? _yourHand : _oppHand;
            library.Clear();
            hand.Clear();

            if (deck == null) return;

            // Separate commander
            DeckCard? commander = null;
            if (deck.DeckType == DeckType.Commander)
            {
                commander = deck.CommanderCards.FirstOrDefault();
                if (isYour)
                {
                    _yourCommander = commander;
                    _yourCmdTax = 0;
                    YourCmdTaxText.Text = "Tax: 0";
                }
                else
                {
                    _oppCommander = commander;
                    _oppCmdTax = 0;
                    OppCmdTaxText.Text = "Tax: 0";
                }
            }

            // Build library — expand quantities, exclude commander
            foreach (var card in deck.MainboardCards)
            {
                if (commander != null &&
                    card.Name == commander.Name) continue;
                // Add non-foil copies
                for (int i = 0; i < card.Quantity; i++)
                {
                    var clone = DeckService.CloneCard(card);
                    clone.IsFoil = false;
                    library.Add(clone);
                }
                // Add foil copies
                for (int i = 0; i < card.FoilQuantity; i++)
                {
                    var clone = DeckService.CloneCard(card);
                    clone.IsFoil = true;
                    library.Add(clone);
                }
            }

            // Shuffle
            Shuffle(library);

            // Deal opening hand (7 cards)
            int handSize = Math.Min(7, library.Count);
            for (int i = 0; i < handSize; i++)
            {
                hand.Add(library[0]);
                library.RemoveAt(0);
            }
        }

        private static void Shuffle(List<DeckCard> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = CryptoNext(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ================================================================
        // CARD DIMENSIONS (75% of standard 63x88mm at 96dpi)
        // ================================================================
        private const double CardW = 90;   // hand/commander width
        private const double CardH = 126;  // hand/commander height
        private const double FieldW = 90;   // battlefield untapped = same as hand
        private const double FieldH = 126;  // battlefield untapped height
        // Tapped = landscape: FieldH wide x FieldW tall (126 x 90)

        // ================================================================
        // RENDER LIBRARY — show card back stack with count
        // ================================================================
        private void RenderLibrary(bool isYour)
        {
            // Update the library card back image to show sleeve if set
            var img = isYour ? YourLibraryImage : OppLibraryImage;
            var sleeve = isYour ? _yourSleeveImage : _oppSleeveImage;
            if (sleeve != null)
                img.Source = sleeve;
            else
            {
                try
                {
                    img.Source = new BitmapImage(
                        new Uri("pack://application:,,,/Resources/Images/MTG_Back.png"));
                }
                catch { }
            }
            UpdateZoneCounts();
        }

        // ================================================================
        // RENDER HAND — draw cards in hand canvas
        // ================================================================
        private void RenderHand(bool isYour)
        {
            // Opponent hand is never rendered - only count shown in info bar
            if (!isYour)
            {
                UpdateHandCounts();
                return;
            }

            var canvas = YourHandCanvas;
            canvas.Children.Clear();
            if (_yourHand.Count == 0) return;

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW < 10) canvasW = 700;
            if (canvasH < 10) canvasH = 100;

            double spacing = Math.Min(CardW + 4,
                (canvasW - CardW) / Math.Max(1, _yourHand.Count - 1));
            double totalW = CardW + spacing * (_yourHand.Count - 1);
            double startX = (canvasW - totalW) / 2;
            double y = (canvasH - CardH) / 2;

            for (int i = 0; i < _yourHand.Count; i++)
            {
                var card = _yourHand[i];
                int idx = i;
                double x = startX + i * spacing;
                var visual = MakeCardVisual(card, isYour: true, isHand: true);
                WireHandCardPlay(visual, idx, isYour: true);
                // Single click = enlarge card view
                var capturedCard = card;
                visual.MouseLeftButtonDown += (s, e) =>
                {
                    ShowCardEnlarged(capturedCard);
                    e.Handled = true;
                };
                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                canvas.Children.Add(visual);
            }
            UpdateHandCounts();
        }

        // ================================================================
        // RENDER COMMANDER — show commander in command zone
        // ================================================================
        private void RenderCommander(bool isYour)
        {
            var canvas = isYour ? YourCommandCanvas : OppCommandCanvas;
            var commander = isYour ? _yourCommander : _oppCommander;
            canvas.Children.Clear();
            if (commander == null) return;

            var visual = MakeCardVisual(commander, isYour, isHand: false);
            visual.Width = CardW;
            visual.Height = CardH;

            // Left click = view enlarged
            var cap = commander;
            visual.MouseLeftButtonDown += (s, e) =>
            {
                ShowCardEnlarged(cap);
                e.Handled = true;
            };

            // Right click = commander context menu
            visual.MouseRightButtonDown += (s, e) =>
            {
                ShowCommanderContextMenu(cap, isYour, visual);
                e.Handled = true;
            };

            Canvas.SetLeft(visual, 2);
            Canvas.SetTop(visual, 2);
            canvas.Children.Add(visual);
        }

        // ================================================================
        // COMMANDER LOCATION TRACKING
        // ================================================================
        private void SetCommanderLocation(bool isYour, CommanderLocation loc)
        {
            if (isYour) _yourCmdLocation = loc;
            else _oppCmdLocation = loc;
            UpdateCmdZoneUI(isYour);
        }

        private void UpdateCmdZoneUI(bool isYour)
        {
            var loc = isYour ? _yourCmdLocation : _oppCmdLocation;
            int castCount = isYour ? _yourCmdTax : _oppCmdTax;
            int taxCost = castCount * 2;

            // Tax always visible — shows cast count and additional mana
            string taxStr = $"Cast: {castCount}x  (+{taxCost})";
            if (isYour) YourCmdTaxText.Text = taxStr;
            else OppCmdTaxText.Text = taxStr;

            // Return button — visible only when commander is away from CMD/Battlefield
            bool showReturn = loc != CommanderLocation.CommandZone
                           && loc != CommanderLocation.Battlefield;
            string locLabel = loc switch
            {
                CommanderLocation.Graveyard => "↩ Return (Grave)",
                CommanderLocation.Exile => "↩ Return (Exile)",
                CommanderLocation.Hand => "↩ Return (Hand)",
                CommanderLocation.Library => "↩ Return (Library)",
                _ => "↩ Return"
            };

            if (isYour)
            {
                BtnYourCmdReturn.Visibility = showReturn
                    ? Visibility.Visible : Visibility.Collapsed;
                BtnYourCmdReturn.Content = locLabel;
            }
            else
            {
                BtnOppCmdReturn.Visibility = showReturn
                    ? Visibility.Visible : Visibility.Collapsed;
                BtnOppCmdReturn.Content = locLabel;
            }
        }

        private void BtnYourCmdReturn_Click(object sender, RoutedEventArgs e)
            => ReturnCommanderToCommandZone(isYour: true);

        private void BtnOppCmdReturn_Click(object sender, RoutedEventArgs e)
            => ReturnCommanderToCommandZone(isYour: false);

        private void ReturnCommanderToCommandZone(bool isYour)
        {
            var loc = isYour ? _yourCmdLocation : _oppCmdLocation;
            var commander = isYour ? _yourCommander : _oppCommander;
            if (commander == null) return;

            // Remove from current zone
            switch (loc)
            {
                case CommanderLocation.Graveyard:
                    if (isYour) _yourGrave.Remove(commander);
                    else _oppGrave.Remove(commander);
                    break;
                case CommanderLocation.Exile:
                    if (isYour) _yourExile.Remove(commander);
                    else _oppExile.Remove(commander);
                    break;
                case CommanderLocation.Hand:
                    if (isYour) _yourHand.Remove(commander);
                    else _oppHand.Remove(commander);
                    RenderHand(isYour: true);
                    UpdateHandCounts();
                    break;
                case CommanderLocation.Library:
                    if (isYour) _yourLibrary.Remove(commander);
                    else _oppLibrary.Remove(commander);
                    break;
            }

            // Put back in command zone
            SetCommanderLocation(isYour, CommanderLocation.CommandZone);
            RenderCommander(isYour);
            UpdateZoneCounts();
        }

        // ================================================================
        // COMMANDER CONTEXT MENU
        // ================================================================
        private void ShowCommanderContextMenu(DeckCard commander,
            bool isYour, Border visual)
        {
            int tax = isYour ? _yourCmdTax : _oppCmdTax;
            int castCost = tax * 2;

            var menu = new ContextMenu();

            var castItem = new MenuItem
            {
                Header = $"⚔ Cast Commander (Tax: +{castCost} generic)"
            };
            castItem.Click += (s, e) => CastCommander(isYour);
            menu.Items.Add(castItem);
            menu.Items.Add(new Separator());

            var viewItem = new MenuItem { Header = "🔍 View Card" };
            var cmdCap = commander;
            viewItem.Click += (s, e) => ShowCardEnlarged(cmdCap);
            menu.Items.Add(viewItem);

            visual.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void CastCommander(bool isYour)
        {
            var commander = isYour ? _yourCommander : _oppCommander;
            if (commander == null) return;

            // Increment tax
            if (isYour) { _yourCmdTax++; YourCmdTaxText.Text = $"Tax: +{_yourCmdTax * 2}"; }
            else { _oppCmdTax++; OppCmdTaxText.Text = $"Tax: +{_oppCmdTax * 2}"; }

            // Place on battlefield
            var field = isYour ? _yourField : _oppField;
            var canvas = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;
            var existing = field.Where(c => !c.IsLandZone).ToList();
            var (x, y) = AutoPosition(canvas, existing.Count, FieldW, FieldH);

            var bc = new BattlefieldCard
            {
                Card = commander,
                X = x,
                Y = y,
                IsYour = isYour,
                IsLandZone = false
            };
            field.Add(bc);
            RenderBattlefieldCard(bc, canvas);

            // Remove from command zone
            // Keep commander reference — location enum tracks where it is
            (isYour ? YourCommandCanvas : OppCommandCanvas).Children.Clear();
            SetCommanderLocation(isYour, CommanderLocation.Battlefield);
        }

        // ================================================================
        // COMMANDER ZONE CHOICE PROMPT
        // ================================================================
        private void PromptCommanderZone(DeckCard card, bool isYour,
            string defaultDestination)
        {
            string destLabel = defaultDestination switch
            {
                "graveyard" => "Graveyard",
                "exile" => "Exile",
                "hand" => "Hand",
                "libtop" => "Library (Top)",
                "libbot" => "Library (Bottom)",
                _ => defaultDestination
            };

            string chosen = "command"; // default to command zone

            var win = new Window
            {
                Title = "Commander — Zone Choice",
                Width = 380,
                Height = 190,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = $"{card.Name} would go to {destLabel}.",
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "As the commander's owner, where would you like to send it?",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var btnCmd = new Button
            {
                Content = "⚔ Command Zone",
                Width = 145,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0x88)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC))
            };
            btnCmd.Click += (s, e) => { chosen = "command"; win.Close(); };

            var btnDest = new Button
            {
                Content = $"→ {destLabel}",
                Width = 130,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            };
            btnDest.Click += (s, e) => { chosen = defaultDestination; win.Close(); };

            btns.Children.Add(btnCmd);
            btns.Children.Add(btnDest);
            stack.Children.Add(btns);
            win.Content = stack;
            win.ShowDialog();

            ApplyCommanderZoneMove(card, isYour, chosen);
        }

        private void ApplyCommanderZoneMove(DeckCard card, bool isYour,
            string destination)
        {
            // Always keep the commander reference — location enum tracks where it is
            if (isYour) _yourCommander = card;
            else _oppCommander = card;

            switch (destination)
            {
                case "command":
                    SetCommanderLocation(isYour, CommanderLocation.CommandZone);
                    RenderCommander(isYour);
                    break;
                case "graveyard":
                    if (isYour) _yourGrave.Add(card); else _oppGrave.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Graveyard);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(card); else _oppExile.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Exile);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(card); else _oppHand.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Hand);
                    RenderHand(isYour: true); UpdateHandCounts();
                    break;
                case "libtop":
                    if (isYour) _yourLibrary.Insert(0, card);
                    else _oppLibrary.Insert(0, card);
                    SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
                case "libbot":
                    if (isYour) _yourLibrary.Add(card);
                    else _oppLibrary.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
            }
            UpdateZoneCounts();
        }


        // ================================================================
        // MAKE CARD VISUAL — image if available, card back otherwise
        // ================================================================
        private Border MakeCardVisual(DeckCard card, bool isYour,
            bool isHand, bool faceDown = false, bool isTransformed = false)
        {
            var border = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = card.Name
            };

            if (card.IsToken)
            {
                border.Child = MakeTokenFace(card);
            }
            else if (isTransformed && !string.IsNullOrEmpty(card.LocalImageBackPath)
                && System.IO.File.Exists(card.LocalImageBackPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImageBackPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    border.Child = new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Fill };
                }
                catch { border.Child = MakeTransformedFallback(card); }
            }
            else if (isTransformed)
            {
                border.Child = MakeTransformedFallback(card);
            }
            else if (faceDown || string.IsNullOrEmpty(card.LocalImagePath)
                || !System.IO.File.Exists(card.LocalImagePath))
            {
                // Try image_unavailable.png as fallback
                string fallback = ImageUnavailablePath;
                if (System.IO.File.Exists(fallback))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(fallback, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = (int)CardW;
                        bmp.EndInit();
                        border.Child = new System.Windows.Controls.Image
                        {
                            Source = bmp,
                            Stretch = Stretch.Fill
                        };
                    }
                    catch { border.Child = MakeCardBack(isYour); }
                }
                else
                    border.Child = MakeCardBack(isYour);
            }
            else
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    border.Child = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill
                    };
                }
                catch
                {
                    border.Child = MakeCardBack(isYour);
                }
            }

            // Cards always display upright regardless of which side
            // Wrap in Canvas to add foil/commander badges without affecting layout
            if (!faceDown && (card.IsFoil || card.IsCommander))
            {
                var canvas = new Canvas
                {
                    Width = CardW,
                    Height = CardH
                };
                Canvas.SetLeft(border, 0);
                Canvas.SetTop(border, 0);
                canvas.Children.Add(border);

                // Foil rainbow triangle — top right
                if (card.IsFoil)
                {
                    const double ts = 14;
                    var poly = new System.Windows.Shapes.Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(0, 0),
                            new Point(ts, 0),
                            new Point(ts, ts)
                        },
                        Fill = new LinearGradientBrush(
                            new GradientStopCollection
                            {
                                new GradientStop(Color.FromRgb(0xFF, 0x00, 0x80), 0.0),
                                new GradientStop(Color.FromRgb(0xFF, 0xA5, 0x00), 0.2),
                                new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.4),
                                new GradientStop(Color.FromRgb(0x00, 0xDD, 0x44), 0.6),
                                new GradientStop(Color.FromRgb(0x00, 0xAA, 0xFF), 0.8),
                                new GradientStop(Color.FromRgb(0xAA, 0x00, 0xFF), 1.0),
                            },
                            new Point(0, 0), new Point(1, 1)),
                        Opacity = 0.9,
                        IsHitTestVisible = false
                    };
                    Canvas.SetRight(poly, 0);
                    Canvas.SetTop(poly, 0);
                    canvas.Children.Add(poly);
                }

                // Commander star — bottom left
                if (card.IsCommander)
                {
                    var star = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 0x22, 0x11, 0x00)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(2, 1, 2, 1),
                        IsHitTestVisible = false,
                        Child = new TextBlock
                        {
                            Text = "⭐",
                            FontSize = 8,
                            Foreground = Brushes.Gold
                        }
                    };
                    Canvas.SetLeft(star, 2);
                    Canvas.SetBottom(star, 2);
                    canvas.Children.Add(star);
                }

                var wrapper = new Border
                {
                    Width = CardW,
                    Height = CardH,
                    Child = canvas
                };
                return wrapper;
            }

            return border;
        }

        // ================================================================
        // CARD BACK — drawn with WPF shapes, no external image needed
        // ================================================================

        private static Grid MakeTokenFace(DeckCard card)
        {
            var g = new Grid();

            // If card has an image, use it with TOKEN overlay
            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    g.Children.Add(new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Fill });
                }
                catch { g.Children.Add(MakeTokenColorBack(card)); }
            }
            else
                g.Children.Add(MakeTokenColorBack(card));

            // TOKEN watermark overlay
            g.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 18,
                Child = new TextBlock
                {
                    Text = "TOKEN",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });

            return g;
        }

        private static Grid MakeTokenColorBack(DeckCard card)
        {
            var bgColor = card.ColorIdentity switch
            {
                "W" => Color.FromRgb(0xF0, 0xF0, 0xD8),
                "U" => Color.FromRgb(0x10, 0x55, 0xAA),
                "B" => Color.FromRgb(0x18, 0x10, 0x22),
                "R" => Color.FromRgb(0xBB, 0x18, 0x18),
                "G" => Color.FromRgb(0x00, 0x66, 0x33),
                "M" => Color.FromRgb(0xAA, 0x88, 0x11),
                _ => Color.FromRgb(0x66, 0x66, 0x66)
            };
            var fgColor = card.ColorIdentity == "W" ? Colors.Black : Colors.White;

            var g = new Grid { Background = new SolidColorBrush(bgColor) };
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4)
            };
            stack.Children.Add(new TextBlock
            {
                Text = card.Name,
                Foreground = new SolidColorBrush(fgColor),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            if (!string.IsNullOrEmpty(card.Power))
                stack.Children.Add(new TextBlock
                {
                    Text = $"{card.Power}/{card.Toughness}",
                    Foreground = new SolidColorBrush(fgColor),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            g.Children.Add(stack);
            return g;
        }


        private static Grid MakeTransformedFallback(DeckCard card)
        {
            // Show card art with a blue "TRANSFORMED" overlay
            var g = new Grid();
            // Try original image with tint
            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    g.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill,
                        Opacity = 0.7
                    });
                }
                catch { }
            }
            g.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(120, 0, 80, 180)),
                Child = new TextBlock
                {
                    Text = "TRANSFORMED",
                    Foreground = Brushes.White,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            });
            return g;
        }

        private static Grid MakeCardBack(bool isYour = true)
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E))
            };

            // If a sleeve image is set for this player, use it
            var sleeve = isYour ? _yourSleeveImage : _oppSleeveImage;
            if (sleeve != null)
            {
                grid.Children.Add(new System.Windows.Controls.Image
                {
                    Source = sleeve,
                    Stretch = Stretch.Fill
                });
                return grid;
            }

            // Outer decorative border
            grid.Children.Add(new Border
            {
                Margin = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3)
            });

            // Inner border
            grid.Children.Add(new Border
            {
                Margin = new Thickness(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x60, 0x18)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(
                    Color.FromRgb(0x12, 0x1E, 0x4A))
            });

            // MTG logo text
            grid.Children.Add(new TextBlock
            {
                Text = "M",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7
            });

            return grid;
        }

        private bool _handExpanded = false;
        private const double HandCollapsedH = 32;
        private const double HandExpandedH = 160;
        private System.Windows.Threading.DispatcherTimer? _handAnimTimer;
        private double _handAnimTarget;
        private double _handAnimCurrent;

        private void YourHand_Click(object sender, MouseButtonEventArgs e)
        {
            _handExpanded = !_handExpanded;
            _handAnimTarget = _handExpanded ? HandExpandedH : HandCollapsedH;
            _handAnimCurrent = YourHandRow.Height.Value;

            HandPeekLabel.Visibility = _handExpanded
                ? Visibility.Collapsed : Visibility.Visible;

            _handAnimTimer?.Stop();
            _handAnimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _handAnimTimer.Tick += (s, _) =>
            {
                double diff = _handAnimTarget - _handAnimCurrent;
                if (Math.Abs(diff) < 0.5)
                {
                    YourHandRow.Height = new GridLength(_handAnimTarget);
                    _handAnimTimer.Stop();
                    if (_handExpanded) RenderHand(isYour: true);
                    return;
                }
                _handAnimCurrent += diff * 0.25; // ease out
                YourHandRow.Height = new GridLength(_handAnimCurrent);
            };
            _handAnimTimer.Start();
            e.Handled = true; // prevent window MouseDown collapsing immediately
        }

        private void UpdateHandCounts()
        {
            if (HandCountLabel != null)
                HandCountLabel.Text = $"({_yourHand.Count})";
            if (OppHandCountLabel != null)
                OppHandCountLabel.Text = _oppHand.Count.ToString();
        }

        private void YourHandCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 10) RenderHand(isYour: true);
        }

        private void OppHandCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Opponent hand is never rendered — count only shown in info bar
        }

        // ================================================================
        // ZONE COUNT UPDATES
        // ================================================================
        private void UpdateZoneCounts()
        {
            YourLibraryCount.Text = _yourLibrary.Count.ToString();
            OppLibraryCount.Text = _oppLibrary.Count.ToString();
            YourGraveyardCount.Text = _yourGrave.Count.ToString();
            OppGraveyardCount.Text = _oppGrave.Count.ToString();
            YourExileCount.Text = _yourExile.Count.ToString();
            OppExileCount.Text = _oppExile.Count.ToString();

            UpdateZoneTopCard(YourGraveTopBorder, YourGraveIcon, _yourGrave);
            UpdateZoneTopCard(OppGraveTopBorder, OppGraveIcon, _oppGrave);
            UpdateZoneTopCard(YourExileTopBorder, YourExileIcon, _yourExile);
            UpdateZoneTopCard(OppExileTopBorder, OppExileIcon, _oppExile);
        }

        private static void UpdateZoneTopCard(Border imgBorder,
            TextBlock icon, List<DeckCard> cards)
        {
            if (cards.Count == 0)
            {
                imgBorder.Visibility = Visibility.Collapsed;
                icon.Visibility = Visibility.Visible;
                return;
            }
            var top = cards[cards.Count - 1];
            if (!string.IsNullOrEmpty(top.LocalImagePath)
                && System.IO.File.Exists(top.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(top.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 44;
                    bmp.EndInit();
                    imgBorder.Child = new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Fill };
                    imgBorder.Visibility = Visibility.Visible;
                    icon.Visibility = Visibility.Collapsed;
                    return;
                }
                catch { }
            }
            imgBorder.Visibility = Visibility.Collapsed;
            icon.Visibility = Visibility.Visible;
        }


        // ================================================================
        // SAVE / RESTORE GAME STATE
        // ================================================================
        private void SaveGameState()
        {
            var state = new GameState
            {
                YourPlayerName = _yourPlayerName,
                OppPlayerName = _oppPlayerName,
                YourLife = _yourLife,
                OppLife = _oppLife,
                YourPoison = _yourPoison,
                OppPoison = _oppPoison,
                YourCmdDmg = _yourCmdDmg,
                OppCmdDmg = _oppCmdDmg,
                YourCmdTax = _yourCmdTax,
                OppCmdTax = _oppCmdTax,
                TurnCounter = _turnCounter,
                CurrentPhase = _currentPhase,
                YourLibrary = _yourLibrary.Select(c => c.Name).ToList(),
                OppLibrary = _oppLibrary.Select(c => c.Name).ToList(),
                YourHand = _yourHand.Select(c => c.Name).ToList(),
                OppHand = _oppHand.Select(c => c.Name).ToList(),
                YourGrave = _yourGrave.Select(c => c.Name).ToList(),
                OppGrave = _oppGrave.Select(c => c.Name).ToList(),
                YourExile = _yourExile.Select(c => c.Name).ToList(),
                OppExile = _oppExile.Select(c => c.Name).ToList(),
                // Modern mechanics
                MonarchHolder = _monarchHolder,
                InitiativeHolder = _initiativeHolder,
                YourEnergy = _yourEnergy,
                OppEnergy = _oppEnergy,
                RingLevel = _ringLevel,
                RingBearer = _ringBearer,
                // Battlefield — save card names, tap state, transform, counters, temp effects
                YourBattlefield = _yourField.Select(bc => new SavedBattlefieldCard
                {
                    CardName = bc.Card.Name,
                    IsTapped = bc.IsTapped,
                    IsTransformed = bc.IsTransformed,
                    IsFaceDown = bc.IsFaceDown,
                    IsLandZone = bc.IsLandZone,
                    X = bc.X,
                    Y = bc.Y,
                    Counters = new Dictionary<string, int>(bc.Counters),
                    TempEffects = bc.TempEffects.Select(ef =>
                        $"{ef.Label}|{ef.BonusPower}|{ef.BonusToughness}").ToList()
                }).ToList(),
                OppBattlefield = _oppField.Select(bc => new SavedBattlefieldCard
                {
                    CardName = bc.Card.Name,
                    IsTapped = bc.IsTapped,
                    IsTransformed = bc.IsTransformed,
                    IsFaceDown = bc.IsFaceDown,
                    IsLandZone = bc.IsLandZone,
                    X = bc.X,
                    Y = bc.Y,
                    Counters = new Dictionary<string, int>(bc.Counters),
                    TempEffects = bc.TempEffects.Select(ef =>
                        $"{ef.Label}|{ef.BonusPower}|{ef.BonusToughness}").ToList()
                }).ToList()
            };
            var json = JsonSerializer.Serialize(state);
            SaveSetting(SavedGameKey, json);
        }

        private void RestoreGameState()
        {
            var json = GetSetting(SavedGameKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var state = JsonSerializer.Deserialize<GameState>(json);
                if (state == null) return;

                _yourPlayerName = state.YourPlayerName;
                _oppPlayerName = state.OppPlayerName;
                _yourLife = state.YourLife;
                _oppLife = state.OppLife;
                _yourPoison = state.YourPoison;
                _oppPoison = state.OppPoison;
                _yourCmdDmg = state.YourCmdDmg;
                _oppCmdDmg = state.OppCmdDmg;
                _yourCmdTax = state.YourCmdTax;
                _oppCmdTax = state.OppCmdTax;
                _turnCounter = state.TurnCounter;
                _currentPhase = state.CurrentPhase;

                YourNameLabel.Text = _yourPlayerName;
                OppNameLabel.Text = _oppPlayerName;
                YourCmdTaxText.Text = $"Tax: {_yourCmdTax}";
                OppCmdTaxText.Text = $"Tax: {_oppCmdTax}";

                UpdateLifeDisplays();
                UpdatePhaseDisplay();
                UpdateZoneCounts();

                // Restore modern mechanics
                _monarchHolder = state.MonarchHolder;
                _initiativeHolder = state.InitiativeHolder;
                _yourEnergy = state.YourEnergy;
                _oppEnergy = state.OppEnergy;
                _ringLevel = state.RingLevel;
                _ringBearer = state.RingBearer;
                UpdateModernMechanicsDisplay();

                // Restore battlefield — match card names back to DeckCard objects
                var allCards = (_yourDeck?.Cards ?? new())
                    .Concat(_oppDeck?.Cards ?? new())
                    .ToList();

                DeckCard? FindCard(string name) =>
                    allCards.FirstOrDefault(c => c.Name == name);

                void RestoreField(List<SavedBattlefieldCard> saved,
                    List<BattlefieldCard> field, Canvas bfCanvas, Canvas landCanvas, bool isYour)
                {
                    field.Clear();
                    bfCanvas.Children.Clear();
                    landCanvas.Children.Clear();
                    foreach (var s in saved)
                    {
                        var card = FindCard(s.CardName);
                        if (card == null) continue;
                        var bc = new BattlefieldCard
                        {
                            Card = card,
                            IsYour = isYour,
                            IsTapped = s.IsTapped,
                            IsTransformed = s.IsTransformed,
                            IsFaceDown = s.IsFaceDown,
                            IsLandZone = s.IsLandZone,
                            X = s.X,
                            Y = s.Y,
                            Counters = new Dictionary<string, int>(s.Counters)
                        };
                        // Restore temp effects
                        foreach (var ef in s.TempEffects)
                        {
                            var parts = ef.Split('|');
                            if (parts.Length == 3 &&
                                int.TryParse(parts[1], out int p) &&
                                int.TryParse(parts[2], out int t))
                                bc.TempEffects.Add(new TempEffect
                                { Label = parts[0], BonusPower = p, BonusToughness = t });
                        }
                        field.Add(bc);
                        var canvas = s.IsLandZone ? landCanvas : bfCanvas;
                        RenderBattlefieldCard(bc, canvas);
                    }
                }

                RestoreField(state.YourBattlefield, _yourField,
                    YourBattlefieldCanvas, YourLandCanvas, isYour: true);
                RestoreField(state.OppBattlefield, _oppField,
                    OppBattlefieldCanvas, OppLandCanvas, isYour: false);

                MessageBox.Show("Game restored successfully.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("Could not restore saved game.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ================================================================
        // PLAYMAT — PERSISTENCE
        // ================================================================
        private const string YourPlaymatKey = "Tabletop_YourPlaymat";
        private const string OppPlaymatKey = "Tabletop_OppPlaymat";
        private const string YourSleeveKey = "Tabletop_YourSleeve";
        private const string OppSleeveKey = "Tabletop_OppSleeve";

        // ── Persistent settings keys ──────────────────────────────────────────
        private const string SettingDefaultLife = "Tabletop_DefaultLife";
        private const string SettingAutoDraw = "Tabletop_AutoDraw";
        private const string SettingAutoEmptyMana = "Tabletop_AutoEmptyMana";
        private const string SettingShowGameOver = "Tabletop_ShowGameOver";
        private const string SettingBlurOppHand = "Tabletop_BlurOppHand";
        private const string SettingTableColor = "Tabletop_TableColor";

        // ── Runtime settings (loaded on init, applied immediately) ────────────
        private int _settingDefaultLife = 20;
        private bool _settingAutoDraw = true;
        private bool _settingAutoEmptyMana = true;
        private bool _settingShowGameOver = true;
        private bool _settingBlurOppHand = true;
        private string _settingTableColor = "Green";

        // Cached sleeve image — null means use the default MTG_Back drawn card back
        private static System.Windows.Media.ImageSource? _yourSleeveImage = null;
        private static System.Windows.Media.ImageSource? _oppSleeveImage = null;

        private static string? GetSetting(string key)
        {
            try
            {
                using var db = new Data.AppDbContext();
                return db.AppSettings.FirstOrDefault(s => s.Key == key)?.Value;
            }
            catch { return null; }
        }

        private static void SaveSetting(string key, string value)
        {
            try
            {
                using var db = new Data.AppDbContext();
                var s = db.AppSettings.FirstOrDefault(s => s.Key == key);
                if (s == null)
                    db.AppSettings.Add(
                        new Models.AppSetting { Key = key, Value = value });
                else
                    s.Value = value;
                db.SaveChanges();
            }
            catch { }
        }

        private void LoadPlaymats()
        {
            var yourPath = GetSetting(YourPlaymatKey);
            var oppPath = GetSetting(OppPlaymatKey);
            var yourSleevePath = GetSetting(YourSleeveKey);
            var oppSleevePath = GetSetting(OppSleeveKey);
            if (!string.IsNullOrEmpty(yourPath) && System.IO.File.Exists(yourPath))
                SetPlaymat(YourPlaymatImage, yourPath);
            if (!string.IsNullOrEmpty(oppPath) && System.IO.File.Exists(oppPath))
                SetPlaymat(OppPlaymatImage, oppPath);
            LoadSleeveImage(yourSleevePath, isYour: true);
            LoadSleeveImage(oppSleevePath, isYour: false);
        }

        private static void LoadSleeveImage(string? path, bool isYour)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                if (isYour) _yourSleeveImage = null;
                else _oppSleeveImage = null;
                return;
            }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                if (isYour) _yourSleeveImage = bmp;
                else _oppSleeveImage = bmp;
            }
            catch
            {
                if (isYour) _yourSleeveImage = null;
                else _oppSleeveImage = null;
            }
        }

        private static void SetPlaymat(System.Windows.Controls.Image img, string path)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                img.Source = bmp;
            }
            catch { img.Source = null; }
        }

        private void YourPlaymat_RightClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
            => ShowPlaymatMenu(YourPlaymatImage, YourPlaymatKey);

        private void OppPlaymat_RightClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
            => ShowPlaymatMenu(OppPlaymatImage, OppPlaymatKey);

        private void ShowPlaymatMenu(
            System.Windows.Controls.Image img, string settingKey)
        {
            var menu = new ContextMenu();

            var upload = new MenuItem { Header = "📁 Select Playmat Image..." };
            upload.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Playmat Image",
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                    InitialDirectory = Services.AppFolderService.PlaymatImagesFolder
                };
                if (dlg.ShowDialog() != true) return;
                var path = CopyToFolder(dlg.FileName,
                    Services.AppFolderService.PlaymatImagesFolder);
                SetPlaymat(img, path);
                SaveSetting(settingKey, path);
            };

            var clear = new MenuItem { Header = "✕ Clear Playmat" };
            clear.Click += (s, e) =>
            {
                img.Source = null;
                SaveSetting(settingKey, string.Empty);
            };

            var openFolder = new MenuItem { Header = "📂 Open Playmats Folder" };
            openFolder.Click += (s, e) =>
                System.Diagnostics.Process.Start("explorer.exe",
                    Services.AppFolderService.PlaymatImagesFolder);

            menu.Items.Add(upload);
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());
            menu.Items.Add(openFolder);
            menu.IsOpen = true;
        }

        // ── Browse for any image, convert to PNG, save to target folder ──────
        private static string? BrowseAndSaveTabletopImage(string title, string targetFolder)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp;*.ico;*.wmp|All Files|*.*"
            };
            if (dlg.ShowDialog() != true) return null;

            string src = dlg.FileName;
            string destName = System.IO.Path.GetFileNameWithoutExtension(src) + ".png";
            string dest = System.IO.Path.Combine(targetFolder, destName);

            try
            {
                // Load via WPF BitmapImage (handles jpg, png, bmp, gif, tiff, ico, wmp)
                // then encode as PNG for consistent storage
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(src, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                using var stream = System.IO.File.Create(dest);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                encoder.Save(stream);

                return dest;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load image:\n{ex.Message}",
                    "Image Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        // ── Copy an image to a target folder, converting to PNG ────────────
        private static string CopyToFolder(string sourcePath, string targetFolder)
        {
            string destName = System.IO.Path.GetFileNameWithoutExtension(sourcePath) + ".png";
            string dest = System.IO.Path.Combine(targetFolder, destName);

            // If already in the target folder, just return the path
            if (string.Equals(System.IO.Path.GetDirectoryName(sourcePath),
                targetFolder, StringComparison.OrdinalIgnoreCase))
                return sourcePath;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(sourcePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                using var stream = System.IO.File.Create(dest);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                encoder.Save(stream);
                return dest;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load image:\n{ex.Message}",
                    "Image Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return sourcePath;
            }
        }

        // ================================================================
        // LIFE TOTALS
        // ================================================================
        // ── Click-to-edit life/poison totals ─────────────────────────────────
        private void YourLifeText_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            var dlg = new InputDialog("Set Life Total", $"Enter new life total (current: {_yourLife}):");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;
            if (int.TryParse(dlg.Value.Trim(), out int val))
            {
                _yourLife = val;
                _gameOverShown = false;
                UpdateLifeDisplays();
            }
        }

        private void OppLifeText_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            var dlg = new InputDialog("Set Opponent Life Total", $"Enter new life total (current: {_oppLife}):");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;
            if (int.TryParse(dlg.Value.Trim(), out int val))
            {
                _oppLife = val;
                _gameOverShown = false;
                UpdateLifeDisplays();
            }
        }

        private void YourPoisonText_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            var dlg = new InputDialog("Set Poison Counters", $"Enter poison count (current: {_yourPoison}):");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;
            if (int.TryParse(dlg.Value.Trim(), out int val) && val >= 0)
            { _yourPoison = val; UpdateLifeDisplays(); }
        }

        private void OppPoisonText_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            var dlg = new InputDialog("Set Opponent Poison", $"Enter poison count (current: {_oppPoison}):");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;
            if (int.TryParse(dlg.Value.Trim(), out int val) && val >= 0)
            { _oppPoison = val; UpdateLifeDisplays(); }
        }

        // ── Draw X cards ─────────────────────────────────────────────────────
        private void BtnDrawCard_RightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            void AddDraw(string label, int count)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (s, ev) => DrawCards(count);
                menu.Items.Add(mi);
            }
            AddDraw("Draw 2", 2);
            AddDraw("Draw 3", 3);
            AddDraw("Draw 4", 4);
            AddDraw("Draw 5", 5);
            AddDraw("Draw 7 (new hand)", 7);
            menu.Items.Add(new Separator());
            var custom = new MenuItem { Header = "Draw X..." };
            custom.Click += (s, ev) =>
            {
                var dlg = new InputDialog("Draw Cards", "How many cards to draw?");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true && int.TryParse(dlg.Value, out int n) && n > 0)
                    DrawCards(n);
            };
            menu.Items.Add(custom);
            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void DrawCards(int count)
        {
            int drawn = 0;
            for (int i = 0; i < count && _yourLibrary.Count > 0; i++)
            {
                _yourHand.Add(_yourLibrary[0]);
                _yourLibrary.RemoveAt(0);
                drawn++;
            }
            if (drawn < count)
                MessageBox.Show($"Drew {drawn} card(s) — library empty.",
                    "Draw", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        // ── Modern Mechanics ──────────────────────────────────────────────────
        private static readonly string[] RingTempts =
        {
            "—",
            "Ring Bearer is legendary, can't be blocked by multiple",
            "Ring Bearer can't be blocked by creatures with power 2+",
            "Ring Bearer has Ward 1",
            "Ring Bearer has deathtouch, lifelink"
        };

        private void UpdateModernMechanicsDisplay()
        {
            // Monarch
            MonarchText.Text = _monarchHolder == "" ? "—"
                             : _monarchHolder == "you" ? "You"
                             : "Opp";
            MonarchPanel.Background = _monarchHolder == "" ?
                new SolidColorBrush(Color.FromArgb(0x44, 0xAA, 0x66, 0x22)) :
                new SolidColorBrush(Color.FromArgb(0xAA, 0xCC, 0xAA, 0x00));

            // Initiative
            InitiativeText.Text = _initiativeHolder == "" ? "—"
                                : _initiativeHolder == "you" ? "You"
                                : "Opp";
            InitiativePanel.Background = _initiativeHolder == "" ?
                new SolidColorBrush(Color.FromArgb(0x44, 0xAA, 0x44, 0x22)) :
                new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0x88, 0x00));

            // Energy
            EnergyText.Text = _yourEnergy.ToString();

            // Ring
            RingText.Text = _ringLevel == 0 ? "—" : $"Lvl {_ringLevel}";
            RingPanel.Background = _ringLevel == 0 ?
                new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x66, 0x88)) :
                new SolidColorBrush(Color.FromArgb(0x88, 0xBB, 0x44, 0xBB));
        }

        private void Monarch_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string h, Action a)
            { var mi = new MenuItem { Header = h }; mi.Click += (s, ev) => a(); menu.Items.Add(mi); }

            Add("👑 You take the Monarch", () => { _monarchHolder = "you"; UpdateModernMechanicsDisplay(); });
            Add("👑 Opponent takes Monarch", () => { _monarchHolder = "opp"; UpdateModernMechanicsDisplay(); });
            Add("Remove Monarch", () => { _monarchHolder = ""; UpdateModernMechanicsDisplay(); });
            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void Initiative_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string h, Action a)
            { var mi = new MenuItem { Header = h }; mi.Click += (s, ev) => a(); menu.Items.Add(mi); }

            Add("⚡ You take the Initiative", () => { _initiativeHolder = "you"; UpdateModernMechanicsDisplay(); });
            Add("⚡ Opponent takes Initiative", () => { _initiativeHolder = "opp"; UpdateModernMechanicsDisplay(); });
            Add("Remove Initiative", () => { _initiativeHolder = ""; UpdateModernMechanicsDisplay(); });
            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void Energy_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string h, Action a)
            { var mi = new MenuItem { Header = h }; mi.Click += (s, ev) => a(); menu.Items.Add(mi); }

            Add($"⚡ Your Energy: {_yourEnergy}  (+1)", () => { _yourEnergy++; UpdateModernMechanicsDisplay(); });
            Add($"⚡ Your Energy: {_yourEnergy}  (-1)", () => { if (_yourEnergy > 0) _yourEnergy--; UpdateModernMechanicsDisplay(); });
            menu.Items.Add(new Separator());
            Add($"⚡ Opp Energy: {_oppEnergy}   (+1)", () => { _oppEnergy++; UpdateModernMechanicsDisplay(); });
            Add($"⚡ Opp Energy: {_oppEnergy}   (-1)", () => { if (_oppEnergy > 0) _oppEnergy--; UpdateModernMechanicsDisplay(); });
            menu.Items.Add(new Separator());
            Add("Set Your Energy...", () =>
            {
                var dlg = new InputDialog("Energy", $"Your energy (current: {_yourEnergy}):");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true && int.TryParse(dlg.Value, out int v) && v >= 0)
                { _yourEnergy = v; UpdateModernMechanicsDisplay(); }
            });
            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void Ring_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string h, Action a)
            { var mi = new MenuItem { Header = h }; mi.Click += (s, ev) => a(); menu.Items.Add(mi); }

            // Current level description
            if (_ringLevel > 0)
                menu.Items.Add(new MenuItem
                {
                    Header = $"Level {_ringLevel}: {RingTempts[_ringLevel]}",
                    IsEnabled = false
                });

            menu.Items.Add(new Separator());
            if (_ringLevel < 4)
                Add("💍 Tempt with the Ring (advance)", () =>
                {
                    _ringLevel++;
                    if (string.IsNullOrEmpty(_ringBearer))
                    {
                        var dlg = new InputDialog("The Ring", "Name of your Ring-bearer:");
                        dlg.Owner = this;
                        if (dlg.ShowDialog() == true)
                            _ringBearer = dlg.Value.Trim();
                    }
                    UpdateModernMechanicsDisplay();
                    MessageBox.Show(
                        $"Ring level {_ringLevel}:\n{RingTempts[_ringLevel]}\n\nBearer: {(_ringBearer == "" ? "None" : _ringBearer)}",
                        "The Ring Tempts You", MessageBoxButton.OK, MessageBoxImage.Information);
                });

            Add("Change Ring-bearer...", () =>
            {
                var dlg = new InputDialog("Ring-bearer", $"Current: {(_ringBearer == "" ? "None" : _ringBearer)}");
                dlg.Owner = this;
                if (dlg.ShowDialog() == true) { _ringBearer = dlg.Value.Trim(); UpdateModernMechanicsDisplay(); }
            });
            Add("Remove The Ring", () => { _ringLevel = 0; _ringBearer = ""; UpdateModernMechanicsDisplay(); });

            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void YourLifeUp_Click(object sender, RoutedEventArgs e)
        {
            _yourLife++;
            AddLog($"Your life: {_yourLife} (+1)");
            UpdateLifeDisplays();
        }

        private void YourLifeDown_Click(object sender, RoutedEventArgs e)
        {
            _yourLife--;
            AddLog($"Your life: {_yourLife} (-1)");
            UpdateLifeDisplays();
        }

        private void OppLifeUp_Click(object sender, RoutedEventArgs e)
        {
            _oppLife++;
            AddLog($"Opponent life: {_oppLife} (+1)");
            UpdateLifeDisplays();
        }

        private void OppLifeDown_Click(object sender, RoutedEventArgs e)
        {
            _oppLife--;
            AddLog($"Opponent life: {_oppLife} (-1)");
            UpdateLifeDisplays();
        }

        private void YourPoisonUp_Click(object sender, RoutedEventArgs e)
        {
            _yourPoison++;
            UpdateLifeDisplays();
            if (_yourPoison >= 10)
                ShowGameOver("You have 10 or more poison counters — you lose!");
        }

        private void YourPoisonDown_Click(object sender, RoutedEventArgs e)
        {
            if (_yourPoison > 0) _yourPoison--;
            UpdateLifeDisplays();
        }

        private void OppPoisonUp_Click(object sender, RoutedEventArgs e)
        {
            _oppPoison++;
            UpdateLifeDisplays();
            if (_oppPoison >= 10)
                ShowGameOver("Opponent has 10 or more poison counters — they lose!");
        }

        private void OppPoisonDown_Click(object sender, RoutedEventArgs e)
        {
            if (_oppPoison > 0) _oppPoison--;
            UpdateLifeDisplays();
        }

        private void OppCmdDmgUp_Click(object sender, RoutedEventArgs e)
        {
            _oppCmdDmg++;
            UpdateLifeDisplays();
            if (_oppCmdDmg >= 21)
                ShowGameOver($"Opponent has taken 21+ commander damage from your commander — they lose!");
        }

        private void OppCmdDmgDown_Click(object sender, RoutedEventArgs e)
        {
            if (_oppCmdDmg > 0) _oppCmdDmg--;
            UpdateLifeDisplays();
        }

        private void YourCmdDmgUp_Click(object sender, RoutedEventArgs e)
        {
            _yourCmdDmg++;
            UpdateLifeDisplays();
            if (_yourCmdDmg >= 21)
                ShowGameOver($"You have taken 21+ commander damage from the opponent's commander — you lose!");
        }

        private void YourCmdDmgDown_Click(object sender, RoutedEventArgs e)
        {
            if (_yourCmdDmg > 0) _yourCmdDmg--;
            UpdateLifeDisplays();
        }

        private void UpdateLifeDisplays()
        {
            YourLifeText.Text = _yourLife.ToString();
            OppLifeText.Text = _oppLife.ToString();
            YourPoisonText.Text = _yourPoison.ToString();
            OppPoisonText.Text = _oppPoison.ToString();
            OppCmdDmgText.Text = _oppCmdDmg.ToString();
            YourCmdDmgText.Text = _yourCmdDmg.ToString();

            // Color life red when at or below 0
            YourLifeText.Foreground = _yourLife <= 0
                ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
            OppLifeText.Foreground = _oppLife <= 0
                ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

            // Life-hits-zero prompt (only trigger exactly at 0, not repeatedly below)
            if (_yourLife == 0)
                ShowGameOver("Your life total has reached 0 — you lose!");
            else if (_oppLife == 0)
                ShowGameOver("Opponent's life total has reached 0 — they lose!");
        }

        private bool _gameOverShown = false;

        private void ShowGameOver(string message)
        {
            if (_gameOverShown) return;
            _gameOverShown = true;

            if (!_settingShowGameOver)
            {
                // Just color the life text red — no popup
                _gameOverShown = false;
                return;
            }

            var result = MessageBox.Show(
                message + "\n\nStart a new game?",
                "Game Over",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.No);
            if (result == MessageBoxResult.Yes)
                BtnNewGame_Click(this, new RoutedEventArgs());
            else
                _gameOverShown = false;
        }

        // ================================================================
        // ================================================================
        // ================================================================
        // SWITCH SEATS
        // ================================================================
        // ================================================================
        // SWITCH SEATS -- swap all data, re-render from data, view stays fixed
        // ================================================================
        private void BtnRotateTable_Click(object sender, RoutedEventArgs e)
        {
            _tableRotated = !_tableRotated;

            // Swap all data lists
            (_yourLibrary, _oppLibrary) = (_oppLibrary, _yourLibrary);
            (_yourHand, _oppHand) = (_oppHand, _yourHand);
            (_yourGrave, _oppGrave) = (_oppGrave, _yourGrave);
            (_yourExile, _oppExile) = (_oppExile, _yourExile);
            (_yourBattlefield, _oppBattlefield) = (_oppBattlefield, _yourBattlefield);
            (_yourField, _oppField) = (_oppField, _yourField);
            (_yourCommander, _oppCommander) = (_oppCommander, _yourCommander);

            // Swap all counters
            (_yourLife, _oppLife) = (_oppLife, _yourLife);
            (_yourPoison, _oppPoison) = (_oppPoison, _yourPoison);
            (_yourCmdDmg, _oppCmdDmg) = (_oppCmdDmg, _yourCmdDmg);
            (_yourCmdTax, _oppCmdTax) = (_oppCmdTax, _yourCmdTax);

            // Swap player names
            (_yourPlayerName, _oppPlayerName) = (_oppPlayerName, _yourPlayerName);

            // Swap playmat paths and reload both images from new paths
            var yourPath = GetSetting(YourPlaymatKey) ?? string.Empty;
            var oppPath = GetSetting(OppPlaymatKey) ?? string.Empty;
            SaveSetting(YourPlaymatKey, oppPath);
            SaveSetting(OppPlaymatKey, yourPath);
            LoadPlaymats();

            // Update all labels from swapped data
            YourNameLabel.Text = _yourPlayerName;
            OppNameLabel.Text = _oppPlayerName;
            YourCmdTaxText.Text = $"Tax: {_yourCmdTax}";
            OppCmdTaxText.Text = $"Tax: {_oppCmdTax}";

            // Re-render everything from data -- no canvas swapping
            UpdateLifeDisplays();
            UpdateZoneCounts();
            UpdateHandCounts();

            // Clear all canvases and re-render from swapped data
            YourHandCanvas.Children.Clear();
            OppHandCanvas.Children.Clear();
            YourCommandCanvas.Children.Clear();
            OppCommandCanvas.Children.Clear();
            YourBattlefieldCanvas.Children.Clear();
            OppBattlefieldCanvas.Children.Clear();
            YourLandCanvas.Children.Clear();
            OppLandCanvas.Children.Clear();
            // Reset all visual references before redraw
            foreach (var bc in _yourField.Concat(_oppField)) bc.Visual = null;

            RenderHand(isYour: true);
            RenderCommander(isYour: true);
            RenderCommander(isYour: false);
            RedrawBattlefield(isYour: true);
            RedrawBattlefield(isYour: false);
        }

        // ================================================================
        // TOKEN COPY — create a token that is a copy of another card
        // ================================================================
        private void CreateTokenCopy(DeckCard source, bool isYour = true)
        {
            var token = new DeckCard
            {
                Name = $"{source.Name} (Token Copy)",
                Power = source.Power,
                Toughness = source.Toughness,
                TypeLine = source.TypeLine,
                OracleText = source.OracleText,
                ColorIdentity = source.ColorIdentity,
                LocalImagePath = source.LocalImagePath,
                ImageNormalUrl = source.ImageNormalUrl,
                IsToken = true,
                Quantity = 1
            };

            var field = isYour ? _yourField : _oppField;
            var canvas = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;
            var existing = field.Where(c => !c.IsLandZone).ToList();
            var bc = new BattlefieldCard
            {
                Card = token,
                IsYour = isYour,
                IsLandZone = false,
                X = AutoPosition(canvas, existing.Count, FieldW, FieldH).x,
                Y = AutoPosition(canvas, existing.Count, FieldW, FieldH).y
            };
            field.Add(bc);
            RenderBattlefieldCard(bc, canvas);
            UpdateZoneCounts();
        }
        private readonly List<DeckCard> _stackCards = new();

        private void BtnClearStack_Click(object sender, RoutedEventArgs e)
        {
            if (_stackCards.Count == 0) return;
            var result = MessageBox.Show(
                $"Clear all {_stackCards.Count} card(s) from the stack?",
                "Clear Stack", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _stackCards.Clear();
            RenderStack();
        }

        private void StackCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => RenderStack();

        private void AddToStack(DeckCard card)
        {
            _stackCards.Insert(0, card); // new items go on top (index 0)
            AddLog($"{card.Name} → stack");
            RenderStack();
            // Show stack if hidden
            if (!_stackVisible)
            {
                _stackVisible = true;
                StackPanel.Visibility = Visibility.Visible;
                BtnToggleStack.Content = "STACK ▲";
            }
        }

        private void RenderStack()
        {
            StackCanvas.Children.Clear();
            StackCountLabel.Text = _stackCards.Count.ToString();

            double x = 8;
            double y = 4;
            double cardSpacing = CardW + 8;

            for (int i = 0; i < _stackCards.Count; i++)
            {
                var card = _stackCards[i];
                int idx = i;
                var border = MakeCardVisual(card, isYour: true, isHand: false);
                border.Width = CardW;
                border.Height = CardH;

                // Order label — "1st" on top
                var orderBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 1, 3, 1),
                    Child = new TextBlock
                    {
                        Text = $"{i + 1}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    }
                };

                // Left click = view enlarged
                border.MouseLeftButtonDown += (s, e) =>
                {
                    ShowCardEnlarged(card);
                    e.Handled = true;
                };

                // Right click = stack context menu
                border.MouseRightButtonDown += (s, e) =>
                {
                    ShowStackContextMenu(card, idx, border);
                    e.Handled = true;
                };

                Canvas.SetLeft(border, x);
                Canvas.SetTop(border, y);
                Panel.SetZIndex(border, 5);
                StackCanvas.Children.Add(border);

                Canvas.SetLeft(orderBadge, x + 2);
                Canvas.SetTop(orderBadge, y + 2);
                Panel.SetZIndex(orderBadge, 10);
                StackCanvas.Children.Add(orderBadge);

                x += cardSpacing;
            }
        }

        private void ShowStackContextMenu(DeckCard card, int idx, Border border)
        {
            var menu = new ContextMenu();

            void Add(string header, Action act)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (s, e) => act();
                menu.Items.Add(mi);
            }

            // Resolve — route based on card type
            Add("▶ Resolve", () => ResolveStackCard(card, idx));

            Add("↩ Counter (→ Graveyard)", () =>
            {
                _stackCards.RemoveAt(idx);
                _yourGrave.Add(card);
                AddLog($"{card.Name} countered → graveyard");
                UpdateZoneCounts();
                RenderStack();
            });

            menu.Items.Add(new Separator());

            Add("🤚 Return to Hand", () =>
            {
                _stackCards.RemoveAt(idx);
                _yourHand.Add(card);
                UpdateHandCounts();
                RenderHand(isYour: true);
                RenderStack();
            });

            menu.Items.Add(new Separator());
            Add("🔍 View Card", () => ShowCardEnlarged(card));

            border.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ResolveStackCard(DeckCard card, int idx)
        {
            _stackCards.RemoveAt(idx);

            if (card.IsPermanent)
            {
                // Offer destination choice for permanents
                var win = new Window
                {
                    Title = $"Resolve — {card.Name}",
                    Width = 380,
                    Height = 180,
                    WindowStyle = WindowStyle.ToolWindow,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
                };

                var stack = new StackPanel { Margin = new Thickness(16) };
                stack.Children.Add(new TextBlock
                {
                    Text = $"{card.Name} resolves. Where does it go?",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 14)
                });

                string chosen = "battlefield";
                var btns = new WrapPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                void MakeBtn(string label, string dest, string color)
                {
                    var btn = new Button
                    {
                        Content = label,
                        Height = 30,
                        Margin = new Thickness(4),
                        Padding = new Thickness(8, 0, 8, 0),
                        Background = new SolidColorBrush(
                            (Color)ColorConverter.ConvertFromString(color)),
                        Foreground = Brushes.White,
                        BorderBrush = Brushes.Transparent
                    };
                    btn.Click += (s, e) => { chosen = dest; win.Close(); };
                    btns.Children.Add(btn);
                }

                MakeBtn("▶ Battlefield", "battlefield", "#225522");
                if (card.IsLand)
                    MakeBtn("◈ Mana Zone", "manazone", "#334433");
                MakeBtn("🤚 Hand", "hand", "#224455");
                MakeBtn("💀 Graveyard", "graveyard", "#443322");

                stack.Children.Add(btns);
                win.Content = stack;
                win.ShowDialog();

                switch (chosen)
                {
                    case "battlefield":
                        var field = _yourField;
                        var canvas = YourBattlefieldCanvas;
                        var bc = new BattlefieldCard
                        {
                            Card = card,
                            IsYour = true,
                            IsLandZone = false,
                            X = 20 + field.Count(c => !c.IsLandZone) * (FieldW + 6),
                            Y = 10
                        };
                        field.Add(bc);
                        RenderBattlefieldCard(bc, canvas);
                        // Commander tracking
                        if (_yourCommander?.Name == card.Name)
                            SetCommanderLocation(true, CommanderLocation.Battlefield);
                        break;
                    case "manazone":
                        var fieldMz = _yourField;
                        var canvasMz = YourLandCanvas;
                        var bcMz = new BattlefieldCard
                        {
                            Card = card,
                            IsYour = true,
                            IsLandZone = true,
                            X = 0,
                            Y = 0
                        };
                        fieldMz.Add(bcMz);
                        RelayoutLandZone(isYour: true);
                        break;
                    case "hand":
                        _yourHand.Add(card);
                        RenderHand(isYour: true);
                        UpdateHandCounts();
                        break;
                    case "graveyard":
                        _yourGrave.Add(card);
                        break;
                }
            }
            else
            {
                // Instants, sorceries → always graveyard
                _yourGrave.Add(card);
                AddLog($"{card.Name} resolved → graveyard");
            }

            UpdateZoneCounts();
            RenderStack();
        }

        // ================================================================
        // PHASE 7 — PHASE TRACKER
        // ================================================================
        private void Phase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? string.Empty;
            int newPhase = Array.IndexOf(PhaseNames, tag);

            // Auto-empty mana pool when moving away from a main phase
            bool wasMain = _currentPhase == 3 || _currentPhase == 5; // Main1=3, Main2=5
            bool goingToNonMain = newPhase != 3 && newPhase != 5;
            if (wasMain && goingToNonMain)
                EmptyManaPool();

            _currentPhase = newPhase;
            UpdatePhaseDisplay();
        }

        private void UpdatePhaseDisplay()
        {
            Button[] phaseButtons =
            {
                PhaseUntap, PhaseUpkeep, PhaseDraw,
                PhaseMain1, PhaseCombat, PhaseMain2, PhaseEnd
            };
            for (int i = 0; i < phaseButtons.Length; i++)
                phaseButtons[i].Style = i == _currentPhase
                    ? (Style)FindResource("ActivePhaseButtonStyle")
                    : (Style)FindResource("PhaseButtonStyle");
            TurnCounterText.Text = _turnCounter.ToString();
        }

        private void BtnPassTurn_Click(object sender, RoutedEventArgs e)
        {
            // Clear "until end of turn" effects from all your battlefield cards
            foreach (var bc in _yourField.Concat(_oppField))
            {
                if (bc.TempEffects.Count > 0)
                {
                    bc.TempEffects.Clear();
                    RenderBattlefieldCard(bc, GetCardCanvas(bc));
                }
            }

            // Untap all your permanents
            UntapAllYour();

            // Advance to next turn
            _currentPhase = 0;
            _turnCounter++;
            UpdatePhaseDisplay();
            AddLog($"— Turn {_turnCounter} —");

            // Auto-draw (respects setting)
            if (_settingAutoDraw)
                DrawCard();
        }

        // ================================================================
        // PHASE 4 — DRAW & MULLIGAN
        // ================================================================
        private void BtnDrawCard_Click(object sender, RoutedEventArgs e)
            => DrawCard();

        private void DrawCard()
        {
            if (_yourLibrary.Count == 0)
            {
                MessageBox.Show("Your library is empty!",
                    "Draw", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _yourHand.Add(_yourLibrary[0]);
            var drawnCard = _yourLibrary[0];
            _yourLibrary.RemoveAt(0);
            AddLog($"Drew {drawnCard.Name}");
            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        private int _mulliganCount = 0; // tracks how many times mulliganed this game

        private void UpdateMulliganDisplay()
        {
            // MulliganCountText removed — mulligan only available at game start
        }

        private void DoMulligan()
        {
            // Return current hand to library and shuffle
            foreach (var card in _yourHand)
                _yourLibrary.Add(card);
            _yourHand.Clear();
            Shuffle(_yourLibrary);

            // Draw 7
            for (int i = 0; i < 7 && _yourLibrary.Count > 0; i++)
            {
                _yourHand.Add(_yourLibrary[0]);
                _yourLibrary.RemoveAt(0);
            }

            _mulliganCount++;
            UpdateMulliganDisplay();

            bool isCommander = _yourCommander != null;

            // Open mulligan window
            var dlg = new MulliganWindow(
                new List<DeckCard>(_yourHand),
                _yourLibrary,
                _mulliganCount,
                isCommander)
            { Owner = this };

            if (dlg.ShowDialog() != true) return;

            if (dlg.MulliganedAgain)
            {
                // Recurse — mulligan again
                DoMulligan();
                return;
            }

            // Apply result — set hand to kept cards
            _yourHand.Clear();
            foreach (var card in dlg.FinalHand)
                _yourHand.Add(card);

            // Put selected cards on bottom of library
            foreach (var card in dlg.BottomCards)
                _yourLibrary.Add(card);

            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        // ================================================================
        // STACK
        // ================================================================
        private void BtnToggleStack_Click(object sender, RoutedEventArgs e)
        {
            _stackVisible = !_stackVisible;
            StackPanel.Visibility = _stackVisible
                ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleStack.Content = _stackVisible ? "STACK ▲" : "STACK ▼";
        }

        // ================================================================
        // PHASE 5 — BATTLEFIELD
        // ================================================================

        // -- Play card from hand to battlefield --
        // Auto-position a card on the battlefield canvas
        private static (double x, double y) AutoPosition(
            Canvas canvas, int cardIndex, double cardW, double cardH)
        {
            double pad = 8;
            double canvasW = canvas.ActualWidth > 0 ? canvas.ActualWidth : 900;
            int cols = Math.Max(1, (int)((canvasW - pad) / (cardW + pad)));
            double col = cardIndex % cols;
            double row = cardIndex / cols;
            return (pad + col * (cardW + pad), pad + row * (cardH + pad));
        }

        // Get the correct canvas for any battlefield card
        private Canvas GetCardCanvas(BattlefieldCard bc) => bc.IsLandZone
            ? (bc.IsYour ? YourLandCanvas : OppLandCanvas)
            : (bc.IsYour ? YourBattlefieldCanvas : OppBattlefieldCanvas);

        private void PlayCardFromHand(int handIndex, bool isYour, bool toLand = false)
        {
            var hand = isYour ? _yourHand : _oppHand;
            var field = isYour ? _yourField : _oppField;
            var canvas = toLand
                ? (isYour ? YourLandCanvas : OppLandCanvas)
                : (isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas);

            if (handIndex < 0 || handIndex >= hand.Count) return;
            var card = hand[handIndex];
            hand.RemoveAt(handIndex);

            // Position — RelayoutLandZone handles lands, auto-pos handles battlefield
            var bc = new BattlefieldCard
            {
                Card = card,
                X = 0, // set by RelayoutLandZone for lands
                Y = 0,
                IsYour = isYour,
                IsLandZone = toLand
            };
            field.Add(bc);
            AddLog($"{(isYour ? "You" : "Opponent")} played {card.Name}{(toLand ? " (land)" : "")}");

            if (toLand)
                RelayoutLandZone(isYour);
            else
                RenderBattlefieldCard(bc, canvas);
            UpdateHandCounts();
            RenderHand(isYour: true);
            if (isYour) UpdateManaPoolSummary();
        }

        private void PlayCardFromHandFaceDown(int handIndex, bool isYour)
        {
            var hand = isYour ? _yourHand : _oppHand;
            var field = isYour ? _yourField : _oppField;
            var canvas = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;

            if (handIndex < 0 || handIndex >= hand.Count) return;
            var card = hand[handIndex];
            hand.RemoveAt(handIndex);

            var existing = field.Where(c => !c.IsLandZone).ToList();
            var (x, y) = AutoPosition(canvas, existing.Count, FieldW, FieldH);
            var bc = new BattlefieldCard
            {
                Card = card,
                X = x,
                Y = y,
                IsYour = isYour,
                IsLandZone = false,
                IsFaceDown = true
            };
            field.Add(bc);
            RenderBattlefieldCard(bc, canvas);
            UpdateHandCounts();
            RenderHand(isYour: true);
        }


        // -- Safely remove old visual --
        private static void RemoveCardVisual(BattlefieldCard bc, Canvas canvas)
        {
            if (bc.Visual == null) return;
            // Detach child from logical tree BEFORE removing from canvas
            try { bc.Visual.Child = null; } catch { }
            try
            {
                if (canvas.Children.Contains(bc.Visual))
                    canvas.Children.Remove(bc.Visual);
            }
            catch { }
            bc.Visual = null;
        }

        // -- Render a single battlefield card --
        private void RenderBattlefieldCard(BattlefieldCard bc, Canvas canvas)
        {
            RemoveCardVisual(bc, canvas); // always clean up old visual
            var border = MakeCardVisual(bc.Card, bc.IsYour, isHand: false,
                faceDown: bc.IsFaceDown, isTransformed: bc.IsTransformed);
            // Always portrait dimensions — LayoutTransform rotates for tapped
            border.Width = FieldW;
            border.Height = FieldH;
            border.Tag = bc;

            // Tapped = border rotated 90° via LayoutTransform (affects layout size)
            // Untapped = portrait (90w x 126h), no transform
            if (bc.IsTapped)
            {
                border.LayoutTransform = new RotateTransform(90);
                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(0xFF, 0xCC, 0x44));
            }
            else
            {
                border.LayoutTransform = new RotateTransform(0);
                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(0x22, 0x22, 0x22));
            }

            // Counter badge — overlay inside the border, not wrapping it
            if (bc.Counters.Count > 0)
            {
                var badgeText = BuildCounterText(bc);

                var grid = new Grid();
                var existingChild = border.Child;
                border.Child = null;
                grid.Children.Add(existingChild ?? new UIElement());
                var badge = new TextBlock
                {
                    Text = badgeText,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Panel.SetZIndex(badge, 10);
                grid.Children.Add(badge);
                border.Child = grid;
            }

            // Temp effect badge — shown at top of card in yellow/orange
            if (bc.TempEffects.Count > 0)
            {
                // Build label: P/T boosts combined, keywords listed
                int totalP = bc.TempEffects.Sum(ef => ef.BonusPower);
                int totalT = bc.TempEffects.Sum(ef => ef.BonusToughness);
                var keywords = bc.TempEffects
                    .Where(ef => ef.BonusPower == 0 && ef.BonusToughness == 0)
                    .Select(ef => ef.Label)
                    .ToList();

                var lines = new List<string>();
                if (totalP != 0 || totalT != 0)
                    lines.Add($"{(totalP >= 0 ? "+" : "")}{totalP}/{(totalT >= 0 ? "+" : "")}{totalT}");
                lines.AddRange(keywords);

                // Wrap card child in a grid if not already
                if (!(border.Child is Grid))
                {
                    var g = new Grid();
                    var existing = border.Child;
                    border.Child = null;
                    g.Children.Add(existing ?? new UIElement());
                    border.Child = g;
                }

                var eotBadge = new TextBlock
                {
                    Text = string.Join(" · ", lines),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    Background = new SolidColorBrush(Color.FromArgb(230, 0xFF, 0xCC, 0x00)),
                    Padding = new Thickness(3, 1, 3, 1),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = FieldW - 4
                };
                Panel.SetZIndex(eotBadge, 11);
                ((Grid)border.Child).Children.Add(eotBadge);
            }

            // Click = tap/untap
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ShowCardEnlarged(bc.Card, bc);
                    e.Handled = true;
                    return;
                }
                TapUntap(bc, canvas);
                e.Handled = true;
            };

            // Right-click = context menu
            border.MouseRightButtonDown += (s, e) =>
            {
                ShowBattlefieldContextMenu(bc, canvas, border);
                e.Handled = true;
            };

            // Drag to reposition
            bool dragging = false;
            Point dragStart = default;
            border.MouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || !dragging) return;
                var pos = e.GetPosition(canvas);
                bc.X = pos.X - dragStart.X;
                bc.Y = pos.Y - dragStart.Y;
                Canvas.SetLeft(border, bc.X);
                Canvas.SetTop(border, bc.Y);
            };
            border.MouseDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                dragging = true;
                dragStart = e.GetPosition(border);
                border.CaptureMouse();
            };
            border.MouseUp += (s, e) =>
            {
                dragging = false;
                border.ReleaseMouseCapture();
            };

            bc.Visual = border;
            Canvas.SetLeft(border, bc.X);
            Canvas.SetTop(border, bc.Y);
            Panel.SetZIndex(border, 5);
            canvas.Children.Add(border);
        }

        // -- Tap / Untap --
        private void TapUntap(BattlefieldCard bc, Canvas canvas)
        {
            bc.IsTapped = !bc.IsTapped;
            AddLog($"{(bc.IsTapped ? "Tapped" : "Untapped")} {bc.Card.Name}");
            if (bc.IsLandZone)
            {
                // Relayout the whole land zone so rows stay grouped
                RelayoutLandZone(bc.IsYour);
            }
            else
            {
                RenderBattlefieldCard(bc, canvas);
            }
            if (bc.IsYour) UpdateManaPoolSummary();
        }

        // -- Untap All your permanents --
        private void UntapAllYour()
        {
            foreach (var bc in _yourField)
                bc.IsTapped = false;
            RedrawBattlefield(isYour: true);
            // RelayoutLandZone is called inside RedrawBattlefield
        }

        // -- Redraw full battlefield --
        private void RedrawBattlefield(bool isYour)
        {
            if (isYour) { YourBattlefieldCanvas.Children.Clear(); YourLandCanvas.Children.Clear(); }
            else { OppBattlefieldCanvas.Children.Clear(); OppLandCanvas.Children.Clear(); }

            var field = isYour ? _yourField : _oppField;

            // Battlefield cards — render as-is
            foreach (var bc in field.Where(c => !c.IsLandZone))
            {
                bc.Visual = null;
                RenderBattlefieldCard(bc, GetCardCanvas(bc));
            }

            // Land zone — grouped layout
            RelayoutLandZone(isYour);
        }

        // Layout land zone: group by name, untapped row above tapped row
        private void RelayoutLandZone(bool isYour)
        {
            var canvas = isYour ? YourLandCanvas : OppLandCanvas;
            var lands = (isYour ? _yourField : _oppField)
                         .Where(c => c.IsLandZone).ToList();

            canvas.Children.Clear();
            foreach (var bc in lands) bc.Visual = null;

            var groups = lands
                .GroupBy(c => c.Card.Name)
                .OrderBy(g => g.Key)
                .ToList();

            // Layout constants
            double groupSpacing = 20;
            double x = 10;

            foreach (var group in groups)
            {
                var untapped = group.Where(c => !c.IsTapped).ToList();
                var tapped = group.Where(c => c.IsTapped).ToList();

                // Use first untapped card as the face shown (portrait on top)
                // Use first tapped card as the face shown (landscape below)
                // Remaining cards of each type are invisible data-only

                double stackTop = 10; // Y start for the stack

                // --- TAPPED card (landscape, behind — top aligned with untapped) ---
                if (tapped.Count > 0)
                {
                    var bc = tapped[0];
                    bc.X = x - (FieldH - FieldW) / 2; // center landscape under portrait
                    bc.Y = stackTop;                    // top-aligned with untapped

                    RenderBattlefieldCard(bc, canvas);
                    Panel.SetZIndex(bc.Visual!, 5);     // behind untapped

                    // Tapped count badge — upper left of tapped card
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 2, 4, 2),
                        Child = new TextBlock
                        {
                            Text = tapped.Count.ToString(),
                            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44)),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    Canvas.SetLeft(badge, bc.X + 4);
                    Canvas.SetTop(badge, bc.Y + 4);
                    Panel.SetZIndex(badge, 20);
                    canvas.Children.Add(badge);

                    foreach (var extra in tapped.Skip(1))
                        extra.Visual = null;
                }

                // --- UNTAPPED card (portrait, in front — top aligned) ---
                if (untapped.Count > 0)
                {
                    var bc = untapped[0];
                    bc.X = x;
                    bc.Y = stackTop;
                    RenderBattlefieldCard(bc, canvas);
                    Panel.SetZIndex(bc.Visual!, 10);    // always in front of tapped

                    // Untapped count badge — bottom right of untapped card
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 2, 4, 2),
                        Child = new TextBlock
                        {
                            Text = untapped.Count.ToString(),
                            Foreground = Brushes.White,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    Canvas.SetLeft(badge, bc.X + FieldW - 24);
                    Canvas.SetTop(badge, bc.Y + FieldH - 22);
                    Panel.SetZIndex(badge, 20);
                    canvas.Children.Add(badge);

                    foreach (var extra in untapped.Skip(1))
                        extra.Visual = null;
                }

                // Advance X — stack width = portrait width (wider landscape handled by centering)
                double stackW = tapped.Count > 0
                    ? Math.Max(FieldW, FieldH) // landscape is wider
                    : FieldW;
                x += stackW + groupSpacing;
            }

            if (isYour) UpdateManaPoolSummary();
        }

        // -- Context menu --
        private void ShowBattlefieldContextMenu(BattlefieldCard bc,
            Canvas canvas, Border border)
        {
            bool isYour = bc.IsYour;
            var menu = new ContextMenu();

            // Label for opponent cards so it's clear which side you're controlling
            if (!isYour)
                menu.Items.Add(new MenuItem
                { Header = "— Opponent's Card —", IsEnabled = false });

            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (s, e) => action();
                menu.Items.Add(item);
            }
            void Sep() => menu.Items.Add(new Separator());

            Add(bc.IsTapped ? "⟳ Untap" : "↷ Tap", () => TapUntap(bc, canvas));
            Sep();

            // Move to zone
            var moveMenu = new MenuItem { Header = "Move to →" };
            void AddMove(string label, Action act)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (s, e) => act();
                moveMenu.Items.Add(mi);
            }

            AddMove("Graveyard", () => MoveFromBattlefield(bc, "graveyard", isYour));
            AddMove("Exile", () => MoveFromBattlefield(bc, "exile", isYour));

            // Linked exile — exile a target card under this permanent
            var linkExileItem = new MenuItem { Header = "🔗 Exile Target Under This Card" };
            linkExileItem.Click += (s, e) => StartLinkedExile(bc, isYour);
            moveMenu.Items.Add(linkExileItem);

            moveMenu.Items.Add(new Separator());
            AddMove("⚡ Cast to Stack", () =>
            {
                var field = isYour ? _yourField : _oppField;
                RemoveCardVisual(bc, GetCardCanvas(bc));
                field.Remove(bc);
                TriggerLinkedExileReturn(bc);
                AddToStack(bc.Card);
            });
            moveMenu.Items.Add(new Separator());

            AddMove("Hand", () => MoveFromBattlefield(bc, "hand", isYour));
            AddMove("Library — Top", () => MoveFromBattlefield(bc, "libtop", isYour));
            AddMove("Library — Bottom", () => MoveFromBattlefield(bc, "libbot", isYour));
            if (bc.Card.IsCommander)
                AddMove("Command Zone", () => MoveFromBattlefield(bc, "command", isYour));
            menu.Items.Add(moveMenu);

            Sep();

            // Add counter
            var counterMenu = new MenuItem { Header = "Add Counter →" };
            foreach (var ctype in new[] { "+1/+1", "-1/-1", "+1/0", "0/+1",
                "Loyalty", "Charge", "Time", "Poison", "Custom..." })
            {
                var ct = ctype;
                var mi = new MenuItem { Header = ct };
                mi.Click += (s, e) => AddCounter(bc, ct, canvas);
                counterMenu.Items.Add(mi);
            }
            menu.Items.Add(counterMenu);

            // Remove counter
            if (bc.Counters.Count > 0)
            {
                var removeMenu = new MenuItem { Header = "Remove Counter →" };
                foreach (var kv in bc.Counters.ToList())
                {
                    var key = kv.Key;
                    var mi = new MenuItem { Header = $"{key} ({kv.Value})" };
                    mi.Click += (s, e) => RemoveCounter(bc, key, canvas);
                    removeMenu.Items.Add(mi);
                }
                menu.Items.Add(removeMenu);
            }

            Sep();
            Add("\U0001f501 Create Token Copy", () => CreateTokenCopy(bc.Card, isYour));
            Add("🔍 View Card", () => ShowCardEnlarged(bc.Card, bc));
            Add("📋 Clone", () => CloneCard(bc, canvas, isYour));

            // Transform — only shown when card has a back face
            if (!string.IsNullOrEmpty(bc.Card.ImageBackUrl) ||
                !string.IsNullOrEmpty(bc.Card.LocalImageBackPath))
            {
                Sep();
                Add(bc.IsTransformed ? "🔄 Transform (→ Front Face)" : "🔄 Transform (→ Back Face)",
                    () => TransformCard(bc));
            }

            // Until End of Turn effects
            Sep();
            var eotMenu = new MenuItem { Header = "⏱ Until End of Turn →" };

            void AddEot(string label, int p = 0, int t = 0)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (s, e) =>
                {
                    bc.TempEffects.Add(new TempEffect
                    {
                        Label = label,
                        BonusPower = p,
                        BonusToughness = t
                    });
                    RenderBattlefieldCard(bc, GetCardCanvas(bc));
                };
                eotMenu.Items.Add(mi);
            }

            // P/T boosts
            AddEot("+1/+1", 1, 1);
            AddEot("+2/+2", 2, 2);
            AddEot("+3/+3", 3, 3);
            AddEot("+0/+3", 0, 3);
            AddEot("+3/+0", 3, 0);

            var customPT = new MenuItem { Header = "Custom +P/+T..." };
            customPT.Click += (s, e) =>
            {
                var dlg = new InputDialog("Until End of Turn", "Enter bonus (e.g.  +2/+3 or -1/-1):");
                dlg.Owner = this;
                if (dlg.ShowDialog() != true) return;
                string raw = dlg.Value.Trim();
                // Parse formats like "+2/+3", "2/3", "-1/-1"
                int p = 0, t = 0;
                var parts = raw.Replace("+", "").Split('/');
                if (parts.Length == 2)
                {
                    int.TryParse(parts[0].Trim(), out p);
                    int.TryParse(parts[1].Trim(), out t);
                }
                string label = $"{(p >= 0 ? "+" : "")}{p}/{(t >= 0 ? "+" : "")}{t}";
                bc.TempEffects.Add(new TempEffect { Label = label, BonusPower = p, BonusToughness = t });
                RenderBattlefieldCard(bc, GetCardCanvas(bc));
            };
            eotMenu.Items.Add(customPT);
            eotMenu.Items.Add(new Separator());

            // Keyword abilities
            foreach (var kw in new[] { "Flying", "Trample", "Haste", "Lifelink",
                                       "Indestructible", "Hexproof", "Deathtouch",
                                       "First Strike", "Double Strike", "Vigilance",
                                       "Menace", "Reach", "Shroud" })
            {
                var k = kw;
                var mi = new MenuItem { Header = k };
                mi.Click += (s, e) =>
                {
                    // Don't add duplicates
                    if (!bc.TempEffects.Any(ef => ef.Label == k))
                    {
                        bc.TempEffects.Add(new TempEffect { Label = k });
                        RenderBattlefieldCard(bc, GetCardCanvas(bc));
                    }
                };
                eotMenu.Items.Add(mi);
            }

            var customKw = new MenuItem { Header = "Custom effect..." };
            customKw.Click += (s, e) =>
            {
                var dlg = new InputDialog("Until End of Turn", "Enter effect (e.g. \"Protection from Red\"):");
                dlg.Owner = this;
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
                bc.TempEffects.Add(new TempEffect { Label = dlg.Value.Trim() });
                RenderBattlefieldCard(bc, GetCardCanvas(bc));
            };
            eotMenu.Items.Add(customKw);

            // Remove a specific temp effect if any exist
            if (bc.TempEffects.Count > 0)
            {
                eotMenu.Items.Add(new Separator());
                var removeEot = new MenuItem { Header = "Remove Effect →" };
                foreach (var ef in bc.TempEffects.ToList())
                {
                    var captured = ef;
                    var mi = new MenuItem { Header = captured.Label };
                    mi.Click += (s, e) =>
                    {
                        bc.TempEffects.Remove(captured);
                        RenderBattlefieldCard(bc, GetCardCanvas(bc));
                    };
                    removeEot.Items.Add(mi);
                }
                eotMenu.Items.Add(removeEot);
            }

            menu.Items.Add(eotMenu);

            border.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // -- Move from battlefield to another zone --
        private void MoveFromBattlefield(BattlefieldCard bc,
            string destination, bool isYour)
        {
            var field = isYour ? _yourField : _oppField;

            RemoveCardVisual(bc, GetCardCanvas(bc));
            field.Remove(bc);

            // If it was a land, relayout remaining lands
            if (bc.IsLandZone) RelayoutLandZone(isYour);

            // Check if this card is a linked exile host
            TriggerLinkedExileReturn(bc);

            bool isCommander = (isYour && _yourCommander?.Name == bc.Card.Name)
                             || (!isYour && _oppCommander?.Name == bc.Card.Name)
                             || bc.Card.IsCommander;

            if (isCommander && destination != "command")
            {
                // Commander leaving battlefield — offer zone choice
                PromptCommanderZone(bc.Card, isYour, destination);
                return;
            }

            // Non-commander or going directly to command zone
            switch (destination)
            {
                case "graveyard":
                    if (isYour) _yourGrave.Add(bc.Card); else _oppGrave.Add(bc.Card);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(bc.Card); else _oppExile.Add(bc.Card);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(bc.Card); else _oppHand.Add(bc.Card);
                    RenderHand(isYour: true); UpdateHandCounts();
                    break;
                case "libtop":
                    if (isYour) _yourLibrary.Insert(0, bc.Card);
                    else _oppLibrary.Insert(0, bc.Card);
                    break;
                case "libbot":
                    if (isYour) _yourLibrary.Add(bc.Card);
                    else _oppLibrary.Add(bc.Card);
                    break;
                case "command":
                    if (isYour) _yourCommander = bc.Card;
                    else _oppCommander = bc.Card;
                    SetCommanderLocation(isYour, CommanderLocation.CommandZone);
                    RenderCommander(isYour);
                    break;
            }
            AddLog($"{bc.Card.Name} → {destination}");
            UpdateZoneCounts();
        }

        // -- Add counter --
        private void AddCounter(BattlefieldCard bc, string type, Canvas canvas)
        {
            string key = type;
            if (type == "Custom...")
            {
                var dlg = new InputDialog("Custom Counter",
                    "Enter counter type (e.g. Oil, Quest):")
                { Owner = this };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
                    return;
                key = dlg.Value.Trim();
            }
            // For P/T counters combine into running totals
            if (key == "+1/+1") { AddPTCounter(bc, 1, 1); }
            else if (key == "-1/-1") { AddPTCounter(bc, -1, -1); }
            else if (key == "+1/0") { AddPTCounter(bc, 1, 0); }
            else if (key == "0/+1") { AddPTCounter(bc, 0, 1); }
            else { bc.Counters.TryGetValue(key, out int cur); bc.Counters[key] = cur + 1; }
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        // -- Remove counter --
        private void RemoveCounter(BattlefieldCard bc, string key, Canvas canvas)
        {
            if (!bc.Counters.ContainsKey(key)) return;
            bc.Counters[key]--;
            if (bc.Counters[key] <= 0) bc.Counters.Remove(key);
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        // -- Clone card --
        private void CloneCard(BattlefieldCard bc, Canvas canvas, bool isYour)
        {
            var clone = new BattlefieldCard
            {
                Card = bc.Card,
                X = bc.X + 20,
                Y = bc.Y + 20,
                IsYour = isYour
            };
            var field = isYour ? _yourField : _oppField;
            field.Add(clone);
            RenderBattlefieldCard(clone, canvas);
        }

        // -- P/T counter accumulation --
        private static void AddPTCounter(BattlefieldCard bc, int power, int tough)
        {
            bc.Counters.TryGetValue("_P", out int p);
            bc.Counters.TryGetValue("_T", out int t);
            bc.Counters["_P"] = p + power;
            bc.Counters["_T"] = t + tough;
            // Remove if both zero
            if (bc.Counters["_P"] == 0 && bc.Counters["_T"] == 0)
            {
                bc.Counters.Remove("_P");
                bc.Counters.Remove("_T");
            }
        }

        // -- Build display text for counters --
        private static string BuildCounterText(BattlefieldCard bc)
        {
            var parts = new List<string>();
            int p = 0, t = 0;
            bc.Counters.TryGetValue("_P", out p);
            bc.Counters.TryGetValue("_T", out t);
            if (p != 0 || t != 0)
                parts.Add($"{(p >= 0 ? "+" : "")}{p}/{(t >= 0 ? "+" : "")}{t}");
            foreach (var kv in bc.Counters.Where(k => k.Key != "_P" && k.Key != "_T"))
                parts.Add($"{kv.Key}×{kv.Value}");
            return string.Join("  ", parts);
        }

        // -- View card enlarged --
        private void ShowCardEnlarged(DeckCard card, BattlefieldCard? bc = null)
        {
            _enlargedCardWindow?.Close();
            _enlargedCardWindow = null;

            bool hasFront = !string.IsNullOrEmpty(card.LocalImagePath)
                               && System.IO.File.Exists(card.LocalImagePath);
            bool hasBack = !string.IsNullOrEmpty(card.LocalImageBackPath)
                               && System.IO.File.Exists(card.LocalImageBackPath);
            bool isDfc = hasBack || !string.IsNullOrEmpty(card.ImageBackUrl);
            bool hasCounters = bc != null && bc.Counters.Count > 0;

            // Start on whichever face the card is currently showing on the battlefield
            bool showingBack = bc?.IsTransformed ?? false;

            var win = new Window
            {
                Title = card.Name,
                Width = 360,
                Height = hasCounters ? 580 : 530,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Black
            };

            var root = new StackPanel { Background = Brushes.Black };

            // Face label
            var faceLabel = new TextBlock
            {
                Text = isDfc ? (showingBack ? $"◀ Back Face — {card.Name}" : $"Front Face — {card.Name} ▶") : card.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xCC, 0xFF)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2)
            };
            root.Children.Add(faceLabel);

            // Card image container
            var imgControl = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Height = 460,
                Margin = new Thickness(4, 0, 4, 0)
            };

            void LoadFace(bool back)
            {
                string? path = back ? card.LocalImageBackPath : card.LocalImagePath;
                imgControl.Source = null;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgControl.Source = bmp;
                    }
                    catch { imgControl.Source = null; }
                }
                faceLabel.Text = isDfc
                    ? (back ? $"◀ Back Face — {card.Name}" : $"Front Face — {card.Name} ▶")
                    : card.Name;
            }

            LoadFace(showingBack);
            root.Children.Add(imgControl);

            // Flip button — only for DFCs
            if (isDfc)
            {
                var flipBtn = new Button
                {
                    Content = showingBack ? "🔄 Show Front Face" : "🔄 Show Back Face",
                    Margin = new Thickness(8, 4, 8, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x66)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x77, 0xAA)),
                    FontSize = 12,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                flipBtn.Click += (s, e) =>
                {
                    showingBack = !showingBack;
                    LoadFace(showingBack);
                    flipBtn.Content = showingBack ? "🔄 Show Front Face" : "🔄 Show Back Face";

                    // Also transform the battlefield card if it came from the battlefield
                    if (bc != null && bc.IsTransformed != showingBack)
                        TransformCard(bc);
                };
                root.Children.Add(flipBtn);
            }

            // Counter info
            if (hasCounters)
            {
                var counterText = BuildCounterText(bc!);
                if (!string.IsNullOrEmpty(counterText))
                    root.Children.Add(new TextBlock
                    {
                        Text = counterText,
                        Foreground = Brushes.Yellow,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 0)
                    });
            }

            // Temp effects (until end of turn)
            if (bc != null && bc.TempEffects.Count > 0)
            {
                int totalP = bc.TempEffects.Sum(ef => ef.BonusPower);
                int totalT = bc.TempEffects.Sum(ef => ef.BonusToughness);
                var kwList = bc.TempEffects
                    .Where(ef => ef.BonusPower == 0 && ef.BonusToughness == 0)
                    .Select(ef => ef.Label);
                var eotLines = new List<string>();
                if (totalP != 0 || totalT != 0)
                    eotLines.Add($"{(totalP >= 0 ? "+" : "")}{totalP}/{(totalT >= 0 ? "+" : "")}{totalT}");
                eotLines.AddRange(kwList);
                root.Children.Add(new TextBlock
                {
                    Text = "⏱ " + string.Join("  ·  ", eotLines),
                    Foreground = Brushes.Black,
                    Background = new SolidColorBrush(Color.FromArgb(230, 0xFF, 0xCC, 0x00)),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                });
            }

            // Click anywhere else to close
            root.Children.Add(new TextBlock
            {
                Text = "Click anywhere to close",
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            });

            win.Content = root;
            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) =>
            {
                if (_enlargedCardWindow == win) _enlargedCardWindow = null;
            };
            _enlargedCardWindow = win;
            win.Show();
        }

        // ── Reveal a card to the "opponent" (shows in a labeled popup) ────────
        private void RevealCard(DeckCard card)
        {
            _enlargedCardWindow?.Close();
            _enlargedCardWindow = null;

            var win = new Window
            {
                Title = $"REVEALED: {card.Name}",
                Width = 380,
                Height = 560,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Black
            };

            var root = new StackPanel { Background = Brushes.Black };

            // Reveal banner
            root.Children.Add(new TextBlock
            {
                Text = "👁  REVEALED TO ALL PLAYERS  👁",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(180, 0x44, 0x33, 0x00)),
                Padding = new Thickness(0, 6, 0, 6),
                TextAlignment = TextAlignment.Center
            });

            if (!string.IsNullOrEmpty(card.LocalImagePath) && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    root.Children.Add(new System.Windows.Controls.Image
                    { Source = bmp, Stretch = Stretch.Uniform, Height = 460 });
                }
                catch { }
            }

            root.Children.Add(new TextBlock
            {
                Text = "Click anywhere to close",
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            });

            win.Content = root;
            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) => { if (_enlargedCardWindow == win) _enlargedCardWindow = null; };
            _enlargedCardWindow = win;
            win.Show();
        }
        private void WireHandCardPlay(Border border, int index, bool isYour)
        {
            border.MouseRightButtonDown += (s, e) =>
            {
                var hand = isYour ? _yourHand : _oppHand;
                var menu = new ContextMenu();

                var playBf = new MenuItem { Header = "\u25b6 Play to Battlefield" };
                playBf.Click += (s2, e2) => PlayCardFromHand(index, isYour, toLand: false);

                var playMana = new MenuItem { Header = "\u25c8 Play to Mana Zone" };
                playMana.Click += (s2, e2) => PlayCardFromHand(index, isYour, toLand: true);

                var playFaceDown = new MenuItem { Header = "\U0001f0a0 Play Face-Down" };
                playFaceDown.Click += (s2, e2) =>
                    PlayCardFromHandFaceDown(index, isYour);

                var copyToken = new MenuItem { Header = "\U0001f501 Create Token Copy" };
                copyToken.Click += (s2, e2) =>
                {
                    if (index < hand.Count)
                        CreateTokenCopy(hand[index], isYour: true);
                };

                var viewCard = new MenuItem { Header = "🔍 View Card" };
                viewCard.Click += (s2, e2) =>
                {
                    if (index < hand.Count)
                        ShowCardEnlarged(hand[index]);
                };

                var revealCard = new MenuItem { Header = "👁 Reveal to Opponent" };
                revealCard.Click += (s2, e2) =>
                {
                    if (index < hand.Count)
                        RevealCard(hand[index]);
                };

                var discardCard = new MenuItem { Header = "🗑 Discard" };
                discardCard.Click += (s2, e2) =>
                {
                    if (index < hand.Count)
                    {
                        var card = hand[index];
                        hand.RemoveAt(index);
                        if (isYour) _yourGrave.Add(card);
                        else _oppGrave.Add(card);
                        UpdateZoneCounts();
                        UpdateHandCounts();
                        RenderHand(isYour: isYour);
                    }
                };

                var returnLib = new MenuItem { Header = "⬆ Return to Library Top" };
                returnLib.Click += (s2, e2) =>
                {
                    if (index < hand.Count)
                    {
                        var card = hand[index];
                        hand.RemoveAt(index);
                        if (isYour) _yourLibrary.Insert(0, card);
                        else _oppLibrary.Insert(0, card);
                        UpdateZoneCounts();
                        UpdateHandCounts();
                        RenderHand(isYour: isYour);
                    }
                };

                menu.Items.Add(playBf);
                menu.Items.Add(playMana);
                menu.Items.Add(playFaceDown);
                menu.Items.Add(new Separator());
                menu.Items.Add(copyToken);
                menu.Items.Add(new Separator());
                menu.Items.Add(viewCard);
                menu.Items.Add(revealCard);
                menu.Items.Add(new Separator());
                menu.Items.Add(discardCard);
                menu.Items.Add(returnLib);
                border.ContextMenu = menu;
                menu.IsOpen = true;
                e.Handled = true;
            };
        }





        // ================================================================
        // PEEK OPPONENT HAND
        // ================================================================
        private void BtnPeekOppHand_Click(object sender, RoutedEventArgs e)
        {
            if (_oppHand.Count == 0)
            {
                MessageBox.Show("Opponent's hand is empty.", "Peek Hand",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Read-only browser — no move menu (faceDown=false so cards show,
            // but we pass a null OnMoveCard so moves are disabled)
            var browser = new ZoneBrowserWindow(ZoneType.Graveyard, isYour: false,
                new List<DeckCard>(_oppHand), faceDown: false)
            {
                Owner = this,
                Title = "Opponent's Hand (Peek)"
            };
            // No OnMoveCard wired — read only peek
            browser.Show();
        }

        // ================================================================
        // LINKED EXILE
        // ================================================================

        // Step 1 — pick which card to exile (opens hand + battlefield picker)
        private void StartLinkedExile(BattlefieldCard host, bool isYour)
        {
            // Build a list of all cards that could be targeted
            // (both players' battlefields, hands, graveyards, exile)
            var targets = new List<DeckCard>();
            targets.AddRange(_yourField.Select(c => c.Card));
            targets.AddRange(_oppField.Select(c => c.Card));
            targets.AddRange(_yourHand);
            targets.AddRange(_oppHand);
            targets.AddRange(_yourGrave);
            targets.AddRange(_oppGrave);
            targets.AddRange(_yourExile);
            targets.AddRange(_oppExile);

            // Remove the host itself
            targets.RemoveAll(c => c.Name == host.Card.Name);

            if (targets.Count == 0)
            {
                MessageBox.Show("No valid targets found.", "Linked Exile",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Open a picker window
            var picker = new LinkedExilePickerWindow(targets, host.Card.Name)
            { Owner = this };

            if (picker.ShowDialog() != true || picker.SelectedCard == null) return;

            var target = picker.SelectedCard;

            // Remove from wherever it is
            RemoveCardFromAllZones(target);

            // Add to linked exile
            var link = _linkedExiles.FirstOrDefault(
                l => l.HostCardName == host.Card.Name && l.HostIsYour == isYour);
            if (link == null)
            {
                link = new LinkedExile
                {
                    HostCardName = host.Card.Name,
                    HostIsYour = isYour
                };
                _linkedExiles.Add(link);
            }
            link.ExiledCards.Add(target);

            // If it was commander, update location
            if ((isYour && _yourCommander?.Name == target.Name) ||
                (!isYour && _oppCommander?.Name == target.Name))
                SetCommanderLocation(isYour, CommanderLocation.Exile);

            UpdateZoneCounts();
            MessageBox.Show(
                $"{target.Name} has been exiled under {host.Card.Name}.",
                "Linked Exile", MessageBoxButton.OK, MessageBoxImage.None);
        }

        // Remove a card from whichever zone it's currently in
        private void RemoveCardFromAllZones(DeckCard card)
        {
            _yourHand.RemoveAll(c => c.Name == card.Name);
            _oppHand.RemoveAll(c => c.Name == card.Name);
            _yourGrave.RemoveAll(c => c.Name == card.Name);
            _oppGrave.RemoveAll(c => c.Name == card.Name);
            _yourExile.RemoveAll(c => c.Name == card.Name);
            _oppExile.RemoveAll(c => c.Name == card.Name);

            var yourBfCard = _yourField.FirstOrDefault(c => c.Card.Name == card.Name);
            if (yourBfCard != null)
            {
                RemoveCardVisual(yourBfCard, GetCardCanvas(yourBfCard));
                _yourField.Remove(yourBfCard);
            }
            var oppBfCard = _oppField.FirstOrDefault(c => c.Card.Name == card.Name);
            if (oppBfCard != null)
            {
                RemoveCardVisual(oppBfCard, GetCardCanvas(oppBfCard));
                _oppField.Remove(oppBfCard);
            }

            RenderHand(isYour: true);
            UpdateHandCounts();
        }

        // Called when a host permanent leaves the battlefield
        private void TriggerLinkedExileReturn(BattlefieldCard host)
        {
            var links = _linkedExiles
                .Where(l => l.HostCardName == host.Card.Name
                         && l.HostIsYour == host.IsYour)
                .ToList();

            foreach (var link in links)
            {
                foreach (var card in link.ExiledCards.ToList())
                {
                    PromptLinkedExileReturn(card, host.IsYour);
                }
                _linkedExiles.Remove(link);
            }
        }

        private void PromptLinkedExileReturn(DeckCard card, bool isYour)
        {
            var win = new Window
            {
                Title = "Linked Exile — Return Card",
                Width = 380,
                Height = 200,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = $"The exile effect on {card.Name} has ended.",
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Where should it return?",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 14)
            });

            string chosen = "battlefield";

            var btns = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };

            void MakeBtn(string label, string dest, string color)
            {
                var btn = new Button
                {
                    Content = label,
                    Height = 30,
                    Margin = new Thickness(4),
                    Padding = new Thickness(8, 0, 8, 0),
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(color)),
                    Foreground = Brushes.White,
                    BorderBrush = Brushes.Transparent
                };
                btn.Click += (s, e) => { chosen = dest; win.Close(); };
                btns.Children.Add(btn);
            }

            MakeBtn("▶ Battlefield", "battlefield", "#225522");
            MakeBtn("◈ Mana Zone", "manazone", "#334433");
            MakeBtn("🤚 Hand", "hand", "#224455");
            MakeBtn("💀 Graveyard", "graveyard", "#443322");
            MakeBtn("✦ Stay in Exile", "exile", "#442222");

            stack.Children.Add(btns);
            win.Content = stack;
            win.ShowDialog();

            // Apply return
            switch (chosen)
            {
                case "battlefield":
                    var fieldBf = isYour ? _yourField : _oppField;
                    var canvasBf = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;
                    var bc = new BattlefieldCard
                    {
                        Card = card,
                        IsYour = isYour,
                        IsLandZone = false,
                        X = 20 + fieldBf.Count(c => !c.IsLandZone) * (FieldW + 6),
                        Y = 10
                    };
                    fieldBf.Add(bc);
                    RenderBattlefieldCard(bc, canvasBf);
                    break;
                case "manazone":
                    var fieldMz = isYour ? _yourField : _oppField;
                    var canvasMz = isYour ? YourLandCanvas : OppLandCanvas;
                    var bcMz = new BattlefieldCard
                    {
                        Card = card,
                        IsYour = isYour,
                        IsLandZone = true,
                        X = 20 + fieldMz.Count(c => c.IsLandZone) * (FieldW + 6),
                        Y = 10
                    };
                    fieldMz.Add(bcMz);
                    RenderBattlefieldCard(bcMz, canvasMz);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(card); else _oppHand.Add(card);
                    RenderHand(isYour: true); UpdateHandCounts();
                    break;
                case "graveyard":
                    if (isYour) _yourGrave.Add(card); else _oppGrave.Add(card);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(card); else _oppExile.Add(card);
                    break;
            }
            UpdateZoneCounts();
        }
        // ================================================================
        // ================================================================
        // PHASE 9 -- NEW GAME RESTART
        // ================================================================
        private void BtnNewGame_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Start a new game? Current game state will be lost.",
                "New Game", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _yourField.Clear(); _oppField.Clear();
            _yourHand.Clear(); _oppHand.Clear();
            _yourGrave.Clear(); _oppGrave.Clear();
            _yourExile.Clear(); _oppExile.Clear();
            _yourLibrary.Clear(); _oppLibrary.Clear();
            _stackCards.Clear(); _linkedExiles.Clear();

            YourBattlefieldCanvas.Children.Clear();
            OppBattlefieldCanvas.Children.Clear();
            YourLandCanvas.Children.Clear();
            OppLandCanvas.Children.Clear();
            YourHandCanvas.Children.Clear();
            OppHandCanvas.Children.Clear();
            YourCommandCanvas.Children.Clear();
            OppCommandCanvas.Children.Clear();
            StackCanvas.Children.Clear();

            _yourCommander = null; _oppCommander = null;
            _yourCmdTax = 0; _oppCmdTax = 0;
            _yourCmdLocation = CommanderLocation.CommandZone;
            _oppCmdLocation = CommanderLocation.CommandZone;
            _mulliganCount = 0; _turnCounter = 1; _currentPhase = 0;

            UpdateZoneCounts(); UpdateHandCounts();
            UpdateMulliganDisplay();
            UpdateManaPoolSummary(); UpdatePhaseDisplay();
            SaveSetting(SavedGameKey, string.Empty);
            ShowNewGameDialog();
        }

        // ================================================================
        // PHASE 9 -- SCRY
        // ================================================================
        private void YourLibrary_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_yourLibrary.Count == 0) return;
            var menu = new ContextMenu();

            // Scry N
            var scryMenu = new MenuItem { Header = "🔮 Scry →" };
            for (int n = 1; n <= Math.Min(5, _yourLibrary.Count); n++)
            {
                int scryN = n;
                var mi = new MenuItem { Header = $"Scry {scryN}" };
                mi.Click += (s2, e2) => OpenScryWindow(scryN);
                scryMenu.Items.Add(mi);
            }
            menu.Items.Add(scryMenu);

            // Surveil N (look at top N, keep or send to graveyard)
            var surveilMenu = new MenuItem { Header = "👁 Surveil →" };
            for (int n = 1; n <= Math.Min(5, _yourLibrary.Count); n++)
            {
                int survN = n;
                var mi = new MenuItem { Header = $"Surveil {survN}" };
                mi.Click += (s2, e2) => OpenSurveilWindow(survN);
                surveilMenu.Items.Add(mi);
            }
            menu.Items.Add(surveilMenu);

            menu.Items.Add(new Separator());

            // Search Library (tutor)
            var search = new MenuItem { Header = "🔍 Search Library..." };
            search.Click += (s, e2) => SearchLibrary();
            menu.Items.Add(search);

            menu.Items.Add(new Separator());

            // Look at top N, put back in any order
            var lookMenu = new MenuItem { Header = "👀 Look at Top →" };
            for (int n = 1; n <= Math.Min(5, _yourLibrary.Count); n++)
            {
                int lookN = n;
                var mi = new MenuItem { Header = $"Top {lookN}" };
                mi.Click += (s2, e2) => OpenZoneBrowser(ZoneType.Library, isYour: true, topN: lookN);
                lookMenu.Items.Add(mi);
            }
            menu.Items.Add(lookMenu);

            menu.Items.Add(new Separator());

            var shuffle = new MenuItem { Header = "🔀 Shuffle Library" };
            shuffle.Click += (s, e2) => { Shuffle(_yourLibrary); UpdateZoneCounts(); AddLog("Shuffled library"); };
            menu.Items.Add(shuffle);

            menu.Items.Add(new Separator());

            var sleeve = new MenuItem { Header = "🎴 Select Card Sleeve..." };
            sleeve.Click += (s, e2) =>
            {
                var path = BrowseAndSaveTabletopImage(
                    "Select Your Card Sleeve Image",
                    AppFolderService.SleeveImagesFolder);
                if (path == null) return;
                LoadSleeveImage(path, isYour: true);
                SaveSetting(YourSleeveKey, path);
                RedrawAllCardBacks();
                AddLog("Your card sleeve changed");
            };
            menu.Items.Add(sleeve);

            var clearSleeve = new MenuItem { Header = "✕ Use Default Card Back" };
            clearSleeve.Click += (s, e2) =>
            {
                _yourSleeveImage = null;
                SaveSetting(YourSleeveKey, string.Empty);
                RedrawAllCardBacks();
                AddLog("Your card sleeve cleared");
            };
            menu.Items.Add(clearSleeve);

            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void OppLibrary_RightClick(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();

            var sleeve = new MenuItem { Header = "🎴 Select Card Sleeve..." };
            sleeve.Click += (s, e2) =>
            {
                var path = BrowseAndSaveTabletopImage(
                    "Select Opponent Card Sleeve Image",
                    AppFolderService.SleeveImagesFolder);
                if (path == null) return;
                LoadSleeveImage(path, isYour: false);
                SaveSetting(OppSleeveKey, path);
                RedrawAllCardBacks();
                AddLog("Opponent card sleeve changed");
            };
            menu.Items.Add(sleeve);

            var clearSleeve = new MenuItem { Header = "✕ Use Default Card Back" };
            clearSleeve.Click += (s, e2) =>
            {
                _oppSleeveImage = null;
                SaveSetting(OppSleeveKey, string.Empty);
                RedrawAllCardBacks();
                AddLog("Opponent card sleeve cleared");
            };
            menu.Items.Add(clearSleeve);

            (sender as FrameworkElement)!.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void OpenScryWindow(int n)
        {
            var topN = _yourLibrary.Take(n).ToList();
            var win = new ScryWindow(topN, n) { Owner = this };
            if (win.ShowDialog() != true) return;

            _yourLibrary.RemoveRange(0, Math.Min(n, _yourLibrary.Count));
            for (int i = win.KeepOnTop.Count - 1; i >= 0; i--)
                _yourLibrary.Insert(0, win.KeepOnTop[i]);
            foreach (var card in win.PutOnBottom)
                _yourLibrary.Add(card);
            UpdateZoneCounts();
        }

        // ── Surveil N — look at top N, keep or send to graveyard ─────────────
        private void OpenSurveilWindow(int n)
        {
            var topN = _yourLibrary.Take(n).ToList();
            if (topN.Count == 0) return;

            // Reuse MulliganWindow concept — show cards, click to mark for graveyard
            var win = new Window
            {
                Title = $"Surveil {n}",
                Width = Math.Max(460, n * 110 + 60),
                Height = 320,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Click a card to send it to the graveyard. Click again to keep it on top.",
                Foreground = Brushes.White,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var cardPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var toGrave = new HashSet<DeckCard>();

            foreach (var card in topN)
            {
                var captured = card;
                var border = new Border
                {
                    Width = 90,
                    Height = 126,
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    BorderThickness = new Thickness(2),
                    Margin = new Thickness(4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = captured.Name
                };

                if (!string.IsNullOrEmpty(captured.LocalImagePath)
                    && System.IO.File.Exists(captured.LocalImagePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(captured.LocalImagePath, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 90;
                        bmp.EndInit();
                        border.Child = new System.Windows.Controls.Image
                        { Source = bmp, Stretch = Stretch.Fill };
                    }
                    catch { border.Child = MakeCardBack(true); }
                }
                else border.Child = MakeCardBack(true);

                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (toGrave.Contains(captured))
                    {
                        toGrave.Remove(captured);
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                        border.BorderThickness = new Thickness(2);
                        border.Effect = null;
                    }
                    else
                    {
                        toGrave.Add(captured);
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x44, 0x44));
                        border.BorderThickness = new Thickness(3);
                        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        { Color = Colors.Red, BlurRadius = 10, ShadowDepth = 0 };
                    }
                };
                cardPanel.Children.Add(border);
            }
            root.Children.Add(cardPanel);

            var btnDone = new Button
            {
                Content = "✓ Done",
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(20, 6, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x66, 0x22)),
                Foreground = Brushes.White,
                FontSize = 12
            };
            btnDone.Click += (s, e) => win.DialogResult = true;
            root.Children.Add(btnDone);
            win.Content = root;

            if (win.ShowDialog() != true) return;

            // Apply: remove top N from library
            _yourLibrary.RemoveRange(0, Math.Min(n, _yourLibrary.Count));

            // Cards NOT going to grave stay on top (in original order)
            var keepOnTop = topN.Where(c => !toGrave.Contains(c)).ToList();
            for (int i = keepOnTop.Count - 1; i >= 0; i--)
                _yourLibrary.Insert(0, keepOnTop[i]);

            // Cards going to grave
            foreach (var c in toGrave)
                _yourGrave.Add(c);

            UpdateZoneCounts();
        }

        // ── Search Library — find a card by name, put it somewhere ───────────
        private void SearchLibrary()
        {
            if (_yourLibrary.Count == 0) return;

            var dlg = new InputDialog("Search Library", "Enter card name (partial match):");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;

            string query = dlg.Value.Trim();
            var matches = _yourLibrary
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            if (matches.Count == 0)
            {
                MessageBox.Show($"No cards matching \"{query}\" found in library.",
                    "Search Library", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pick which card if multiple matches
            DeckCard? chosen = matches.Count == 1 ? matches[0] : null;
            if (chosen == null)
            {
                var pickDlg = new InputDialog("Search Library",
                    "Multiple matches found. Enter exact name:\n" +
                    string.Join("\n", matches.Select(c => $"  • {c.Name}")));
                pickDlg.Owner = this;
                if (pickDlg.ShowDialog() != true) return;
                chosen = matches.FirstOrDefault(c =>
                    c.Name.Equals(pickDlg.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? matches[0];
            }

            // Ask what to do with it
            var destDlg = new Window
            {
                Title = $"Found: {chosen.Name}",
                Width = 280,
                Height = 200,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };

            DeckCard? finalChosen = chosen;
            string destination = "hand";

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = $"Found \"{chosen.Name}\". Put it where?",
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            foreach (var (label, dest) in new[]
            {
                ("🤚 Hand", "hand"),
                ("⬆ Library — Top", "libtop"),
                ("⬇ Library — Bottom", "libbot"),
                ("⚔ Battlefield", "battlefield"),
            })
            {
                var d = dest;
                var btn = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x55)),
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                btn.Click += (s, e) => { destination = d; destDlg.DialogResult = true; };
                panel.Children.Add(btn);
            }
            destDlg.Content = panel;

            if (destDlg.ShowDialog() != true) return;

            // Remove from library (first occurrence)
            int idx = _yourLibrary.IndexOf(finalChosen!);
            if (idx >= 0) _yourLibrary.RemoveAt(idx);

            switch (destination)
            {
                case "hand":
                    _yourHand.Add(finalChosen!);
                    RenderHand(isYour: true);
                    break;
                case "libtop":
                    _yourLibrary.Insert(0, finalChosen!);
                    break;
                case "libbot":
                    _yourLibrary.Add(finalChosen!);
                    break;
                case "battlefield":
                    var canvas = finalChosen!.IsLand ? YourLandCanvas : YourBattlefieldCanvas;
                    var (x, y) = AutoPosition(canvas, _yourField.Count, FieldW, FieldH);
                    var bc2 = new BattlefieldCard
                    {
                        Card = finalChosen,
                        X = x,
                        Y = y,
                        IsYour = true,
                        IsLandZone = finalChosen.IsLand
                    };
                    _yourField.Add(bc2);
                    RenderBattlefieldCard(bc2, canvas);
                    break;
            }

            // Always shuffle after search (standard MTG rule)
            Shuffle(_yourLibrary);
            UpdateZoneCounts();
            UpdateHandCounts();
            MessageBox.Show($"\"{finalChosen!.Name}\" moved to {destination}. Library shuffled.",
                "Search Library", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── OpenZoneBrowser with optional topN limit (for "Look at Top N") ───
        // (defined below after zone click handlers)

        private void TransformCard(BattlefieldCard bc)
        {
            bc.IsTransformed = !bc.IsTransformed;
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        private void YourLibrary_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Library, isYour: true);

        private void YourGraveyard_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Graveyard, isYour: true);

        private void YourExile_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Exile, isYour: true);

        private void OppLibrary_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Library, isYour: false);

        private void OppGraveyard_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Graveyard, isYour: false);

        private void OppExile_Click(object sender, MouseButtonEventArgs e)
            => OpenZoneBrowser(ZoneType.Exile, isYour: false);

        private void OpenZoneBrowser(ZoneType zone, bool isYour, int topN = 0)
        {
            var allCards = zone switch
            {
                ZoneType.Graveyard => isYour ? _yourGrave : _oppGrave,
                ZoneType.Exile => isYour ? _yourExile : _oppExile,
                ZoneType.Library => isYour ? _yourLibrary : _oppLibrary,
                _ => new List<DeckCard>()
            };

            // If topN specified, only show that many from the top
            var cards = topN > 0 ? allCards.Take(topN).ToList() : allCards;

            // Pass commander ref so browser can highlight and offer zone choice
            var cmdLoc = isYour ? _yourCmdLocation : _oppCmdLocation;
            var cmdCard = isYour ? _yourCommander : _oppCommander;
            DeckCard? cmdInZone = null;

            if (zone == ZoneType.Graveyard && cmdLoc == CommanderLocation.Graveyard)
                cmdInZone = cmdCard;
            else if (zone == ZoneType.Exile && cmdLoc == CommanderLocation.Exile)
                cmdInZone = cmdCard;
            else if (zone == ZoneType.Library && cmdLoc == CommanderLocation.Library)
                cmdInZone = cmdCard;

            // Opponent library is face-down — cards not revealed
            bool faceDown = zone == ZoneType.Library && !isYour;

            // Get linked exiles relevant to this zone
            var relevantLinks = zone == ZoneType.Exile
                ? _linkedExiles.Where(l => l.HostIsYour == isYour).ToList()
                : new List<LinkedExile>();

            var browser = new ZoneBrowserWindow(zone, isYour, cards, cmdInZone,
                faceDown: faceDown, linkedExiles: relevantLinks)
            {
                Owner = this
            };

            browser.OnMoveCard = (card, dest) =>
                ZoneBrowserMoveCard(card, dest, zone, isYour);

            browser.OnShuffle = () =>
            {
                Shuffle(isYour ? _yourLibrary : _oppLibrary);
            };

            browser.OnCopyCard = (card) =>
                CreateTokenCopy(card, isYour: true);

            browser.Show();
        }

        private void ZoneBrowserMoveCard(DeckCard card, string dest,
            ZoneType fromZone, bool isYour)
        {
            // Remove from source zone (browser already removed from its list)
            // The browser operates on the actual list reference so it's already gone

            bool isCmd = (isYour ? _yourCommander : _oppCommander)?.Name == card.Name;

            switch (dest)
            {
                case "command":
                    ApplyCommanderZoneMove(card, isYour, "command");
                    break;
                case "battlefield":
                    var fieldBf = isYour ? _yourField : _oppField;
                    var canvasBf = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;
                    var existingBf = fieldBf.Where(c => !c.IsLandZone).ToList();
                    var bcBf = new BattlefieldCard
                    {
                        Card = card,
                        IsYour = isYour,
                        IsLandZone = false,
                        X = 20 + (existingBf.Count % 8) * (FieldW + 6),
                        Y = 10 + (existingBf.Count / 8) * (FieldH + 6)
                    };
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Battlefield);
                    fieldBf.Add(bcBf);
                    RenderBattlefieldCard(bcBf, canvasBf);
                    break;
                case "manazone":
                    var fieldMz = isYour ? _yourField : _oppField;
                    var canvasMz = isYour ? YourLandCanvas : OppLandCanvas;
                    var existingMz = fieldMz.Where(c => c.IsLandZone).ToList();
                    var bcMz = new BattlefieldCard
                    {
                        Card = card,
                        IsYour = isYour,
                        IsLandZone = true,
                        X = 20 + (existingMz.Count % 8) * (FieldW + 6),
                        Y = 10 + (existingMz.Count / 8) * (FieldH + 6)
                    };
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Battlefield);
                    fieldMz.Add(bcMz);
                    RenderBattlefieldCard(bcMz, canvasMz);
                    break;
                case "graveyard":
                    if (isYour) _yourGrave.Add(card); else _oppGrave.Add(card);
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Graveyard);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(card); else _oppExile.Add(card);
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Exile);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(card); else _oppHand.Add(card);
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Hand);
                    RenderHand(isYour: true);
                    UpdateHandCounts();
                    break;
                case "libtop":
                    if (isYour) _yourLibrary.Insert(0, card);
                    else _oppLibrary.Insert(0, card);
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
                case "libbot":
                    if (isYour) _yourLibrary.Add(card);
                    else _oppLibrary.Add(card);
                    if (isCmd) SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
            }
            UpdateZoneCounts();
        }

        // ================================================================
        // MANA POOL
        // ================================================================
        private void ManaPool_Click(object sender, MouseButtonEventArgs e)
        {
            // Mana pool bar is display only — shows live land tap state
        }

        private void EmptyManaPool()
        {
            if (!_settingAutoEmptyMana) return;
            ManaPoolSummary.Text = "∅";
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                UpdateManaPoolSummary();
            };
            timer.Start();
        }

        private void UpdateManaPoolSummary()
        {
            var lands = _yourField.Where(c => c.IsLandZone).ToList();
            if (lands.Count == 0)
            {
                ManaPoolSummary.Text = "—";
                return;
            }

            var parts = new List<string>();
            var groups = lands.GroupBy(c => c.Card.Name).OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                int u = g.Count(c => !c.IsTapped);
                int t = g.Count(c => c.IsTapped);
                string symbol = g.Key switch
                {
                    "Plains" => "W",
                    "Island" => "U",
                    "Swamp" => "B",
                    "Mountain" => "R",
                    "Forest" => "G",
                    "Wastes" => "C",
                    _ => g.Key.Length > 6 ? g.Key[..6] : g.Key
                };
                parts.Add($"{symbol}:{u}↑{t}↷");
            }

            ManaPoolSummary.Text = string.Join("  ", parts);
        }

        // ================================================================
        // DICE
        // ================================================================
        private void BtnDice_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string header, Func<string> roll)
            {
                var item = new MenuItem { Header = header };
                item.Click += (s, _) =>
                    MessageBox.Show(roll(), "Dice Roll",
                        MessageBoxButton.OK, MessageBoxImage.None);
                menu.Items.Add(item);
            }
            Add("Flip Coin", () => CryptoNext(2) == 0 ? "Heads!" : "Tails!");
            Add("Roll d6", () => $"You rolled: {CryptoNext(6) + 1}");
            Add("Roll d20", () => $"You rolled: {CryptoNext(20) + 1}");
            Add("Roll d100", () => $"You rolled: {CryptoNext(100) + 1}");
            menu.IsOpen = true;
        }

        // ================================================================
        // STUBS
        // ================================================================
        private void BtnToken_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TokenCreatorWindow { Owner = this };
            if (dlg.ShowDialog() != true) return;

            foreach (var token in dlg.CreatedTokens)
            {
                var field = _yourField;
                bool toLand = dlg.ToManaZone;
                var canvas = toLand ? YourLandCanvas : YourBattlefieldCanvas;

                if (toLand)
                {
                    var bc = new BattlefieldCard
                    {
                        Card = token,
                        IsYour = true,
                        IsLandZone = true,
                        X = 0,
                        Y = 0
                    };
                    field.Add(bc);
                    RelayoutLandZone(isYour: true);
                }
                else
                {
                    var existing = field.Where(c => !c.IsLandZone).ToList();
                    var bc = new BattlefieldCard
                    {
                        Card = token,
                        IsYour = true,
                        IsLandZone = false,
                        X = 20 + (existing.Count % 8) * (FieldW + 6),
                        Y = 10 + (existing.Count / 8) * (FieldH + 6)
                    };
                    field.Add(bc);
                    RenderBattlefieldCard(bc, canvas);
                }
            }
            UpdateZoneCounts();
        }
        private void BtnUntapAll_Click(object sender, RoutedEventArgs e)
            => UntapAllYour();
        private void BtnTabletopSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TabletopSettingsWindow(
                defaultLife: _settingDefaultLife,
                autoDraw: _settingAutoDraw,
                autoEmptyMana: _settingAutoEmptyMana,
                showGameOver: _settingShowGameOver,
                blurOppHand: _settingBlurOppHand,
                tableColor: _settingTableColor,
                currentSleeve: GetSetting(YourSleeveKey) ?? string.Empty,
                currentYourPlaymat: GetSetting(YourPlaymatKey) ?? string.Empty,
                currentOppPlaymat: GetSetting(OppPlaymatKey) ?? string.Empty)
            { Owner = this };

            if (dlg.ShowDialog() != true) return;

            // Apply and persist all settings
            _settingDefaultLife = dlg.DefaultLife;
            _settingAutoDraw = dlg.AutoDraw;
            _settingAutoEmptyMana = dlg.AutoEmptyMana;
            _settingShowGameOver = dlg.ShowGameOver;
            _settingBlurOppHand = dlg.BlurOppHand;
            _settingTableColor = dlg.TableColor;

            SaveSetting(SettingDefaultLife, dlg.DefaultLife.ToString());
            SaveSetting(SettingAutoDraw, dlg.AutoDraw ? "1" : "0");
            SaveSetting(SettingAutoEmptyMana, dlg.AutoEmptyMana ? "1" : "0");
            SaveSetting(SettingShowGameOver, dlg.ShowGameOver ? "1" : "0");
            SaveSetting(SettingBlurOppHand, dlg.BlurOppHand ? "1" : "0");
            SaveSetting(SettingTableColor, dlg.TableColor);

            // Apply table color
            ApplyTableColor(dlg.TableColor);

            // Apply opponent hand blur
            OppHandBlur.Visibility = dlg.BlurOppHand
                ? Visibility.Visible : Visibility.Collapsed;

            // Apply sleeve changes
            if (dlg.SleepCleared)
            {
                _yourSleeveImage = null;
                _oppSleeveImage = null;
                SaveSetting(YourSleeveKey, string.Empty);
                SaveSetting(OppSleeveKey, string.Empty);
                RedrawAllCardBacks();
            }
            else if (dlg.NewSleevePath != null)
            {
                LoadSleeveImage(dlg.NewSleevePath, isYour: true);
                LoadSleeveImage(dlg.NewSleevePath, isYour: false);
                SaveSetting(YourSleeveKey, dlg.NewSleevePath);
                SaveSetting(OppSleeveKey, dlg.NewSleevePath);
                RedrawAllCardBacks();
            }

            // Apply playmat changes
            if (dlg.YourPlaymatCleared)
            {
                YourPlaymatImage.Source = null;
                SaveSetting(YourPlaymatKey, string.Empty);
            }
            else if (dlg.NewYourPlaymat != null)
            {
                SetPlaymat(YourPlaymatImage, dlg.NewYourPlaymat);
                SaveSetting(YourPlaymatKey, dlg.NewYourPlaymat);
            }

            if (dlg.OppPlaymatCleared)
            {
                OppPlaymatImage.Source = null;
                SaveSetting(OppPlaymatKey, string.Empty);
            }
            else if (dlg.NewOppPlaymat != null)
            {
                SetPlaymat(OppPlaymatImage, dlg.NewOppPlaymat);
                SaveSetting(OppPlaymatKey, dlg.NewOppPlaymat);
            }
        }

        private void LoadSettings()
        {
            _settingDefaultLife = int.TryParse(GetSetting(SettingDefaultLife), out int l)
                ? l : 20;
            _settingAutoDraw = (GetSetting(SettingAutoDraw) ?? "1") != "0";
            _settingAutoEmptyMana = (GetSetting(SettingAutoEmptyMana) ?? "1") != "0";
            _settingShowGameOver = (GetSetting(SettingShowGameOver) ?? "1") != "0";
            _settingBlurOppHand = (GetSetting(SettingBlurOppHand) ?? "1") != "0";
            _settingTableColor = GetSetting(SettingTableColor) ?? "Green";

            ApplyTableColor(_settingTableColor);
            OppHandBlur.Visibility = _settingBlurOppHand
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyTableColor(string colorName)
        {
            // Map color name to felt gradient stops
            var colors = colorName switch
            {
                "Blue" => ("#1A2D5A", "#162448", "#0E1830"),
                "Black" => ("#1A1A1A", "#141414", "#0A0A0A"),
                "Red" => ("#4A1A1A", "#3A1414", "#250D0D"),
                "Purple" => ("#2E1A4A", "#24143A", "#160D25"),
                "Brown" => ("#3A2A1A", "#2E2014", "#1E140D"),
                _ => ("#2D5A27", "#234820", "#152E14"), // Green default
            };

            Color Parse(string hex) => Color.FromRgb(
                Convert.ToByte(hex[1..3], 16),
                Convert.ToByte(hex[3..5], 16),
                Convert.ToByte(hex[5..7], 16));

            // Find the main table border (first child of root grid that uses FeltBrush)
            // We update it directly rather than replacing the resource
            if (TableFeltBorder != null)
            {
                TableFeltBorder.Background = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.8,
                    RadiusY = 0.8,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Parse(colors.Item1), 0.0),
                        new GradientStop(Parse(colors.Item2), 0.5),
                        new GradientStop(Parse(colors.Item3), 1.0),
                    }
                };
            }
        }

        private void RedrawAllCardBacks()
        {
            // Redraw all battlefield cards (updates card backs for face-down/sleeve)
            RedrawBattlefield(isYour: true);
            RedrawBattlefield(isYour: false);
            RenderHand(isYour: true);
            RenderHand(isYour: false);
            RenderLibrary(isYour: true);
            RenderLibrary(isYour: false);
        }

        // Public static so TabletopSettingsWindow can call it without an instance
        public static string? BrowseTabletopImageStatic(string title, string targetFolder)
            => BrowseAndSaveTabletopImage(title, targetFolder);
        // ================================================================
        // GAME LOG
        // ================================================================
        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{timestamp}] {message}";
            _gameLog.Add(entry);

            // Auto-scroll to bottom
            if (LogListBox.Items.Count > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
            => _gameLog.Clear();

        // ================================================================
        // FILE MENU HANDLERS
        // ================================================================
        private void MenuNewGame_Click(object sender, RoutedEventArgs e)
            => BtnNewGame_Click(sender, e);

        private void MenuYourSleeve_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseAndSaveTabletopImage(
                "Select Your Card Sleeve Image",
                AppFolderService.SleeveImagesFolder);
            if (path == null) return;
            LoadSleeveImage(path, isYour: true);
            SaveSetting(YourSleeveKey, path);
            RedrawAllCardBacks();
            AddLog("Your card sleeve changed");
        }

        private void MenuOppSleeve_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseAndSaveTabletopImage(
                "Select Opponent Card Sleeve Image",
                AppFolderService.SleeveImagesFolder);
            if (path == null) return;
            LoadSleeveImage(path, isYour: false);
            SaveSetting(OppSleeveKey, path);
            RedrawAllCardBacks();
            AddLog("Opponent card sleeve changed");
        }

        private void MenuClearSleeves_Click(object sender, RoutedEventArgs e)
        {
            _yourSleeveImage = null;
            _oppSleeveImage = null;
            SaveSetting(YourSleeveKey, string.Empty);
            SaveSetting(OppSleeveKey, string.Empty);
            RedrawAllCardBacks();
            AddLog("All card sleeves cleared");
        }

        private void MenuYourPlaymat_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseAndSaveTabletopImage(
                "Select Your Playmat Image",
                AppFolderService.PlaymatImagesFolder);
            if (path == null) return;
            SetPlaymat(YourPlaymatImage, path);
            SaveSetting(YourPlaymatKey, path);
            AddLog("Your playmat changed");
        }

        private void MenuOppPlaymat_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseAndSaveTabletopImage(
                "Select Opponent Playmat Image",
                AppFolderService.PlaymatImagesFolder);
            if (path == null) return;
            SetPlaymat(OppPlaymatImage, path);
            SaveSetting(OppPlaymatKey, path);
            AddLog("Opponent playmat changed");
        }

        private void MenuClearPlaymats_Click(object sender, RoutedEventArgs e)
        {
            YourPlaymatImage.Source = null;
            OppPlaymatImage.Source = null;
            SaveSetting(YourPlaymatKey, string.Empty);
            SaveSetting(OppPlaymatKey, string.Empty);
            AddLog("Playmats cleared");
        }

        private void MenuToggleLog_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.Visibility = LogPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnTabletopClose_Click(object sender, RoutedEventArgs e)
        {
            // Auto-save on close
            SaveGameState();
            Close();
        }
    }

    // ================================================================
    // SIMPLE INPUT DIALOG — for custom counter type
    // ================================================================
    public class InputDialog : Window
    {
        private readonly TextBox _box;
        public string Value => _box.Text;

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 320; Height = 130;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock
            { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
            _box = new TextBox { Height = 26, Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(_box);
            var btn = new Button
            { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
            btn.Click += (s, e) => { DialogResult = true; };
            stack.Children.Add(btn);
            Content = stack;
            _box.Focus();
        }
    }

    // ================================================================
    // GAME STATE — serializable snapshot
    // ================================================================
    public class SavedBattlefieldCard
    {
        public string CardName { get; set; } = "";
        public bool IsTapped { get; set; }
        public bool IsTransformed { get; set; }
        public bool IsFaceDown { get; set; }
        public bool IsLandZone { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public Dictionary<string, int> Counters { get; set; } = new();
        public List<string> TempEffects { get; set; } = new(); // "Label|P|T"
    }

    public class GameState
    {
        public string YourPlayerName { get; set; } = "Player 1";
        public string OppPlayerName { get; set; } = "Player 2";
        public int YourLife { get; set; } = 20;
        public int OppLife { get; set; } = 20;
        public int YourPoison { get; set; }
        public int OppPoison { get; set; }
        public int YourCmdDmg { get; set; }
        public int OppCmdDmg { get; set; }
        public int YourCmdTax { get; set; }
        public int OppCmdTax { get; set; }
        public int TurnCounter { get; set; } = 1;
        public int CurrentPhase { get; set; }
        // Modern mechanics
        public string MonarchHolder { get; set; } = "";
        public string InitiativeHolder { get; set; } = "";
        public int YourEnergy { get; set; }
        public int OppEnergy { get; set; }
        public int RingLevel { get; set; }
        public string RingBearer { get; set; } = "";
        // Zones
        public List<string> YourLibrary { get; set; } = new();
        public List<string> OppLibrary { get; set; } = new();
        public List<string> YourHand { get; set; } = new();
        public List<string> OppHand { get; set; } = new();
        public List<string> YourGrave { get; set; } = new();
        public List<string> OppGrave { get; set; } = new();
        public List<string> YourExile { get; set; } = new();
        public List<string> OppExile { get; set; } = new();
        // Battlefield
        public List<SavedBattlefieldCard> YourBattlefield { get; set; } = new();
        public List<SavedBattlefieldCard> OppBattlefield { get; set; } = new();
    }
}