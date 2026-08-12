using System.Windows;
using BreakersOfE.Data;
using BreakersOfE.Services;

namespace BreakersOfE
{
    /// <summary>
    /// App.xaml.cs — first code that runs on startup.
    ///
    /// For this fresh v2 build we keep it minimal:
    ///   - Ensure the BoE_V2 folder tree exists
    ///   - Ensure the pool database exists (EnsureCreated via EnsureSchema)
    ///
    /// Collection/deck initialization will be added back when we rebuild
    /// those features. Right now the goal is: create a pool DB and show it.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Make sure My Documents\BoE_V2\ and its subfolders exist
            AppFolderService.EnsureAllFolders();

            // Ensure the pool database file exists with the current schema.
            // If it's the very first run, this creates an empty breakersofe.db
            // that the user then fills via Database Update.
            try
            {
                using var pool = new AppDbContext();
                pool.EnsureSchema();
            }
            catch
            {
                // Don't crash on startup if the DB can't be initialized —
                // the user can still run a database update to fix things.
            }
        }
    }
}