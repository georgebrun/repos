using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Safe database updates: an update works on a temporary copy of the
    /// database; only when it finishes is the copy swapped in for the real
    /// file. Cancel, an error or a crash leaves the real database exactly as
    /// it was (the temporary copy is thrown away).
    ///
    ///   var stage = DatabaseStaging.Begin(realPath);   // copy real → temp
    ///   ... write to stage.WorkingPath ...
    ///   stage.Commit();                                // temp → real
    ///   // or stage.Abort();                           // delete temp
    /// </summary>
    public sealed class DatabaseStaging
    {
        public string RealPath { get; }
        /// <summary>The temporary copy the update writes to.</summary>
        public string WorkingPath { get; }
        private bool _done;

        private DatabaseStaging(string realPath)
        {
            RealPath = realPath;
            WorkingPath = TempPathFor(realPath);
        }

        /// <summary>"breakersofe.db" → "breakersofe.updating.db" (same folder).</summary>
        public static string TempPathFor(string realPath) =>
            Path.Combine(Path.GetDirectoryName(realPath) ?? "",
                         Path.GetFileNameWithoutExtension(realPath) + ".updating" + Path.GetExtension(realPath));

        /// <summary>
        /// Start an update: remove any leftover temp copy, then copy the real
        /// database (if there is one) to the temp file with SQLite's backup,
        /// which gives a consistent copy even while the app is reading it.
        /// </summary>
        public static DatabaseStaging Begin(string realPath)
        {
            var stage = new DatabaseStaging(realPath);
            DeleteWithSidecars(stage.WorkingPath);

            if (File.Exists(realPath))
            {
                using var src = new SqliteConnection($"Data Source={realPath};Mode=ReadOnly;Pooling=False");
                using var dst = new SqliteConnection($"Data Source={stage.WorkingPath};Pooling=False");
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }
            return stage;
        }

        /// <summary>
        /// The update finished: swap the temp copy in for the real database.
        /// Retries briefly if the app is reading the real file at that moment.
        /// </summary>
        public void Commit()
        {
            if (_done) return;

            // Close pooled connections so neither file is held open.
            SqliteConnection.ClearAllPools();

            Exception? last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    // Sidecar files belong to the OLD database; left in place they
                    // would be applied to the new one.
                    DeleteSidecars(RealPath);
                    File.Move(WorkingPath, RealPath, overwrite: true);
                    DeleteSidecars(WorkingPath);
                    _done = true;
                    return;
                }
                catch (IOException ex) { last = ex; }
                catch (UnauthorizedAccessException ex) { last = ex; }

                Thread.Sleep(250);
                SqliteConnection.ClearAllPools();
            }
            throw new IOException(
                $"The update finished but the new database could not replace the old one " +
                $"(the file is in use). Your old database is unchanged. {last?.Message}", last);
        }

        /// <summary>Cancelled or failed: throw the temp copy away. Never throws.</summary>
        public void Abort()
        {
            if (_done) return;
            _done = true;
            try
            {
                SqliteConnection.ClearAllPools();
                DeleteWithSidecars(WorkingPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseStaging.Abort: {ex.Message}");
            }
        }

        /// <summary>
        /// On startup: remove a temp copy left behind by an update that was
        /// interrupted (app closed or crashed). The real database is used as is.
        /// </summary>
        public static void CleanupLeftover(string realPath)
        {
            try { DeleteWithSidecars(TempPathFor(realPath)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseStaging.CleanupLeftover: {ex.Message}");
            }
        }

        private static void DeleteWithSidecars(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            DeleteSidecars(path);
        }

        private static void DeleteSidecars(string path)
        {
            foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
            {
                string f = path + suffix;
                if (File.Exists(f)) File.Delete(f);
            }
        }
    }
}