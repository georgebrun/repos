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

            RootNavigation.Navigated += RootNavigation_Navigated;
            UpdateModeSwitch();

            // Sections start closed and stacked: click View to open the listings.
            ApplySection(NavSection.None);

            // Land on the main Cards pool by default.
            Loaded += (_, _) =>
                RootNavigation.Navigate(typeof(PoolPage));
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

            // Edit items (placeholder until editing exists)
            var editVis = open == NavSection.Edit ? Visibility.Visible : Visibility.Collapsed;
            NavEditPoolToCollection.Visibility = editVis;
            NavEditOnline.Visibility = editVis;
            NavEditDecks.Visibility = editVis;

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

        private void RootNavigation_Navigated(
            NavigationView sender, NavigatedEventArgs args)
        {
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