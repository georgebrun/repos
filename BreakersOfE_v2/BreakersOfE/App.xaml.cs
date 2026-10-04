using System.Windows;
using BreakersOfE.Data;
using BreakersOfE.Services;

namespace BreakersOfE
{
    /// <summary>
    /// App.xaml.cs — first code that runs on startup, and the last on exit.
    ///
    /// Startup: the BoE_V2 folder tree, leftovers from an interrupted update,
    /// the pool databases (cards and online) and the collection database —
    /// each brought up to the current schema (only ever ADDS tables/columns).
    /// Exit: the collection database's write-ahead log is merged into
    /// collection.db, so the file on disk is complete and current.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Make sure My Documents\BoE_V2\ and its subfolders exist
            AppFolderService.EnsureAllFolders();

            // Downloads used to live next to the program; copy them over once.
            AppFolderService.CopyLegacyDownloads();

            // An update interrupted by closing/crashing leaves a temporary
            // copy behind; drop it — the real databases were never touched.
            DatabaseStaging.CleanupLeftover(AppFolderService.DatabasePath);
            DatabaseStaging.CleanupLeftover(BreakersOfE.Data.RulingsDbContext.DefaultPath);

            // Ensure the pool database file exists with the current schema.
            // If it's the very first run, this creates an empty breakersofe.db
            // that the user then fills via Database Update.
            try
            {
                using (var pool = new AppDbContext())
                    pool.EnsureSchema();
                // Online pool (MTGO / Arena): its own table in the same file.
                using (var online = new OnlineDbContext())
                    online.EnsureSchema();
            }
            catch
            {
                // Don't crash on startup if the DB can't be initialized —
                // the user can still run a database update to fix things.
            }

            // Collection database: create it on first run, or bring an
            // existing one (e.g. copied over from v1) up to the current
            // schema. Only ADDS missing tables/columns, never removes data.
            try
            {
                using var collection = new CollectionDbContext();
                collection.MigrateSchema();
            }
            catch
            {
                // Same policy as the pool: never block startup on this.
            }

            // Pictures of your cards, kept for offline use: downloaded in the
            // background (Settings → Card pictures).
            PictureSync.StartBackground();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PictureSync.StopBackground();

            // Merge collection.db-wal into collection.db and reset it (TRUNCATE),
            // so the main file is complete — e.g. for a backup or a copy to
            // another computer. Best-effort: never blocks closing.
            try
            {
                using var collection = new CollectionDbContext();
                collection.Checkpoint();
            }
            catch
            {
                // closing anyway
            }
            base.OnExit(e);
        }
    }
}