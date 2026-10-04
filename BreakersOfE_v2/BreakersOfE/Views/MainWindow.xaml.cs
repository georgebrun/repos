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
            Application.Current.MainWindow = this;      // (the first-run folder question may have opened first)

            RootNavigation.Navigated += RootNavigation_Navigated;
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
                        "connect it and restart BoE (or pick it again in Settings → Data folder).",
                        "Data Folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            };
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

        /// <summary>Settings (pane footer).</summary>
        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            RootNavigation.Navigate(typeof(SettingsPage));

        /// <summary>Settings → Open on: the page the app starts on.</summary>
        private void OpenStartPage()
        {
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
                if (days > 0 && (age < 0 || age >= days))
                {
                    UpdateReminder.Text = age < 0
                        ? "No card data update yet — click to update."
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

        private void RootNavigation_Navigated(
            NavigationView sender, NavigatedEventArgs args)
        {
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