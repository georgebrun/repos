using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.Data.Sqlite;

namespace BreakersOfE.Services
{
    /// <summary>One point of a card's price history.</summary>
    public sealed class CardPricePoint
    {
        public DateTime Date { get; init; }
        public decimal? Usd { get; init; }
        public decimal? UsdFoil { get; init; }
        public decimal? UsdEtched { get; init; }
    }

    /// <summary>One point of the collection's value history.</summary>
    public sealed class CollectionValuePoint
    {
        public DateTime Date { get; init; }
        public decimal Value { get; init; }
        public int Cards { get; init; }
    }

    /// <summary>
    /// Price history. After every Update Database (full or prices only) the
    /// prices of the whole card pool are saved as a dated snapshot, plus your
    /// collection's total value and card count at that moment.
    ///
    /// • One snapshot per day: a second update the same day replaces it.
    /// • Only prices that CHANGED since the card's previous snapshot are
    ///   written (a missing row = same as before), so the file stays small.
    /// • Stored in its own file, Documents\BoE_V2\Collection\PriceHistory.db;
    ///   the collection database is never touched.
    ///
    /// Read by the card details pop-up (last 5 points) and Collection
    /// Statistics (last 15 points).
    /// </summary>
    public static class PriceHistoryService
    {
        private const int SchemaVersion = 2;

        public static string DatabasePath =>
            Path.Combine(AppFolderService.CollectionFolder, "PriceHistory.db");

        private static SqliteConnection Open()
        {
            var con = new SqliteConnection($"Data Source={DatabasePath}");
            con.Open();

            long version;
            using (var v = con.CreateCommand())
            {
                v.CommandText = "PRAGMA user_version;";
                version = (long)(v.ExecuteScalar() ?? 0L);
            }

            if (version < SchemaVersion)
            {
                // Earlier test layout (before schema 2) held test data only: start over.
                using var cmd = con.CreateCommand();
                cmd.CommandText = $@"
                    DROP TABLE IF EXISTS Prices;
                    DROP TABLE IF EXISTS Snapshots;
                    CREATE TABLE Snapshots (
                        SnapshotId      INTEGER PRIMARY KEY AUTOINCREMENT,
                        TakenAt         TEXT NOT NULL,          -- local time, yyyy-MM-dd HH:mm:ss
                        Day             TEXT NOT NULL UNIQUE,   -- yyyy-MM-dd (one per day)
                        CollectionCents INTEGER NOT NULL,       -- collection value then
                        CollectionCards INTEGER NOT NULL        -- collection card count then
                    );
                    -- Only changes: a card's price at a snapshot is its newest row at or before it.
                    CREATE TABLE Prices (
                        ScryfallId      TEXT NOT NULL,
                        SnapshotId      INTEGER NOT NULL,
                        UsdCents        INTEGER,
                        UsdFoilCents    INTEGER,
                        UsdEtchedCents  INTEGER,
                        PRIMARY KEY (ScryfallId, SnapshotId)
                    ) WITHOUT ROWID;
                    PRAGMA user_version = {SchemaVersion};";
                cmd.ExecuteNonQuery();
            }
            return con;
        }

        // ── Taking a snapshot ───────────────────────────────────────────

