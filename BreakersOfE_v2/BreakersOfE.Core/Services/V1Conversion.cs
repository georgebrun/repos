using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BreakersOfE.Data;
using Microsoft.Data.Sqlite;

namespace BreakersOfE.Services
{
    /// <summary>
    /// The one-time move from Breakers of E v1 to v2, run at v2's first start
    /// on a data folder that still holds v1's collection.
    ///
    ///  1. v1's files are copied to "v1 Backups" in the data folder and every
    ///     copy is checked (size and contents).
    ///  2. v1's card pool (breakersofe.db) and keyword cache are set aside —
    ///     v2 downloads its own with Update Database.
    ///  3. collection.db gets v2's tables and columns (only added, never
    ///     removed); v1 rows that still held foil copies next to non-foil ones
    ///     are split into a foil row; every deck and Trade Binder claim is
    ///     placed on its collection rows.
    ///  4. Every copy is counted before and after: per table and printing
    ///     (non-foil and foil / etched), the Trade Binder, the Want List, and
    ///     every deck's claims. Any difference — or any error — puts v1's
    ///     files back from the backup, and nothing is changed.
    /// Decks, filters, card pictures and Tabletop are left as they are.
    /// </summary>
    public static class V1Conversion
    {
        public const string BackupFolderName = "v1 Backups";

        /// <summary>The folders copied to v1 Backups (besides the files at the top of the data folder).</summary>
        private static readonly string[] BackedUpFolders = { "Collection", "Decks", "Filters" };

        /// <summary>
        /// v1's files that v2 replaces with its own (they stay in v1 Backups).
        /// agent_status.json was never v1's: an early v2 test build left it, and
        /// its old "last update" date would make the update reminder wrong.
        /// </summary>
        private static readonly string[] SetAsideFiles =
            { "breakersofe.db", "breakersofe.db-wal", "breakersofe.db-shm", "keywords.json", "agent_status.json" };

        /// <summary>The collection tables (v1 and v2 have the same seven).</summary>
        private static readonly string[] CollectionTables =
        {
            "CollectionEntries", "TokenCollectionEntries", "PlanarCollectionEntries", "SchemeCollectionEntries",
            "VanguardCollectionEntries", "ArtSeriesCollectionEntries", "ConspiracyCollectionEntries",
        };

