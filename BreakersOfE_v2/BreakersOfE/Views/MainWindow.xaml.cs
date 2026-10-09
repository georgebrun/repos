using System.Windows;
using BreakersOfE.Views.Pages;
using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Application.Current.MainWindow = this;      // (the v1 conversion window may have opened first)

            RootNavigation.Navigated += RootNavigation_Navigated;
            // The Program Tour: starts on its own until Do Not Show Again (or its last stop).
            Tour.Ended += Tour_Ended;

            // An update finished: the reminder goes, and (after v1) foil / etched gets sorted out.
            Services.AgentCoordinator.Updated += OnUpdateRecorded;
            Closed += (_, _) => Services.AgentCoordinator.Updated -= OnUpdateRecorded;

            // F1: Help on the topic for the page on screen.
            PreviewKeyDown += (_, e) =>
            {
                // While the tour is up, its keys (← → Enter Esc) and nothing else.
                if (Tour.IsRunning && !Tour.IsKeyboardFocusWithin) { Tour.Focus(); e.Handled = true; return; }
                if (e.Key != System.Windows.Input.Key.F1) return;
                HelpWindow.Open(HelpTopicHere());
                e.Handled = true;
            };
            UpdateModeSwitch();

            // Sections start closed and stacked: click View to open the listings.
            ApplySection(NavSection.None);

            // Land on the start page chosen in Settings (the main Cards pool by default).
            Loaded += (_, _) =>
            {
                OpenStartPage();
                ShowUpdateReminder();
                // The data folder from Settings couldn't be used (drive not connected …): say so now,
                // before anything gets added to the default folder by mistake.
                if (Services.AppFolderService.DataFolderProblem.Length > 0) App.CloseSplash(now: true);   // the message below must be seen
                if (Services.AppFolderService.DataFolderProblem.Length > 0)
                    System.Windows.MessageBox.Show(this,
                        Services.AppFolderService.DataFolderProblem +
                        "\n\nChanges you make now are saved in the default folder. To use your data folder, " +
                        "connect it and restart BoE.",
                        "Data Folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StartTourWhenReady();
                _ = CheckForNewVersionAtStart();
            };
        }

        /// <summary>
        /// Settings → Check for new versions when BoE starts: quietly ask GitHub;
        /// a message only when there's a newer BoE. Not on a first start (the
        /// conversion and card download come first), and never over the startup
        /// picture, the Program Tour or another window.
        /// </summary>
        private async System.Threading.Tasks.Task CheckForNewVersionAtStart()
        {
            if (App.OpenUpdateDatabaseFirst || !Services.AppSettingsService.Current.CheckForNewVersions) return;
            Services.ReleaseCheck.Release latest;
            try
            {
                latest = await Services.ReleaseCheck.GetLatestAsync();
            }
            catch
            {
                return;                                    // offline, GitHub busy: say nothing
            }
            if (!Services.ReleaseCheck.IsNewer(latest)) return;

            // Wait for a quiet moment (gives up after 10 minutes; the next start asks again).
            var waited = System.Diagnostics.Stopwatch.StartNew();
            int calm = 0;
            while (waited.Elapsed < TimeSpan.FromMinutes(10))
            {
                bool busy = App.SplashOpen || Tour.IsRunning || OwnedWindows.Count > 0 || !IsActive;
                calm = busy ? 0 : calm + 1;
                if (calm >= 3) break;                      // 3 s with nothing else going on (the tour starts just after the splash)
                await System.Threading.Tasks.Task.Delay(1000);
            }
            if (calm < 3) return;
            Dialogs.NewVersionWindow.Ask(this, latest);
        }

        // ── View / Edit section accordion ───────────────────────────────
        // Like an accordion panel: at most one section open. Closed section
        // buttons stack at the top; opening View drops Edit to the bottom of
        // the pane (above Update Database) with View's items between them;
        // opening Edit puts it right under View with its items below. Click
        // an open section again to close it.
        private enum NavSection { None, View, Edit }
        private NavSection _openSection = NavSection.View;   // the XAML's starting layout

        /// <summary>Update Database (pane footer, always last): open the update page.</summary>
        private void BtnUpdateDatabase_Click(object sender, RoutedEventArgs e) =>
            RootNavigation.Navigate(typeof(DatabaseUpdatePage));

        /// <summary>Help (pane footer): its own window, on the topic it showed last.</summary>
        private void BtnHelp_Click(object sender, RoutedEventArgs e) => HelpWindow.Open();

        /// <summary>Settings (pane footer).</summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            RootNavigation.Navigate(typeof(SettingsPage));

        /// <summary>Settings → Open on: the page the app starts on.</summary>
        private void OpenStartPage()
        {
            // No card data yet (a new folder, or just converted from v1): the full
            // update comes first, and starts by itself.
            if (App.OpenUpdateDatabaseFirst)
            {
                DatabaseUpdatePage.StartFullUpdateOnOpen = true;
                RootNavigation.Navigate(typeof(DatabaseUpdatePage));
                return;
            }
            string start = Services.AppSettingsService.Current.StartPage;
            switch (start)
            {
                case "keywords":
                    RootNavigation.Navigate(typeof(KeywordDictionaryPage));
                    return;
                case "collection":
                case "decks":
                case "sets":
                    ApplySection(NavSection.View);
                    if (start == "collection") NavCollection.IsExpanded = true;
                    if (RootNavigation.Navigate(start == "collection" ? "coll-cards" : start == "decks" ? "view-decks" : "view-sets"))
                        return;
                    break;
            }
            RootNavigation.Navigate(typeof(PoolPage));
        }

        /// <summary>Settings → Remind me to update: an amber line above Update Database when the card data is old.</summary>
        private void ShowUpdateReminder()
        {
            try
            {
                int days = Services.AppSettingsService.Current.UpdateReminderDays;
                var st = new Services.AgentCoordinator().ReadStatus();
                // A prices-only update counts too (prices are what get old fastest).
                DateTime? last = st.LastPoolUpdate > st.LastPriceUpdate || st.LastPriceUpdate == null ? st.LastPoolUpdate : st.LastPriceUpdate;
                int age = last.HasValue ? (int)(DateTime.UtcNow - last.Value).TotalDays : -1;
                // No update recorded at all (a new or just-converted folder): the card data is
                // needed, whatever the reminder setting. Otherwise: when it's older than the setting.
                if (age < 0 || (days > 0 && age >= days))
                {
                    UpdateReminder.Text = age < 0
                        ? "Please update the database to continue."
                        : $"Card data and prices are {age} days old — click to update.";
                    UpdateReminder.Visibility = Visibility.Visible;
                }
                else UpdateReminder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                UpdateReminder.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateReminder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            RootNavigation.Navigate(typeof(DatabaseUpdatePage));

        /// <summary>Keyword Dictionary (pane footer): the keyword reference page.</summary>
        private void BtnKeywordDictionary_Click(object sender, RoutedEventArgs e) =>
            RootNavigation.Navigate(typeof(KeywordDictionaryPage));

        /// <summary>
        /// Keyword Dictionary → "Show in Pool / My Collection": open View on
        /// Card Pool \ Cards or Collection \ Cards, filtered to that keyword.
        /// </summary>
        public void ShowKeyword(string keyword, bool collection)
        {
            PoolPage.RequestKeyword(collection ? "Collection" : "Cards", keyword);
            if (_openSection != NavSection.View) ApplySection(NavSection.View);
            if (collection) NavCollection.IsExpanded = true;
            else NavCardPool.IsExpanded = true;
            if (!RootNavigation.Navigate(collection ? "coll-cards" : "pool-cards"))
                RootNavigation.Navigate(typeof(PoolPage));
        }

        private void BtnSectionView_Click(object sender, RoutedEventArgs e) => ToggleSection(NavSection.View);
        private void BtnSectionEdit_Click(object sender, RoutedEventArgs e) => ToggleSection(NavSection.Edit);

        private void ToggleSection(NavSection section) =>
            ApplySection(_openSection == section ? NavSection.None : section);

        private bool _viewOpenedOnce;

        private void ApplySection(NavSection open)
        {
            _openSection = open;

            // First time View opens: Card Pool expanded (the app starts on
            // Card Pool \ Cards), everything else collapsed. After that the
            // tree keeps whatever you expanded.
            if (open == NavSection.View && !_viewOpenedOnce)
            {
                _viewOpenedOnce = true;
                NavCardPool.IsExpanded = true;
                NavCollection.IsExpanded = false;
                NavOnline.IsExpanded = false;
            }

            // View items
            var viewVis = open == NavSection.View ? Visibility.Visible : Visibility.Collapsed;
            NavCardPool.Visibility = viewVis;
            NavSets.Visibility = viewVis;
            NavCollection.Visibility = viewVis;
            NavDecks.Visibility = viewVis;
            NavOnline.Visibility = viewVis;

            // Edit items
            var editVis = open == NavSection.Edit ? Visibility.Visible : Visibility.Collapsed;
            NavEditPoolToCollection.Visibility = editVis;
            NavEditOnline.Visibility = editVis;
            NavEditDecks.Visibility = editVis;
            NavEditLists.Visibility = editVis;
            NavEditImport.Visibility = editVis;

            // Edit button: at the bottom only while View is open; otherwise
            // stacked at the top under View.
            if (BtnSectionEdit.Parent is System.Windows.Controls.Panel from)
                from.Children.Remove(BtnSectionEdit);
            (open == NavSection.View ? BottomSections : TopSections).Children.Add(BtnSectionEdit);

            // Arrows: ▾ open, ▸ closed
            BtnSectionView.Icon = new SymbolIcon
            {
                Symbol = open == NavSection.View ? SymbolRegular.ChevronDown24 : SymbolRegular.ChevronRight24
            };
            BtnSectionEdit.Icon = new SymbolIcon
            {
                Symbol = open == NavSection.Edit ? SymbolRegular.ChevronDown24 : SymbolRegular.ChevronRight24
            };
        }

        // ── Open a deck in Edit → Decks → Deck → Collection ────────────
        /// <summary>A deck to open when the Deck → Collection page arrives (from a "Used in" table).</summary>
        private string? _pendingDeckPath;

        /// <summary>
        /// "Used in" table → double-click a deck: open Edit, go to Decks →
        /// Deck → Collection, and open that deck there.
        /// </summary>
        public void OpenDeckToCollection(string deckPath)
        {
            _pendingDeckPath = deckPath;
            if (_openSection != NavSection.Edit) ApplySection(NavSection.Edit);
            NavEditDecks.IsExpanded = true;
            if (!RootNavigation.Navigate("editdeck-d2c"))
                RootNavigation.Navigate(typeof(EditDeckPage));
        }

        // ── Grid / Gallery switch ───────────────────────────────────────
        private void BtnModeGrid_Click(object sender, RoutedEventArgs e)
        {
            Services.CardViewModeService.Set(Services.CardViewMode.Grid);
            UpdateModeSwitch();
        }

        private void BtnModeGallery_Click(object sender, RoutedEventArgs e)
        {
            Services.CardViewModeService.Set(Services.CardViewMode.Gallery);
            UpdateModeSwitch();
        }

        private void BtnModeCompact_Click(object sender, RoutedEventArgs e)
        {
            bool gallery = Services.CardViewModeService.Mode == Services.CardViewMode.Gallery;
            Services.CardViewModeService.Set(gallery ? Services.CardViewMode.Grid
                                                     : Services.CardViewMode.Gallery);
            UpdateModeSwitch();
        }

        /// <summary>Highlight the active side of the switch; compact button shows the current mode.</summary>
        private void UpdateModeSwitch()
        {
            bool gallery = Services.CardViewModeService.Mode == Services.CardViewMode.Gallery;
            BtnModeGrid.Appearance = gallery ? ControlAppearance.Secondary : ControlAppearance.Primary;
            BtnModeGallery.Appearance = gallery ? ControlAppearance.Primary : ControlAppearance.Secondary;

            BtnModeCompact.Icon = new SymbolIcon
            {
                Symbol = gallery ? SymbolRegular.Image24 : SymbolRegular.Grid24
            };
            BtnModeCompact.ToolTip = gallery
                ? "Gallery view (click for Grid)"
                : "Grid view (click for Gallery)";
        }

        private bool _splashWaiting = true;

        /// <summary>The startup picture stays until the first page shows its cards (or any other page opens).</summary>
        private void CloseSplashWhenReady(object page)
        {
            if (!_splashWaiting) return;
            _splashWaiting = false;
            if (page is PoolPage pool)
            {
                void Ready()
                {
                    pool.ItemsReloaded -= Ready;
                    App.CloseSplash();
                }
                pool.ItemsReloaded += Ready;
            }
            else App.CloseSplash();
        }

        // ══════════════════════════════════════════════════════════════════
        // PROGRAM TOUR
        // ══════════════════════════════════════════════════════════════════

        /// <summary>At startup: the tour, once the startup picture has gone (unless turned off).</summary>
        private void StartTourWhenReady()
        {
            bool review = FinishReviewReady();
            if (!review && !Services.AppSettingsService.Current.ShowTour) return;
            if (App.OpenUpdateDatabaseFirst) return;        // no card data yet: the update first, the tour next time
            var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            int tries = 0;
            wait.Tick += (_, _) =>
            {
                if (App.SplashOpen && ++tries < 100) return;       // (gives up waiting after 40 s)
                wait.Stop();
                // Converted from v1, card data in, foil / etched not sorted out yet: that first (the tour next time).
                if (review) { CheckFinishReview(); return; }
                if (OwnedWindows.Count == 0 && IsActive) StartTour();
                else if (OwnedWindows.Count == 0) Activated += StartOnce;   // another window was in front: when BoE is back
            };
            wait.Start();

            void StartOnce(object? s, EventArgs e)
            {
                Activated -= StartOnce;
                if (!Tour.IsRunning) StartTour();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // AFTER v1 → v2: FOIL OR ETCHED (needs the card data)
        // ══════════════════════════════════════════════════════════════════

        private bool _reviewing;

        /// <summary>Converted from v1, the foil / etched step still to do, and the card data is in.</summary>
        private static bool FinishReviewReady()
        {
            try
            {
                return Services.AppSettingsService.Current.FinishReviewPending &&
                       new Services.AgentCoordinator().ReadStatus().LastPoolUpdate != null;
            }
            catch { return false; }
        }

        /// <summary>An update was recorded: the reminder, and (card data) the v1 foil / etched step.</summary>
        private void OnUpdateRecorded(bool cardData) => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowUpdateReminder();
            if (cardData) CheckFinishReview();
        }));

        /// <summary>
        /// v1 kept etched copies as foil. Etched-only printings are set to etched
        /// now; printings that come in both are asked about (Foil or Etched?).
        /// Done when applied (or nothing to ask); "Later" asks again next start.
        /// </summary>
        private void CheckFinishReview()
        {
            if (_reviewing || !FinishReviewReady()) return;
            _reviewing = true;
            try
            {
                int fixedCount;
                List<Services.FinishQuestion> ask;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try { (fixedCount, ask) = Services.CollectionEditService.PrepareV1FinishReview(); }
                finally { System.Windows.Input.Mouse.OverrideCursor = null; }

                bool done;
                if (ask.Count > 0) done = Dialogs.FinishReviewWindow.Ask(this, ask, fixedCount);
                else
                {
                    done = true;
                    if (fixedCount > 0)
                        System.Windows.MessageBox.Show(this,
                            fixedCount == 1
                                ? "1 printing that only comes in etched was kept as foil by v1. It's set to etched now."
                                : $"{fixedCount} printings that only come in etched were kept as foil by v1. They're set to etched now.",
                            "Etched Cards", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                if (done)
                {
                    var s = Services.AppSettingsService.Current;
                    s.FinishReviewPending = false;
                    Services.AppSettingsService.Save(s);
                }
                if (fixedCount > 0 || done) (_page as PoolPage)?.ReloadRows(null);   // the table on screen shows the new finishes
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this,
                    "Couldn't sort out foil and etched copies from v1: " + ex.Message +
                    "\n\nNothing is lost — BoE tries again the next time it starts.",
                    "Foil or Etched", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                _reviewing = false;
            }
        }

        /// <summary>
        /// Show the tour. From Settings (Restart Tour): turned back on for
        /// startup, and shown from the Card Pool.
        /// </summary>
        public void StartTour(bool goHome = false)
        {
            if (goHome)
            {
                var s = Services.AppSettingsService.Current;
                if (!s.ShowTour)
                {
                    s.ShowTour = true;
                    Services.AppSettingsService.Save(s);
                }
                if (_openSection != NavSection.View) ApplySection(NavSection.View);
                NavCardPool.IsExpanded = true;
                if (!RootNavigation.Navigate("pool-cards")) RootNavigation.Navigate(typeof(PoolPage));
            }
            RootNavigation.IsPaneOpen = true;                  // the tour points at the side menu's buttons
            Dispatcher.BeginInvoke(new Action(() => Tour.Start(TourSteps(), doNotShowTicked: false)),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>The last stop's Finish, or Do Not Show Again: not again at startup. Skip / ✕: next time again.</summary>
        private void Tour_Ended(bool finished, bool doNotShowAgain)
        {
            if (!finished && !doNotShowAgain) return;
            var s = Services.AppSettingsService.Current;
            s.ShowTour = false;
            Services.AppSettingsService.Save(s);
        }

        private IReadOnlyList<Controls.TourStep> TourSteps() => new List<Controls.TourStep>
        {
            new("Welcome to Breakers of E",
                "A quick look around: where things are and what they're for. Use Next (or →) to go on, Back (←) to go back. " +
                "Skip Tour or ✕ leaves it; tick Do Not Show Again if you don't want it when BoE starts.",
                () => null),
            new("Grid or Gallery",
                "Show cards as a table (Grid) or as pictures (Gallery), everywhere in the app.",
                () => BtnModeGrid.Parent as FrameworkElement),
            new("View",
                "Look at the card pool, sets, your collection, decks and online cards. Nothing can be changed here. " +
                "Click View to open or close its list.",
                () => BtnSectionView),
            new("Edit",
                "Change things: add cards to your collection, build decks, fill the Trade Binder and Want List, and import or export lists.",
                () => BtnSectionEdit),
            new("The page",
                "What you pick in the side menu shows here. On card pages: type a name in Search, use Filters and Columns above the table, " +
                "click a card for its details on the left, and double-click it for the full card window.",
                () => _page as FrameworkElement),
            new("Keyword Dictionary",
                "Look up any keyword: what it means, the official rules, and the cards that have it.",
                () => BtnKeywordDictionary),
            new("Help",
                "How BoE works, step-by-step How-Tos, and every keyboard shortcut. Press F1 anywhere for help on the page you're on.",
                () => BtnHelp),
            new("Settings",
                "Light, Dark or your own colors, collection defaults, card pictures, your data folder — and Restart Tour, to see this tour again.",
                () => BtnSettings),
            new("Update Database",
                "Downloads every card, its prices and the rulings from Scryfall. Do this first, then now and then for new cards and prices.",
                () => BtnUpdateDatabase),
            new("That's the tour",
                "Next steps: Update Database, then add your cards (Help → How To has step-by-step guides). Enjoy!",
                () => null),
        };

        /// <summary>The page on screen (for F1).</summary>
        private object? _page;

        /// <summary>
        /// The Help topic for the page on screen (F1): the page type, and for
        /// the shared table page which side-menu item opened it.
        /// </summary>
        private string HelpTopicHere()
        {
            string tag = (RootNavigation.SelectedItem as NavigationViewItem)?.Tag as string ?? "";
            return _page switch
            {
                KeywordDictionaryPage => "keyword-dictionary",
                SettingsPage => "settings",
                DatabaseUpdatePage => "update-database",
                ImportExportPage => "import-export",
                EditDeckPage => "decks",
                EditTradeBinderPage => "trade-binder",
                EditWantListPage => "want-list",
                EditCollectionPage => tag is "Edit:MtgoCards" or "Edit:ArenaCards" ? "online" : "edit-collection",
                PoolPage => tag switch
                {
                    "Sets" => "sets",
                    "Decks" => "decks",
                    "TradeBinder" => "trade-binder",
                    "WantList" => "want-list",
                    _ when tag.StartsWith("Mtgo") || tag.StartsWith("Arena") => "online",
                    _ when tag == "Collection" || tag.StartsWith("Coll") => "view-and-edit",
                    _ => "card-pool",
                },
                _ => HelpWindow.StartTopic,
            };
        }

        private void RootNavigation_Navigated(
            NavigationView sender, NavigatedEventArgs args)
        {
            _page = args.Page;
            CloseSplashWhenReady(args.Page);
            // After an update (or a Settings change) the reminder may no longer apply.
            ShowUpdateReminder();

            // Edit → Pool → Collection: tag "Edit:<pool>" picks the table pair.
            if (args.Page is EditCollectionPage editPage)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string tag = (sender.SelectedItem as NavigationViewItem)?.Tag as string ?? "";
                    editPage.LoadPair(tag.StartsWith("Edit:") ? tag.Substring(5) : "Cards");
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            // Edit → Decks → Pool → Deck.
            // Edit → Decks: tag "EditDeck:<mode>" picks Pool → Deck, Collection → Deck or Deck → Collection.
            if (args.Page is EditDeckPage deckPage)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string tag = (sender.SelectedItem as NavigationViewItem)?.Tag as string ?? "";
                    var mode = tag switch
                    {
                        "EditDeck:Collection" => DeckEditMode.Collection,
                        "EditDeck:DeckToCollection" => DeckEditMode.DeckToCollection,
                        _ => DeckEditMode.Pool,
                    };
                    // Opened from a "Used in" table: Deck → Collection with that deck.
                    string? open = _pendingDeckPath;
                    _pendingDeckPath = null;
                    if (open != null) mode = DeckEditMode.DeckToCollection;
                    deckPage.Start(mode, open);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            // Edit → Lists → Collection → Trade Binder.
            if (args.Page is EditTradeBinderPage binderPage)
            {
                Dispatcher.BeginInvoke(new Action(binderPage.Start), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            // Edit → Lists → Pool → Want List.
            if (args.Page is EditWantListPage wantPage)
            {
                Dispatcher.BeginInvoke(new Action(wantPage.Start), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            if (args.Page is not PoolPage page)
                return;

            // SelectedItem lags behind the Navigated event in Wpf.Ui —
            // defer reading it until the dispatcher has processed the
            // selection update, so we get the NEWLY selected item.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string poolTag = "Cards";
                if (sender.SelectedItem is NavigationViewItem item &&
                    item.Tag is string tag &&
                    !string.IsNullOrEmpty(tag))
                {
                    poolTag = tag;
                }

                if (poolTag == "Sets")
                    page.ShowSets();
                else if (poolTag == "Decks")
                    page.ShowDecks();
                else
                    page.LoadPool(poolTag);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}