        /// <summary>
        /// Save today's prices for the whole card pool and today's collection
        /// value. Never throws (a failed snapshot must not fail an update).
        /// Returns how many price rows were written (changes only).
        /// </summary>
        public static int TakeSnapshot()
        {
            try
            {
                // Current pool prices (+ which printings are etched-only).
                var pool = new Dictionary<string, (long? u, long? f, long? e)>(StringComparer.OrdinalIgnoreCase);
                var etchedOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var db = new AppDbContext())
                {
                    foreach (var c in db.PoolCards
                                 .Select(c => new { c.ScryfallId, c.PriceUsd, c.PriceUsdFoil, c.PriceUsdEtched, c.IsFoil, c.IsEtched })
                                 .ToList())
                    {
                        if (string.IsNullOrEmpty(c.ScryfallId)) continue;
                        pool[c.ScryfallId] = (Cents(c.PriceUsd), Cents(c.PriceUsdFoil), Cents(c.PriceUsdEtched));
                        if (c.IsEtched && !c.IsFoil) etchedOnly.Add(c.ScryfallId);
                    }
                }
                if (pool.Count == 0) return 0;

                // Collection value and count right now (priced by each row's finish).
                long valueCents = 0;
                int cards = 0;
                try
                {
                    using var cdb = new CollectionDbContext();
                    foreach (var e in cdb.CollectionEntries
                                 .Where(e => e.Quantity > 0)
                                 .Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                 .ToList())
                    {
                        cards += e.Quantity;
                        if (!pool.TryGetValue(e.ScryfallId ?? "", out var p)) continue;
                        // The one price rule, on the row's real finish.
                        string finish = CardFinish.Shown(e.Finish, etchedOnly.Contains(e.ScryfallId ?? ""));
                        long? price = CardFinish.Normalize(finish) switch
                        {
                            CardFinish.Foil => p.f,
                            CardFinish.Etched => p.e,
                            _ => p.u,
                        };
                        valueCents += (price ?? 0) * e.Quantity;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Price snapshot (collection): {ex.Message}");
                }

                var now = DateTime.Now;
                string day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                using var con = Open();
                using var tx = con.BeginTransaction();

                // Today's snapshot: reuse it (drop its rows) or create it.
                long snapId;
                using (var find = con.CreateCommand())
                {
                    find.Transaction = tx;
                    find.CommandText = "SELECT SnapshotId FROM Snapshots WHERE Day = $d";
                    find.Parameters.AddWithValue("$d", day);
                    var existing = find.ExecuteScalar();
                    if (existing is long id)
                    {
                        snapId = id;
                        using var upd = con.CreateCommand();
                        upd.Transaction = tx;
                        upd.CommandText = @"
                            DELETE FROM Prices WHERE SnapshotId = $s;
                            UPDATE Snapshots SET TakenAt = $t, CollectionCents = $v, CollectionCards = $c
                            WHERE SnapshotId = $s;";
                        upd.Parameters.AddWithValue("$s", snapId);
                        upd.Parameters.AddWithValue("$t", now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                        upd.Parameters.AddWithValue("$v", valueCents);
                        upd.Parameters.AddWithValue("$c", cards);
                        upd.ExecuteNonQuery();
                    }
                    else
                    {
                        using var ins = con.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = @"
                            INSERT INTO Snapshots (TakenAt, Day, CollectionCents, CollectionCards)
                            VALUES ($t, $d, $v, $c);
                            SELECT last_insert_rowid();";
                        ins.Parameters.AddWithValue("$t", now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                        ins.Parameters.AddWithValue("$d", day);
                        ins.Parameters.AddWithValue("$v", valueCents);
                        ins.Parameters.AddWithValue("$c", cards);
                        snapId = (long)ins.ExecuteScalar()!;
                    }
                }

                // Each card's last known prices before this snapshot.
                var prev = new Dictionary<string, (long? u, long? f, long? e)>(StringComparer.OrdinalIgnoreCase);
                using (var q = con.CreateCommand())
                {
                    q.Transaction = tx;
                    q.CommandText = @"
                        SELECT p.ScryfallId, p.UsdCents, p.UsdFoilCents, p.UsdEtchedCents
                        FROM Prices p
                        JOIN (SELECT ScryfallId, MAX(SnapshotId) AS M
                              FROM Prices WHERE SnapshotId < $s GROUP BY ScryfallId) x
                          ON p.ScryfallId = x.ScryfallId AND p.SnapshotId = x.M";
                    q.Parameters.AddWithValue("$s", snapId);
                    using var r = q.ExecuteReader();
                    while (r.Read())
                        prev[r.GetString(0)] = (Long(r, 1), Long(r, 2), Long(r, 3));
                }

                // Write only what changed (or first seen with a price).
                int written = 0;
                using (var cmd = con.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT OR REPLACE INTO Prices
                        (ScryfallId, SnapshotId, UsdCents, UsdFoilCents, UsdEtchedCents)
                        VALUES ($id, $s, $u, $f, $e)";
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pS = cmd.Parameters.Add("$s", SqliteType.Integer);
                    var pU = cmd.Parameters.Add("$u", SqliteType.Integer);
                    var pF = cmd.Parameters.Add("$f", SqliteType.Integer);
                    var pE = cmd.Parameters.Add("$e", SqliteType.Integer);
                    pS.Value = snapId;

                    foreach (var (sid, p) in pool)
                    {
                        if (prev.TryGetValue(sid, out var old))
                        {
                            if (old == p) continue;                       // unchanged
                        }
                        else if (p.u == null && p.f == null && p.e == null)
                        {
                            continue;                                     // never priced
                        }

                        pId.Value = sid;
                        pU.Value = (object?)p.u ?? DBNull.Value;
                        pF.Value = (object?)p.f ?? DBNull.Value;
                        pE.Value = (object?)p.e ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                        written++;
                    }
                }

                tx.Commit();
                return written;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Price snapshot failed: {ex.Message}");
                return 0;
            }
        }

        // ── Reading ─────────────────────────────────────────────────────

        /// <summary>
        /// A card's prices at the newest <paramref name="max"/> snapshots,
        /// oldest first. Snapshots from before the card had any price are left out.
        /// </summary>
        public static List<CardPricePoint> GetCardHistory(string scryfallId, int max = 5)
        {
            var points = new List<CardPricePoint>();
            if (string.IsNullOrEmpty(scryfallId) || !File.Exists(DatabasePath)) return points;
            try
            {
                using var con = Open();

                var snaps = new List<(long id, DateTime at)>();
                using (var s = con.CreateCommand())
                {
                    s.CommandText = "SELECT SnapshotId, TakenAt FROM Snapshots ORDER BY SnapshotId DESC LIMIT $n";
                    s.Parameters.AddWithValue("$n", max);
                    using var r = s.ExecuteReader();
                    while (r.Read())
                    {
                        DateTime.TryParse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at);
                        snaps.Add((r.GetInt64(0), at));
                    }
                }
                if (snaps.Count == 0) return points;
                snaps.Reverse();                                    // oldest first

                // All of this card's change rows up to the newest snapshot.
                var rows = new List<(long id, decimal? u, decimal? f, decimal? e)>();
                using (var p = con.CreateCommand())
                {
                    p.CommandText = @"SELECT SnapshotId, UsdCents, UsdFoilCents, UsdEtchedCents
                                      FROM Prices WHERE ScryfallId = $id ORDER BY SnapshotId";
                    p.Parameters.AddWithValue("$id", scryfallId);
                    using var r = p.ExecuteReader();
                    while (r.Read())
                        rows.Add((r.GetInt64(0), Dollars(r, 1), Dollars(r, 2), Dollars(r, 3)));
                }
                if (rows.Count == 0) return points;

                foreach (var (id, at) in snaps)
                {
                    // Newest change at or before this snapshot.
                    var hit = rows.LastOrDefault(x => x.id <= id);
                    if (hit.id == 0) continue;                      // no price yet at this point
                    points.Add(new CardPricePoint { Date = at, Usd = hit.u, UsdFoil = hit.f, UsdEtched = hit.e });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Price history (card): {ex.Message}");
            }
            return points;
        }

        /// <summary>The collection's value at the newest <paramref name="max"/> snapshots, oldest first.</summary>
        public static List<CollectionValuePoint> GetCollectionHistory(int max = 15)
        {
            var points = new List<CollectionValuePoint>();
            if (!File.Exists(DatabasePath)) return points;
            try
            {
                using var con = Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"SELECT TakenAt, CollectionCents, CollectionCards
                                    FROM Snapshots ORDER BY SnapshotId DESC LIMIT $n";
                cmd.Parameters.AddWithValue("$n", max);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime.TryParse(r.GetString(0), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at);
                    points.Add(new CollectionValuePoint
                    {
                        Date = at,
                        Value = r.GetInt64(1) / 100m,
                        Cards = r.GetInt32(2),
                    });
                }
                points.Reverse();                                   // oldest first
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Price history (collection): {ex.Message}");
            }
            return points;
        }

        private static long? Cents(decimal? d) => d.HasValue ? (long)Math.Round(d.Value * 100m) : null;

        private static long? Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

        private static decimal? Dollars(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i) / 100m;
    }
}
