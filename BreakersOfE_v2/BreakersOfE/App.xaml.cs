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
        private static Views.SplashWindow? _splash;
        private static readonly System.Diagnostics.Stopwatch _splashShown = new();
        private static readonly TimeSpan SplashMinimum = TimeSpan.FromSeconds(2.5);

        /// <summary>Start-up progress on the splash (no-op once it's closed).</summary>
        public static void SplashStatus(string message, int progress)
        {
            try { _splash?.SetStatus(message, progress); } catch { /* closing */ }
        }

        /// <summary>
        /// Close the startup picture (fading). Called when the first page has
        /// its cards on screen; also after a time limit, and before any
        /// startup message (so the picture never covers it).
        /// </summary>
        public static void CloseSplash(bool now = false)
        {
            if (_splash == null) return;
            if (!now) SplashStatus("Ready!", 100);
            // Shown at least a moment: a fast start would otherwise just flash it.
            var left = SplashMinimum - _splashShown.Elapsed;
            if (!now && left > TimeSpan.Zero)
            {
                var wait = new System.Windows.Threading.DispatcherTimer { Interval = left };
                wait.Tick += (_, _) => { wait.Stop(); CloseSplash(now: true); };
                wait.Start();
                return;
            }
            var s = _splash;
            _splash = null;
            if (s == null) return;
            try
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => s.Close();
                s.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch { try { s.Close(); } catch { /* already gone */ } }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // First start after installing: ask where to keep the data, before
            // anything is created. (While the question is open, closing it must
            // not end the app — the main window isn't there yet.)
            if (AppFolderService.IsFirstRun)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Views.Dialogs.FirstRunFolderDialog.Ask();
                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }

            // The startup picture (the campus at night), shown while the app gets ready.
            // After the first-run question, so it never covers it.
            try
            {
                _splash = new Views.SplashWindow();
                _splash.Show();
                _splash.SetStatus("Starting up…", 5);
                _splashShown.Start();
                // Never longer than this, whatever happens.
                var limit = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                limit.Tick += (_, _) => { limit.Stop(); CloseSplash(now: true); };
                limit.Start();
            }
            catch
            {
                _splash = null;                   // no picture: start without it
            }

            // Light, Dark or Follow Windows (Settings → Appearance).
            ThemeApplier.ApplySaved();

            SplashStatus("Checking the data folder…", 15);

            // Make sure My Documents\BoE_V2\ and its subfolders exist
            AppFolderService.EnsureAllFolders();

            // Downloads used to live next to the program; copy them over once.
            AppFolderService.CopyLegacyDownloads();

            // An update interrupted by closing/crashing leaves a temporary
            // copy behind; drop it — the real databases were never touched.
            DatabaseStaging.CleanupLeftover(AppFolderService.DatabasePath);
            DatabaseStaging.CleanupLeftover(BreakersOfE.Data.RulingsDbContext.DefaultPath);

            SplashStatus("Checking the card pool database…", 30);

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

            SplashStatus("Checking your collection…", 45);

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
            SplashStatus("Loading the card pool…", 60);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PictureSync.StopBackground();
            ThemeApplier.Stop();

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