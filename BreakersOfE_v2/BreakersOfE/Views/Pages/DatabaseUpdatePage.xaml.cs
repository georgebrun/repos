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
        public DatabaseUpdatePage()
        {
            InitializeComponent();
        }
    }
}
