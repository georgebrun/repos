using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Settings → Data folder → Move…: copy everything in the data folder
    /// (databases, decks, pictures, exports …) to a new folder, check every
    /// file arrived whole, then remember the new place for the next start.
    /// The old folder is left as it was (the user deletes it when happy).
    /// </summary>
    public static class DataFolderMover
    {
        public sealed record Progress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string Current);

        /// <summary>Why this folder can't take the data, or "" when it can.</summary>
        public static string CheckTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return "Pick a folder.";
            string from = Path.GetFullPath(AppFolderService.RootFolder).TrimEnd('\\') + "\\";
            string to = Path.GetFullPath(target).TrimEnd('\\') + "\\";
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return "That's already your data folder.";
            if (to.StartsWith(from, StringComparison.OrdinalIgnoreCase)) return "The new folder can't be inside the current data folder.";
            if (from.StartsWith(to, StringComparison.OrdinalIgnoreCase)) return "The new folder can't contain the current data folder.";
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                return "Pick an empty folder (or a new one) so nothing there gets mixed up with your data.";
            return "";
        }

        /// <summary>
        /// Copy and verify (sizes match), then point BoE at the new folder.
        /// Throws (and leaves the pointer unchanged) if anything fails.
        /// </summary>
        public static void Move(string target, IProgress<Progress>? progress, CancellationToken ct)
        {
            string problem = CheckTarget(target);
            if (problem.Length > 0) throw new InvalidOperationException(problem);
            var status = new AgentCoordinator().ReadStatus();
            if (status.Status != "idle" && status.UpdateStartedAt is { } started && DateTime.UtcNow - started < TimeSpan.FromHours(2))
                throw new InvalidOperationException("A database update is running — move the folder once it has finished.");

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string from = AppFolderService.RootFolder;
            bool targetExisted = Directory.Exists(target);
            var files = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
                                 .Select(f => new FileInfo(f))
                                 .Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                                             // A database's -wal / -shm are folded into its copy (below).
                                             !f.Name.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) &&
                                             !f.Name.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase))
                                 .ToList();
            long total = files.Sum(f => f.Length), done = 0;
            Directory.CreateDirectory(target);
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var f = files[i];
                    string rel = Path.GetRelativePath(from, f.FullName);
                    string dest = Path.Combine(target, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (f.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
                        CopyDatabase(f.FullName, dest);              // a consistent copy, even while in use
                    else
                    {
                        File.Copy(f.FullName, dest, overwrite: true);
                        if (new FileInfo(dest).Length != f.Length)
                            throw new IOException($"\"{rel}\" didn't copy completely.");
                    }
                    done += f.Length;
                    if (i % 25 == 0 || i == files.Count - 1)
                        progress?.Report(new Progress(i + 1, files.Count, done, total, rel));
                }
                // Empty folders too (Exports, Imports …).
                foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(from, d)));
            }
            catch
            {
                // Leave nothing half-copied behind (the folder was new or empty).
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try
                {
                    if (targetExisted)
                    {
                        foreach (var e in Directory.EnumerateFileSystemEntries(target))
                            if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e);
                    }
                    else Directory.Delete(target, true);
                }
                catch { /* best effort */ }
                throw;
            }

            AppFolderService.SetDataFolder(target);
        }

        /// <summary>SQLite's own backup: a complete, consistent copy of a database (its log included).</summary>
        private static void CopyDatabase(string source, string dest)
        {
            if (File.Exists(dest)) File.Delete(dest);
            using var src = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = source, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            using var dst = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = dest, Pooling = false,
            }.ToString());
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }

        /// <summary>Folder size and file count (for "Card pictures: 1.2 GB in 9,000 files").</summary>
        public static (long Bytes, int Files) Measure(string folder)
        {
            long bytes = 0; int n = 0;
            try
            {
                foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    bytes += f.Length;
                    n++;
                }
            }
            catch { }
            return (bytes, n);
        }

        /// <summary>"1.4 GB", "820 MB", "12 KB".</summary>
        public static string Size(long bytes) => bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
            _ => $"{Math.Max(1, bytes / 1024):0} KB",
        };
    }
}
