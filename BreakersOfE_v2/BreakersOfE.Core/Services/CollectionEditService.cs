using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>Result of one add or remove.</summary>
    public sealed class EditResult
    {
        /// <summary>Copies actually added or removed.</summary>
        public int Changed { get; init; }
        /// <summary>Plain-language outcome for the status line.</summary>
        public string Message { get; init; } = "";
        /// <summary>Something was refused or only partly done (shown as a warning).</summary>
        public bool Warning { get; init; }
    }

    /// <summary>Owned and in-use copies of one printing, per finish.</summary>
    public sealed class FinishCounts
    {
        public int NonFoil, Foil, Etched;
        public int UsedNonFoil, UsedFoil, UsedEtched;
        public int Total => NonFoil + Foil + Etched;
        public int Owned(string finish) => CardFinish.Normalize(finish) switch
        {
            CardFinish.Foil => Foil,
            CardFinish.Etched => Etched,
            _ => NonFoil,
        };
    }

    /// <summary>
    /// Collection editing — THE one place every add and remove goes through,
    /// whatever button, key or menu started it (v1 lesson: gestures that each
    /// did their own thing drifted apart).
    ///
    /// • Rows are per printing AND finish (ScryfallId + Finish); etched is a
    ///   real finish. A v1 foil row of an etched-only printing is the etched row
    ///   (it is rewritten as etched the first time it is edited).
    /// • Copies used by decks or the Trade Binder (UsedCount) are never removed.
    /// • Before the first change of the session, collection.db is backed up to
    ///   Documents\BoE_V2\Backups (the newest 10 backups are kept).
    /// Works for the main collection and the six special collections.
    /// </summary>
    public static class CollectionEditService
    {
        // ── Table pairs ─────────────────────────────────────────────────
        /// <summary>Pool table tag → its collection table tag.</summary>
        public static string CollectionTagFor(string poolTag) => poolTag switch
        {
            "Tokens" => "CollTokens",
            "Planes" => "CollPlanes",
            "Schemes" => "CollSchemes",
            "Vanguards" => "CollVanguards",
            "ArtSeries" => "CollArtSeries",
            "Conspiracies" => "CollConspiracies",
            _ => "Collection",
        };

        /// <summary>The pool card for a printing (from the pool table matching a collection tag).</summary>
        public static object? FindPoolCard(string collTag, string scryfallId)
        {
            if (string.IsNullOrEmpty(scryfallId)) return null;
            try
            {
                using var db = new AppDbContext();
                return collTag switch
                {
                    "CollTokens" => db.TokenCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    "CollPlanes" => db.PlanarCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    "CollSchemes" => db.SchemeCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    "CollVanguards" => db.VanguardCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    "CollArtSeries" => db.ArtSeriesCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    "CollConspiracies" => db.ConspiracyCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                    _ => (object?)db.PoolCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId),
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FindPoolCard: {ex.Message}");
                return null;
            }
        }

        /// <summary>Which finishes a pool printing exists in.</summary>
        public static (bool nonFoil, bool foil, bool etched) FinishesOf(object poolCard) =>
            (GetBool(poolCard, "IsNonFoil"), GetBool(poolCard, "IsFoil"), GetBool(poolCard, "IsEtched"));

        // ── Counts ──────────────────────────────────────────────────────
        /// <summary>
        /// Owned and in-use copies of one printing in one collection table, per
        /// finish (a v1 foil row of an etched-only printing counts as etched).
        /// </summary>
        public static FinishCounts Counts(string collTag, string scryfallId, bool etchedOnly)
        {
            var counts = new FinishCounts();
            if (string.IsNullOrEmpty(scryfallId)) return counts;
            try
            {
                using var db = new CollectionDbContext();
                foreach (var row in Rows(db, collTag, scryfallId))
                {
                    string f = CardFinish.Shown(row.Finish, etchedOnly);
                    int used = Math.Min(row.UsedCount, row.Quantity);
                    switch (f)
                    {
                        case CardFinish.Foil: counts.Foil += row.Quantity; counts.UsedFoil += used; break;
                        case CardFinish.Etched: counts.Etched += row.Quantity; counts.UsedEtched += used; break;
                        default: counts.NonFoil += row.Quantity; counts.UsedNonFoil += used; break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Collection counts: {ex.Message}");
            }
            return counts;
        }

        // ── Add ─────────────────────────────────────────────────────────
        /// <summary>Add <paramref name="qty"/> copies of a pool printing in one finish.</summary>
        public static EditResult Add(string collTag, object poolCard, string finish, int qty)
        {
            finish = CardFinish.Normalize(finish);
            string label = Label(poolCard, finish);
            if (qty < 1) return new EditResult { Message = "Enter a quantity of 1 or more.", Warning = true };

            var (nf, f, e) = FinishesOf(poolCard);
            bool exists = finish switch { CardFinish.Foil => f, CardFinish.Etched => e, _ => nf };
            if (!exists)
                return new EditResult { Message = $"{label}: this printing doesn't exist in {CardFinish.Display(finish)}.", Warning = true };

            string sid = GetString(poolCard, "ScryfallId");
            bool etchedOnly = e && !f;
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var row = FindRow(db, collTag, sid, finish, etchedOnly);
                decimal? price = PriceOf(poolCard, finish);

                if (row == null)
                {
                    row = NewRow(collTag, poolCard);
                    row.Finish = finish;
                    row.Quantity = qty;
                    row.Price = price;
                    row.DateAdded = DateTime.Now;
                    row.DateModified = DateTime.Now;
                    db.Add(row.Entity);
                }
                else
                {
                    row.Finish = finish;             // a v1 foil row of an etched-only printing becomes etched
                    row.Quantity += qty;
                    if (price.HasValue) row.Price = price;
                    row.DateModified = DateTime.Now;
                }
                db.SaveChanges();
                return new EditResult { Changed = qty, Message = $"Added {qty} × {label}  (you now own {row.Quantity})." };
            }
            catch (Exception ex)
            {
                return new EditResult { Message = $"Could not add {label}: {ex.Message}", Warning = true };
            }
        }

        // ── Remove ──────────────────────────────────────────────────────
        /// <summary>
        /// Remove up to <paramref name="qty"/> copies of one finish of a printing
        /// (int.MaxValue = all). Copies used by decks or the Trade Binder stay.
        /// </summary>
        public static EditResult Remove(string collTag, string scryfallId, string name, string finish,
                                        int qty, bool etchedOnly)
        {
            finish = CardFinish.Normalize(finish);
            string label = $"{CardFinish.Display(finish)} {name}";
            if (qty < 1) return new EditResult { Message = "Enter a quantity of 1 or more.", Warning = true };

            try
            {
                using var db = new CollectionDbContext();
                var row = FindRow(db, collTag, scryfallId, finish, etchedOnly);
                if (row == null || row.Quantity <= 0)
                    return new EditResult { Message = $"You don't own any {label}.", Warning = true };

                int used = Math.Min(Math.Max(row.UsedCount, 0), row.Quantity);
                int free = row.Quantity - used;
                if (free <= 0)
                    return new EditResult
                    {
                        Message = $"All {row.Quantity} {label} are in use ({UsedBy(db, collTag, scryfallId)}) — nothing removed.",
                        Warning = true,
                    };

                EnsureBackup();
                int take = Math.Min(qty == int.MaxValue ? free : qty, free);
                row.Quantity -= take;
                row.Finish = finish;                  // v1 foil row of an etched-only printing → etched
                row.DateModified = DateTime.Now;
                if (row.Quantity <= 0) db.Remove(row.Entity);
                db.SaveChanges();

                bool partial = qty != int.MaxValue && take < qty;
                string left = row.Quantity > 0 ? $"you now own {row.Quantity}" : "none left";
                string inUse = used > 0 ? $"; {used} in use ({UsedBy(db, collTag, scryfallId)}) kept" : "";
                return new EditResult
                {
                    Changed = take,
                    Message = $"Removed {take} × {label}  ({left}{inUse}).",
                    Warning = partial,
                };
            }
            catch (Exception ex)
            {
                return new EditResult { Message = $"Could not remove {label}: {ex.Message}", Warning = true };
            }
        }

        // ── Set quantity (double-click the Qty cell) ────────────────────
        /// <summary>
        /// Set one finish of a printing to exactly <paramref name="newQty"/>
        /// copies. Never below the copies in use by decks or the Trade Binder.
        /// 0 removes the row (when nothing is in use).
        /// </summary>
        public static EditResult SetQuantity(string collTag, string scryfallId, string name, string finish,
                                             int newQty, bool etchedOnly)
        {
            finish = CardFinish.Normalize(finish);
            string label = $"{CardFinish.Display(finish)} {name}";
            if (newQty < 0) return new EditResult { Message = "Quantity can't be negative.", Warning = true };

            try
            {
                using var db = new CollectionDbContext();
                var row = FindRow(db, collTag, scryfallId, finish, etchedOnly);
                if (row == null)
                    return new EditResult { Message = $"You don't own any {label}.", Warning = true };

                int old = row.Quantity;
                int used = Math.Min(Math.Max(row.UsedCount, 0), old);
                int target = Math.Max(newQty, used);
                if (target == old)
                    return new EditResult
                    {
                        Message = newQty < used
                            ? $"{label}: {used} in use ({UsedBy(db, collTag, scryfallId)}) — can't go below {used}."
                            : $"{label}: quantity unchanged ({old}).",
                        Warning = newQty < used,
                    };

                EnsureBackup();
                row.Quantity = target;
                row.Finish = finish;                  // v1 foil row of an etched-only printing → etched
                row.DateModified = DateTime.Now;
                if (target == 0) db.Remove(row.Entity);
                db.SaveChanges();

                string msg = target == 0
                    ? $"Removed all {old} × {label}."
                    : $"{label}: quantity {old} → {target}.";
                if (newQty < used) msg += $"  {used} in use ({UsedBy(db, collTag, scryfallId)}) kept.";
                return new EditResult { Changed = Math.Abs(target - old), Message = msg, Warning = newQty < used };
            }
            catch (Exception ex)
            {
                return new EditResult { Message = $"Could not change {label}: {ex.Message}", Warning = true };
            }
        }

        // ── Backup (once per session, before the first change) ─────────
        private static bool _backedUp;
        private const int BackupsKept = 10;

        public static void EnsureBackup()
        {
            if (_backedUp) return;
            string source = AppFolderService.CollectionDatabasePath;
            if (!File.Exists(source)) { _backedUp = true; return; }

            string target = Path.Combine(AppFolderService.BackupsFolder,
                $"collection_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            using (var src = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
            using (var dst = new SqliteConnection($"Data Source={target};Pooling=False"))
            {
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);          // consistent copy, even while the app reads it
            }
            _backedUp = true;

            try
            {
                foreach (var old in new DirectoryInfo(AppFolderService.BackupsFolder)
                             .GetFiles("collection_*.db")
                             .OrderByDescending(f => f.Name)
                             .Skip(BackupsKept))
                    old.Delete();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup cleanup: {ex.Message}");
            }
        }

        // ── Rows (any of the 7 collection tables, through one small wrapper) ──
        /// <summary>A collection row of any table, by its shared fields.</summary>
        private sealed class Row
        {
            public object Entity { get; }
            public Row(object entity) => Entity = entity;
            // Not every table has every field (e.g. Conspiracy rows have no
            // UsedCount): a missing field reads as 0/empty and writes are skipped.
            private PropertyInfo? P(string n) => Entity.GetType().GetProperty(n);
            public string ScryfallId => P("ScryfallId")?.GetValue(Entity) as string ?? "";
            public string Finish { get => P("Finish")?.GetValue(Entity) as string ?? ""; set => P("Finish")?.SetValue(Entity, value); }
            public int Quantity { get => P("Quantity")?.GetValue(Entity) is int q ? q : 0; set => P("Quantity")?.SetValue(Entity, value); }
            public int UsedCount => P("UsedCount")?.GetValue(Entity) is int u ? u : 0;
            public decimal? Price { get => P("Price")?.GetValue(Entity) as decimal?; set => P("Price")?.SetValue(Entity, value); }
            public DateTime DateAdded { set => P("DateAdded")?.SetValue(Entity, value); }
            public DateTime DateModified { set => P("DateModified")?.SetValue(Entity, value); }
        }

        private static List<Row> Rows(CollectionDbContext db, string collTag, string sid)
        {
            IEnumerable<object> rows = collTag switch
            {
                "CollTokens" => db.TokenCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                "CollPlanes" => db.PlanarCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                "CollSchemes" => db.SchemeCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                "CollVanguards" => db.VanguardCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                "CollArtSeries" => db.ArtSeriesCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                "CollConspiracies" => db.ConspiracyCollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
                _ => db.CollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
            };
            return rows.Select(r => new Row(r)).ToList();
        }

        /// <summary>The row for this printing + finish (etched: also a v1 foil row of an etched-only printing).</summary>
        private static Row? FindRow(CollectionDbContext db, string collTag, string sid, string finish, bool etchedOnly)
        {
            var rows = Rows(db, collTag, sid);
            return rows.FirstOrDefault(r => CardFinish.Normalize(r.Finish) == finish)
                ?? (finish == CardFinish.Etched && etchedOnly
                    ? rows.FirstOrDefault(r => CardFinish.Normalize(r.Finish) == CardFinish.Foil)
                    : null);
        }

        private static Type EntryType(string collTag) => collTag switch
        {
            "CollTokens" => typeof(TokenCollectionEntry),
            "CollPlanes" => typeof(PlanarCollectionEntry),
            "CollSchemes" => typeof(SchemeCollectionEntry),
            "CollVanguards" => typeof(VanguardCollectionEntry),
            "CollArtSeries" => typeof(ArtSeriesCollectionEntry),
            "CollConspiracies" => typeof(ConspiracyCollectionEntry),
            _ => typeof(CollectionEntry),
        };

        /// <summary>
        /// A new collection row carrying the pool card's data: every stored
        /// field the two share by name and type is copied (card text, set,
        /// images, prices, legality…), plus the finish-availability flags.
        /// </summary>
        private static Row NewRow(string collTag, object poolCard)
        {
            var type = EntryType(collTag);
            var entity = Activator.CreateInstance(type)!;
            var skip = new HashSet<string>(StringComparer.Ordinal)
            {
                "Quantity", "FoilQuantity", "Finish", "Price", "UsedCount",
                "DateAdded", "DateModified", "RowIndex", "IsFoil", "IsNonFoil", "IsEtched",
            };
            var src = poolCard.GetType();
            foreach (var to in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!to.CanWrite || skip.Contains(to.Name)) continue;
                if (to.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null) continue;
                if (to.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() != null) continue;
                var from = src.GetProperty(to.Name, BindingFlags.Public | BindingFlags.Instance);
                if (from == null || !from.CanRead || from.PropertyType != to.PropertyType) continue;
                to.SetValue(entity, from.GetValue(poolCard));
            }
            type.GetProperty("IsFoilAvailable")?.SetValue(entity, GetBool(poolCard, "IsFoil") || GetBool(poolCard, "IsEtched"));
            type.GetProperty("IsNonFoilAvailable")?.SetValue(entity, GetBool(poolCard, "IsNonFoil"));
            return new Row(entity);
        }

        // ── Helpers ─────────────────────────────────────────────────────
        private static decimal? PriceOf(object card, string finish) =>
            CardFinish.PriceFor(finish,
                card.GetType().GetProperty("PriceUsd")?.GetValue(card) as decimal?,
                card.GetType().GetProperty("PriceUsdFoil")?.GetValue(card) as decimal?,
                card.GetType().GetProperty("PriceUsdEtched")?.GetValue(card) as decimal?);

        private static string Label(object card, string finish)
        {
            string set = GetString(card, "SetCode").ToUpperInvariant();
            string no = GetString(card, "CollectorNumber");
            return $"{CardFinish.Display(finish)} {GetString(card, "Name")} ({set} #{no})";
        }

        /// <summary>"Deck A, Deck B" — who uses this printing (main collection only).</summary>
        private static string UsedBy(CollectionDbContext db, string collTag, string sid)
        {
            if (collTag != "Collection") return "in use";
            try
            {
                var names = db.DeckUsages.Where(u => u.ScryfallId == sid && (u.EnteredNonFoil + u.EnteredFoil) > 0)
                    .Select(u => u.DeckName).Distinct().ToList();
                return names.Count == 0 ? "used by decks" : "used by " + string.Join(", ", names);
            }
            catch { return "used by decks"; }
        }

        private static bool GetBool(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) is bool b && b;

        private static string GetString(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) as string ?? "";
    }
}