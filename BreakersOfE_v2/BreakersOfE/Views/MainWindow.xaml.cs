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

            // When a pool sub-item is clicked, hand its Tag to the PoolPage
            // so the page knows which table to load.
            RootNavigation.Navigated += RootNavigation_Navigated;

            // Land on the main Cards pool by default.
            Loaded += (_, _) =>
                RootNavigation.Navigate(typeof(PoolPage));
        }

        private void RootNavigation_Navigated(
            NavigationView sender, NavigatedEventArgs args)
        {
            // The page instance we just navigated to
            if (args.Page is not PoolPage page)
                return;

            // Find the selected nav item and read its Tag ("Cards", "Tokens", …)
            if (sender.SelectedItem is NavigationViewItem item &&
                item.Tag is string poolTag &&
                !string.IsNullOrEmpty(poolTag))
            {
                page.LoadPool(poolTag);
            }
            else
            {
                // Default when the parent "Card Pool" item itself is hit
                page.LoadPool("Cards");
            }
        }
    }
}