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

            // Land on the main Cards pool by default.
            Loaded += (_, _) =>
                RootNavigation.Navigate(typeof(PoolPage));
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
                page.LoadPool(poolTag);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}