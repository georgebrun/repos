using Wpf.Ui.Controls;

namespace BreakersOfE.Views
{
    /// <summary>
    /// MainWindow — the app shell.
    /// 
    /// In v1 this file was 10,000+ lines handling everything.
    /// In v2 it's tiny — just the window frame. All logic lives
    /// in ViewModels and Services.
    /// 
    /// The NavigationView in the XAML handles page switching
    /// automatically based on which menu item is clicked.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Wait until the window is fully loaded before navigating —
            // the NavigationView isn't ready during the constructor
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Navigate to Dashboard on startup
            RootNavigation.Navigate(typeof(Pages.DashboardPage));
        }
    }
}