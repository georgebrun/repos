using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// What <see cref="CollectionEditService.MoveRowsToTheirTables"/> did:
    /// "Anje Falkenrath (OC19) → Oversized" for each printing moved, and the
    /// ones left in Cards because a deck or the Trade Binder uses them.
    /// </summary>
    public sealed record SortReport(List<string> Moved, List<string> Kept)
    {
        public bool Any => Moved.Count > 0 || Kept.Count > 0;
    }

    /// <summary>
    /// Collection → Cards holds real Magic cards only. Tokens (emblems,
    /// dungeons, helper and substitute cards …), oversized cards and front
    /// cards have their own tables; rows added to Cards before those rules
    /// (or by an older version) are moved there after each full update.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>
        /// Move every Collection → Cards row whose printing the card pool now
        /// keeps in Tokens, Oversized or Front Cards to that collection table,
        /// with its copies, finish, language, condition, notes and storage
        /// (merged into a matching row there, if there is one). Rows with
        /// copies in use (a deck or the Trade Binder) stay where they are and
        /// are listed. Collection is backed up first. Safe to run every time:
        /// once moved, there is nothing left to move.
        /// </summary>
        public static SortReport MoveRowsToTheirTables()
        {
            var moved = new List<string>();
            var kept = new List<string>();

            using var db = new CollectionDbContext();
            var rows = db.CollectionEntries.ToList();
            if (rows.Count == 0) return new SortReport(moved, kept);

            // Where each printing in Cards belongs now (only the ones that moved tables).
            var sids = rows.Select(r => r.ScryfallId).Where(s => !string.IsNullOrEmpty(s))
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var poolCards = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            using (var pdb = new AppDbContext())
            {
                for (int i = 0; i < sids.Count; i += 400)
                {
                    var chunk = sids.Skip(i).Take(400).ToList();
                    // Still a regular card: stays.
                    var regular = new HashSet<string>(pdb.PoolCards.AsNoTracking()
                        .Where(c => chunk.Contains(c.ScryfallId)).Select(c => c.ScryfallId), StringComparer.OrdinalIgnoreCase);
                    void Note(string table, IEnumerable<object> cards)
                    {
                        foreach (var c in cards)
                        {
                            string sid = GetString(c, "ScryfallId");
                            if (regular.Contains(sid) || target.ContainsKey(sid)) continue;
                            target[sid] = table;
                            poolCards[sid] = c;
                        }
                    }
                    Note(OversizedTable, pdb.OversizedCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList());
                    Note(TokensTable, pdb.TokenCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList());
                    Note(FrontTable, pdb.FrontCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList());
                }
            }
            if (target.Count == 0) return new SortReport(moved, kept);

            var toMove = rows.Where(r => target.ContainsKey(r.ScryfallId ?? "")).ToList();
            if (toMove.Count == 0) return new SortReport(moved, kept);

            EnsureBackup();
            using var tx = db.Database.BeginTransaction();
            var movedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var keptNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in toMove)
            {
                string table = target[row.ScryfallId];
                string label = $"{row.Name} ({row.SetCode.ToUpperInvariant()} #{row.CollectorNumber})";
                if (row.UsedCount > 0)
                {
                    keptNames.Add(label);
                    continue;
                }

                // A matching row there already (same finish, language and condition): add the copies to it.
                var key = RowKey.Of(row.ScryfallId, row.Finish, row.Language, row.Condition);
                var there = Rows(db, table, row.ScryfallId)
                    .FirstOrDefault(r => RowKey.Of(r.ScryfallId, r.Finish, r.Language, r.Condition) == key);
                if (there != null)
                {
                    there.Quantity += row.Quantity;
                    there.DateModified = DateTime.Now;
                }
                else
                {
                    var fresh = NewRow(table, poolCards[row.ScryfallId]);
                    var e = fresh.Entity;
                    void Set(string name, object? value)
                    {
                        var p = e.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                        if (p != null && p.CanWrite && (value == null || p.PropertyType.IsInstanceOfType(value) ||
                                                        Nullable.GetUnderlyingType(p.PropertyType)?.IsInstanceOfType(value) == true))
                            p.SetValue(e, value);
                    }
                    Set("Quantity", row.Quantity);
                    Set("Finish", row.Finish);
                    Set("Price", row.Price);
                    Set("Language", row.Language);
                    Set("Condition", row.Condition);
                    Set("Notes", row.Notes);
                    Set("StorageLocation", row.StorageLocation);
                    Set("IsFavorite", row.IsFavorite);
                    Set("DateAdded", row.DateAdded);
                    Set("DateModified", DateTime.Now);
                    db.Add(e);
                }
                db.CollectionEntries.Remove(row);
                movedNames.Add($"{label} → {TableName(table)}");
            }

            db.SaveChanges();
            tx.Commit();
            moved.AddRange(movedNames);
            kept.AddRange(keptNames);
            return new SortReport(moved, kept);
        }

        /// <summary>"Tokens", "Oversized", "Front Cards" (a collection table's name in the side menu).</summary>
        private static string TableName(string table) => table switch
        {
            TokensTable => "Tokens",
            OversizedTable => "Oversized",
            FrontTable => "Front Cards",
            _ => "Cards",
        };
    }
}
