using System.Windows;
using BreakersOfE.Data;

namespace BreakersOfE
{
    /// <summary>
    /// App.xaml.cs — the very first code that runs when the application starts.
    /// 
    /// Handles:
    ///   - Database schema initialization (pool + collection)
    ///   - Future: DI container setup
    ///   - Future: Agent status check
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure databases exist with current schema
            InitializeDatabases();

            // Future: DI container setup goes here
            // Future: Agent status check goes here
        }

        /// <summary>
        /// Initializes both databases on startup.
        /// 
        /// AppDbContext (pool): Uses EnsureCreated() — pool is wiped and
        /// rebuilt on every full update anyway, so no incremental migrations.
        /// 
        /// CollectionDbContext (user data): Uses MigrateSchema() which
        /// checks each column with PRAGMA table_info before adding — no
        /// duplicate column exceptions, handles v1→v2 migration cleanly.
        /// </summary>
        private static void InitializeDatabases()
        {
            try
            {
                // Pool database — create if missing, schema comes from model
                using (var pool = new AppDbContext())
                {
                    pool.EnsureSchema();
                }

                // Collection database — create if missing, migrate schema if needed
                using (var col = new CollectionDbContext())
                {
                    col.MigrateSchema();
                }
            }
            catch
            {
                // Don't crash on startup if databases can't be initialized
                // The user can still run a database update to fix things
            }
        }
    }
}