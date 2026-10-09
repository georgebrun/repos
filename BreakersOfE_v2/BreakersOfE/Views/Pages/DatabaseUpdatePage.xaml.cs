using System.Windows.Controls;

namespace BreakersOfE.Views.Pages
{
    /// <summary>
    /// Database Update page.
    /// 
    /// Notice how tiny this code-behind is compared to v1.
    /// ALL logic is in DatabaseUpdateViewModel — this file
    /// just initializes the page. The XAML handles everything
    /// else through data binding.
    /// </summary>
    public partial class DatabaseUpdatePage : Page
    {
        /// <summary>
        /// Set at startup when no card data update is on record (a new folder, or
        /// just converted from v1): the next time this page opens, the full update
        /// (Update Database) starts by itself. Used once.
        /// </summary>
        public static bool StartFullUpdateOnOpen { get; set; }

        public DatabaseUpdatePage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (!StartFullUpdateOnOpen) return;
                StartFullUpdateOnOpen = false;
                if (DataContext is ViewModels.DatabaseUpdateViewModel vm && vm.CanStart &&
                    vm.StartFullUpdateWithRulingsCommand.CanExecute(null))
                    vm.StartFullUpdateWithRulingsCommand.Execute(null);
            };
        }
    }
}