        // ══════════════════════════════════════════════════════════════════
        // DETECT
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The data folder holds a v1 collection not yet converted: collection.db
        /// without v2's online collection table (v2 adds it the first time it
        /// opens a collection, so a converted or new v2 collection always has it).
        /// </summary>
        public static bool IsV1Folder(string root)
        {
            string path = Path.Combine(root, "Collection", "collection.db");
            if (!File.Exists(path)) return false;
            try
            {
                using var conn = Open(path, readOnly: true);
                return HasTable(conn, "CollectionEntries") && !HasTable(conn, "OnlineCollectionEntries");
            }
            catch
            {
                return false;           // unreadable: leave it to the normal startup
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // RUN
        // ══════════════════════════════════════════════════════════════════

        /// <summary>What the conversion did, with the counts before and after.</summary>
        public sealed class Result
        {
            public bool Success { get; init; }
            public string Message { get; init; } = "";
            public string BackupFolder { get; init; } = "";
            public Tally Before { get; init; } = new();
            public Tally After { get; init; } = new();
            /// <summary>What didn't match (at most 20 lines).</summary>
            public List<string> Problems { get; init; } = new();
        }

        /// <summary>Convert the v1 collection in the data folder. Never throws: a failure comes back in the result, with v1's files put back.</summary>
        public static Result Run(string root, IProgress<string>? progress = null)
        {
            void Say(string s) => progress?.Report(s);
            string collection = Path.Combine(root, "Collection", "collection.db");
            string backup = "";
            Tally before = new();
            try
            {
                // 1. Everything v1 wrote, in the main file (merges v1's -wal, if any), then counted.
                Say("Counting your v1 cards…");
                Checkpoint(collection);
                before = Count(collection, v1: true);

                // 2. Backup, checked.
                Say("Copying your v1 files to \"" + BackupFolderName + "\"…");
                backup = NewBackupFolder(root);
                CopyAndCheck(root, backup);
                WriteReadme(backup, before);

                // 3. v1's card pool and keyword cache set aside; v1's own old collection copies tidied away.
                Say("Setting aside v1's card database…");
                foreach (string f in SetAsideFiles)
                {
                    string p = Path.Combine(root, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                foreach (string f in Directory.GetFiles(Path.Combine(root, "Collection")))
                {
                    string name = Path.GetFileName(f);
                    if (!name.StartsWith("collection.db", StringComparison.OrdinalIgnoreCase)) File.Delete(f);
                }

                // 4. collection.db in v2's form.
                Say("Converting your collection…");
                using (var db = new CollectionDbContext())
                    db.MigrateSchema();
                SqliteConnection.ClearAllPools();
                SplitCombinedRows(collection);

                Say("Converting your deck and Trade Binder claims…");
                CollectionEditService.ConvertAllV1Claims();
                SqliteConnection.ClearAllPools();
                Checkpoint(collection);

                // 5. Count again, and compare.
                Say("Counting your cards again…");
                var after = Count(collection, v1: false);
                var problems = Compare(before, after);
                if (problems.Count > 0)
                {
                    Restore(root, backup);
                    return new Result
                    {
                        Success = false,
                        BackupFolder = backup,
                        Before = before,
                        After = after,
                        Problems = problems.Take(20).ToList(),
                        Message = "The counts after converting didn't match the counts before, so nothing was changed: " +
                                  "your v1 files were put back as they were.",
                    };
                }

                Say("Done.");
                return new Result
                {
                    Success = true,
                    BackupFolder = backup,
                    Before = before,
                    After = after,
                    Message = "Every card matches. Your v1 files are kept in \"" + backup + "\".",
                };
            }
            catch (Exception ex)
            {
                string restored = "";
                if (backup.Length > 0)
                {
                    try { Restore(root, backup); restored = " Your v1 files were put back as they were."; }
                    catch (Exception rex) { restored = " Putting your v1 files back also failed (" + rex.Message + "): they are all in \"" + backup + "\"."; }
                }
                return new Result
                {
                    Success = false,
                    BackupFolder = backup,
                    Before = before,
                    Message = "The conversion stopped: " + ex.Message.TrimEnd('.') + "." + restored +
                              (backup.Length == 0 ? " Nothing was changed." : ""),
                };
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // BACKUP AND RESTORE
        // ══════════════════════════════════════════════════════════════════

        /// <summary>"v1 Backups", or "v1 Backups (2026-10-08 1835)" if one is there already (never mixed).</summary>
        private static string NewBackupFolder(string root)
        {
            string path = Path.Combine(root, BackupFolderName);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
                path = Path.Combine(root, $"{BackupFolderName} ({DateTime.Now:yyyy-MM-dd HHmmss})");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>The files at the top of the data folder, and the Collection, Decks and Filters folders — each copy checked.</summary>
        private static void CopyAndCheck(string root, string backup)
        {
            foreach (string f in Directory.GetFiles(root))
                CopyChecked(f, Path.Combine(backup, Path.GetFileName(f)));
            foreach (string folder in BackedUpFolders)
            {
                string from = Path.Combine(root, folder);
                if (!Directory.Exists(from)) continue;
                foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    CopyChecked(f, Path.Combine(backup, folder, Path.GetRelativePath(from, f)));
            }
        }

        private static void CopyChecked(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
            if (new FileInfo(from).Length != new FileInfo(to).Length || !Hash(from).SequenceEqual(Hash(to)))
                throw new IOException($"the backup copy of {Path.GetFileName(from)} didn't match the original");
        }

        private static byte[] Hash(string path)
        {
            using var s = File.OpenRead(path);
            return SHA256.HashData(s);
        }

        /// <summary>v1's collection, card pool and keyword cache back as they were (from the checked backup).</summary>
        private static void Restore(string root, string backup)
        {
            SqliteConnection.ClearAllPools();
            string coll = Path.Combine(root, "Collection");
            Directory.CreateDirectory(coll);
            foreach (string f in Directory.GetFiles(coll, "collection.db*")) File.Delete(f);
            string fromColl = Path.Combine(backup, "Collection");
            if (Directory.Exists(fromColl))
                foreach (string f in Directory.GetFiles(fromColl))
                    File.Copy(f, Path.Combine(coll, Path.GetFileName(f)), overwrite: true);
            foreach (string name in SetAsideFiles)
            {
                string f = Path.Combine(backup, name);
                if (File.Exists(f)) File.Copy(f, Path.Combine(root, name), overwrite: true);
            }
        }

        private static void WriteReadme(string backup, Tally before)
        {
            File.WriteAllText(Path.Combine(backup, "README.txt"),
                $"Breakers of E — your v1 files, copied on {DateTime.Now:yyyy-MM-dd HH:mm} before converting to v2.\r\n\r\n" +
                "Breakers of E v1 is no longer supported. These are your v1 collection, decks, filters and\r\n" +
                "card database exactly as they were, kept in case you ever need them.\r\n\r\n" +
                $"Counted here: {before.Rows:N0} collection rows, {before.Copies:N0} copies ({before.FoilCopies:N0} foil), " +
                $"{before.Claimed:N0} copies claimed by {before.Decks:N0} decks, {before.BinderCopies:N0} in the Trade Binder, " +
                $"{before.WantCopies:N0} on the Want List.\r\n");
        }

        // ══════════════════════════════════════════════════════════════════
        // v1 ROWS STILL HOLDING FOIL COPIES (from before v1's per-finish split)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Older v1 collections kept foil copies in FoilQuantity next to the
        /// non-foil ones; v2 keeps one row per finish. Those foil copies move to
        /// the row of the same printing, language and condition in foil (made
        /// if needed); the Trade Binder and Want List get their finish from IsFoil.
        /// </summary>
        private static void SplitCombinedRows(string collection)
        {
            using var conn = Open(collection, readOnly: false);
            using var tx = conn.BeginTransaction();
            foreach (string table in CollectionTables)
            {
                if (!HasTable(conn, table, tx)) continue;
                var cols = Columns(conn, table, tx);
                if (!cols.Contains("FoilQuantity") || !cols.Contains("Finish")) continue;
                string id = cols[0];                                       // the key is the first column
                bool lang = cols.Contains("Language"), cond = cols.Contains("Condition");

                var rows = new List<(long Id, string Sid, string Lang, string Cond, int Qty, int Foil)>();
                using (var cmd = Cmd(conn, tx, $"SELECT \"{id}\", ScryfallId, {(lang ? "Language" : "''")}, {(cond ? "Condition" : "''")}, Quantity, FoilQuantity FROM \"{table}\" WHERE FoilQuantity > 0"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        rows.Add((r.GetInt64(0), Str(r, 1), Str(r, 2), Str(r, 3), r.GetInt32(4), r.GetInt32(5)));

                foreach (var row in rows)
                {
                    // A foil row of the same printing, language and condition already there?
                    string match = $"SELECT \"{id}\" FROM \"{table}\" WHERE ScryfallId = $sid AND Finish = 'foil' AND \"{id}\" <> $id" +
                                   (lang ? " AND Language = $lang" : "") + (cond ? " AND Condition = $cond" : "") + " LIMIT 1";
                    var args = new List<(string, object)> { ("$sid", row.Sid), ("$id", row.Id) };
                    if (lang) args.Add(("$lang", row.Lang));
                    if (cond) args.Add(("$cond", row.Cond));
                    long? foilRow = null;
                    using (var cmd = Cmd(conn, tx, match, args.ToArray()))
                        if (cmd.ExecuteScalar() is long l) foilRow = l;

                    if (foilRow is long target)
                    {
                        Exec(conn, tx, $"UPDATE \"{table}\" SET Quantity = Quantity + $n WHERE \"{id}\" = $id", ("$n", row.Foil), ("$id", target));
                        if (row.Qty > 0) Exec(conn, tx, $"UPDATE \"{table}\" SET FoilQuantity = 0, Finish = 'nonfoil' WHERE \"{id}\" = $id", ("$id", row.Id));
                        else Exec(conn, tx, $"DELETE FROM \"{table}\" WHERE \"{id}\" = $id", ("$id", row.Id));
                    }
                    else if (row.Qty <= 0)
                    {
                        // Foil copies only: the row itself becomes the foil row.
                        Exec(conn, tx, $"UPDATE \"{table}\" SET Quantity = FoilQuantity, FoilQuantity = 0, Finish = 'foil' WHERE \"{id}\" = $id", ("$id", row.Id));
                    }
                    else
                    {
                        // A new foil row: a copy of this one, with the foil copies.
                        var copy = cols.Skip(1).ToList();
                        string list = string.Join(", ", copy.Select(c => $"\"{c}\""));
                        string values = string.Join(", ", copy.Select(c => c switch
                        {
                            "Quantity" => "FoilQuantity",
                            "FoilQuantity" => "0",
                            "Finish" => "'foil'",
                            "UsedCount" => "0",
                            _ => $"\"{c}\"",
                        }));
                        Exec(conn, tx, $"INSERT INTO \"{table}\" ({list}) SELECT {values} FROM \"{table}\" WHERE \"{id}\" = $id", ("$id", row.Id));
                        Exec(conn, tx, $"UPDATE \"{table}\" SET FoilQuantity = 0, Finish = 'nonfoil' WHERE \"{id}\" = $id", ("$id", row.Id));
                    }
                }
            }

            foreach (string table in new[] { "TradeBinderEntries", "WantListEntries" })
            {
                if (!HasTable(conn, table, tx)) continue;
                var cols = Columns(conn, table, tx);
                if (cols.Contains("IsFoil") && cols.Contains("Finish"))
                    Exec(conn, tx, $"UPDATE \"{table}\" SET Finish = 'foil' WHERE IsFoil = 1 AND (Finish = 'nonfoil' OR Finish = '' OR Finish IS NULL)");
            }
            tx.Commit();
        }

        // ══════════════════════════════════════════════════════════════════
        // COUNTS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Every copy, counted: per table and printing, per deck and printing, the Trade Binder and the Want List.</summary>
        public sealed class Tally
        {
            /// <summary>"table|scryfallId" → (non-foil, foil or etched) copies.</summary>
            public Dictionary<string, (int NonFoil, int Foil)> Cards { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>"deckId|scryfallId" → (non-foil, foil or etched) copies claimed.</summary>
            public Dictionary<string, (int NonFoil, int Foil)> Claims { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>"table|scryfallId" → copies, for the Trade Binder and the Want List.</summary>
            public Dictionary<string, int> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>"table|scryfallId" → a card name, for the problem list.</summary>
            public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

            public int Rows { get; set; }
            public int Copies => Cards.Values.Sum(v => v.NonFoil + v.Foil);
            public int FoilCopies => Cards.Values.Sum(v => v.Foil);
            public int Claimed => Claims.Values.Sum(v => v.NonFoil + v.Foil);
            public int Decks => Claims.Keys.Select(k => k.Split('|')[0]).Distinct().Count();
            public int BinderCopies => Lists.Where(k => k.Key.StartsWith("TradeBinderEntries|")).Sum(k => k.Value);
            public int WantCopies => Lists.Where(k => k.Key.StartsWith("WantListEntries|")).Sum(k => k.Value);
            public int Tokens => Cards.Where(k => k.Key.StartsWith("TokenCollectionEntries|")).Sum(k => k.Value.NonFoil + k.Value.Foil);
        }

        private static bool IsFoilish(string finish) =>
            finish.Equals("foil", StringComparison.OrdinalIgnoreCase) || finish.Equals("etched", StringComparison.OrdinalIgnoreCase);

        private static Tally Count(string collection, bool v1)
        {
            var t = new Tally();
            using var conn = Open(collection, readOnly: true);

            foreach (string table in CollectionTables)
            {
                if (!HasTable(conn, table)) continue;
                var cols = Columns(conn, table);
                string finish = cols.Contains("Finish") ? "Finish" : "'nonfoil'";
                string foilQty = cols.Contains("FoilQuantity") ? "FoilQuantity" : "0";
                using var cmd = Cmd(conn, null, $"SELECT ScryfallId, {finish}, Quantity, {foilQty}, Name FROM \"{table}\"");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string key = table + "|" + Str(r, 0);
                    int qty = Math.Max(0, r.GetInt32(2)), foil = Math.Max(0, r.GetInt32(3));
                    var (nf, f) = t.Cards.TryGetValue(key, out var v) ? v : (0, 0);
                    if (IsFoilish(Str(r, 1))) f += qty; else nf += qty;
                    f += foil;
                    t.Cards[key] = (nf, f);
                    t.Names[key] = Str(r, 4);
                    t.Rows++;
                }
            }

            foreach (string table in new[] { "TradeBinderEntries", "WantListEntries" })
            {
                if (!HasTable(conn, table)) continue;
                using var cmd = Cmd(conn, null, $"SELECT ScryfallId, Quantity, Name FROM \"{table}\"");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string key = table + "|" + Str(r, 0);
                    t.Lists[key] = (t.Lists.TryGetValue(key, out int n) ? n : 0) + Math.Max(0, r.GetInt32(1));
                    t.Names[key] = Str(r, 2);
                }
            }

            if (HasTable(conn, "DeckUsages"))
            {
                var cols = Columns(conn, "DeckUsages");
                string etched = cols.Contains("EnteredEtched") ? "EnteredEtched" : "0";
                string nfE = cols.Contains("EnteredNonFoil") ? "EnteredNonFoil" : "0";
                string fE = cols.Contains("EnteredFoil") ? "EnteredFoil" : "0";
                using var cmd = Cmd(conn, null, $"SELECT DeckId, ScryfallId, Quantity, FoilQuantity, {nfE}, {fE}, {etched} FROM DeckUsages");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string key = Str(r, 0) + "|" + Str(r, 1);
                    int q = r.GetInt32(2), fq = r.GetInt32(3), en = r.GetInt32(4), ef = r.GetInt32(5), ee = r.GetInt32(6);
                    // v1 rows from before per-deck entries keep their counts in Quantity / FoilQuantity.
                    (int nf, int f) claim = v1 && en + ef + ee == 0 ? (q, fq) : (en, ef + ee);
                    var (a, b) = t.Claims.TryGetValue(key, out var v) ? v : (0, 0);
                    t.Claims[key] = (a + Math.Max(0, claim.nf), b + Math.Max(0, claim.f));
                }
            }
            return t;
        }

        /// <summary>Every difference between the two counts, as lines a person can read.</summary>
        private static List<string> Compare(Tally before, Tally after)
        {
            var problems = new List<string>();
            string Name(string key) =>
                (before.Names.TryGetValue(key, out var n) || after.Names.TryGetValue(key, out n)) && n.Length > 0
                    ? n : key.Split('|').Last();
            string Table(string key) => key.Split('|')[0] switch
            {
                "CollectionEntries" => "Collection",
                "TokenCollectionEntries" => "Tokens",
                "TradeBinderEntries" => "Trade Binder",
                "WantListEntries" => "Want List",
                var other => other.Replace("CollectionEntries", ""),
            };

            foreach (var key in before.Cards.Keys.Union(after.Cards.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var a = before.Cards.TryGetValue(key, out var x) ? x : (0, 0);
                var b = after.Cards.TryGetValue(key, out var y) ? y : (0, 0);
                if (a != b)
                    problems.Add($"{Table(key)} · {Name(key)}: before {a.Item1} non-foil, {a.Item2} foil — after {b.Item1} non-foil, {b.Item2} foil");
            }
            foreach (var key in before.Lists.Keys.Union(after.Lists.Keys, StringComparer.OrdinalIgnoreCase))
            {
                int a = before.Lists.TryGetValue(key, out int x) ? x : 0;
                int b = after.Lists.TryGetValue(key, out int y) ? y : 0;
                if (a != b) problems.Add($"{Table(key)} · {Name(key)}: before {a}, after {b}");
            }
            foreach (var key in before.Claims.Keys.Union(after.Claims.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var a = before.Claims.TryGetValue(key, out var x) ? x : (0, 0);
                var b = after.Claims.TryGetValue(key, out var y) ? y : (0, 0);
                if (a != b)
                {
                    string sid = key.Split('|').Last();
                    string name = before.Names.FirstOrDefault(n => n.Key.EndsWith("|" + sid, StringComparison.OrdinalIgnoreCase)).Value ?? sid;
                    problems.Add($"Deck claim · {name}: before {a.Item1} non-foil, {a.Item2} foil — after {b.Item1} non-foil, {b.Item2} foil");
                }
            }
            return problems;
        }

        // ══════════════════════════════════════════════════════════════════
        // SQLITE HELPERS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Open the database. Always read-write, even just to read: v1's database
        /// is in WAL mode, and a read-only connection can't open one whose -shm
        /// file isn't there yet. (Reading never changes the data.)
        /// </summary>
        private static SqliteConnection Open(string path, bool readOnly)
        {
            _ = readOnly;
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            conn.Open();
            return conn;
        }

        /// <summary>Merge the -wal file into the database file (so a copy of the file is complete).</summary>
        private static void Checkpoint(string path)
        {
            using var conn = Open(path, readOnly: false);
            Exec(conn, null, "PRAGMA wal_checkpoint(TRUNCATE);");
        }

        // Every command on a connection with an open transaction must carry it (SQLite refuses otherwise).
        private static bool HasTable(SqliteConnection conn, string table, SqliteTransaction? tx = null)
        {
            using var cmd = Cmd(conn, tx, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $t", ("$t", table));
            return cmd.ExecuteScalar() != null;
        }

        private static List<string> Columns(SqliteConnection conn, string table, SqliteTransaction? tx = null)
        {
            var cols = new List<string>();
            using var cmd = Cmd(conn, tx, $"PRAGMA table_info(\"{table}\")");
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(1));
            return cols;
        }

        private static SqliteCommand Cmd(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = tx;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            return cmd;
        }

        private static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
        {
            using var cmd = Cmd(conn, tx, sql, args);
            cmd.ExecuteNonQuery();
        }

        private static string Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
    }
}
