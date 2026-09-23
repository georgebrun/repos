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

            // Land on the main Cards pool by default.
            Loaded += (_, _) =>
                RootNavigation.Navigate(typeof(PoolPage));
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
                else
                    page.LoadPool(poolTag);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}