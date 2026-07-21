using System.Windows;

namespace BreakersOfE
{
    /// <summary>
    /// App.xaml.cs — the very first code that runs when the application starts.
    /// 
    /// Right now it's minimal. In later phases we'll add:
    ///   - Dependency Injection (DI) container setup
    ///   - Service registration (ScryfallService, PriceService, etc.)
    ///   - Database initialization
    ///   - Agent status check
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Future: DI container setup goes here
            // Future: Database initialization goes here
            // Future: Agent status check goes here
        }
    }
}
