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
    /// <summary>
    /// What makes a collection row one row: the printing, its finish, the
    /// language and the condition (all normalized: "Near Mint", "English", …).
    /// </summary>
    public sealed record RowKey(string ScryfallId, string Finish, string Language, string Condition)
    {
        public static RowKey Of(string sid, string? finish, string? language, string? condition) =>
            new(sid ?? "", CardFinish.Normalize(finish),
                CardLanguage.Normalize(language), CardCondition.Normalize(condition));

        /// <summary>"Foil · Japanese · Lightly Played".</summary>
        public string Text => $"{CardFinish.Display(Finish)} · {Language} · {Condition}";
    }

    /// <summary>Result of one edit.</summary>
    public sealed class EditResult
    {
        /// <summary>Copies (or rows, for notes and favorites) actually changed.</summary>
        public int Changed { get; init; }
        /// <summary>Plain-language outcome for the status line.</summary>
        public string Message { get; init; } = "";
        /// <summary>Something was refused or only partly done (shown as a warning).</summary>
        public bool Warning { get; init; }
        /// <summary>The rows the edit ended up in (the table re-selects these).</summary>
        public List<RowKey> Touched { get; init; } = new();
    }

    /// <summary>Owned and in-use copies of one printing, per finish (all languages and conditions).</summary>
    public sealed class FinishCounts
    {
        public int NonFoil, Foil, Etched;
        public int UsedNonFoil, UsedFoil, UsedEtched;
        /// <summary>How many rows (finish × language × condition) the printing has.</summary>
        public int Rows;
        public int Total => NonFoil + Foil + Etched;
        public int Used => UsedNonFoil + UsedFoil + UsedEtched;
        public int Owned(string finish) => CardFinish.Normalize(finish) switch
        {
            CardFinish.Foil => Foil,
            CardFinish.Etched => Etched,
            _ => NonFoil,
        };
    }

    /// <summary>
    /// The rows of one or more printings as they were before an edit — what
    /// Undo puts back. Opaque to the page.
    /// </summary>
    public sealed class EditSnapshot
    {
        internal string CollTag = "";
        internal HashSet<string> Sids = new(StringComparer.Ordinal);
        internal List<Dictionary<string, object?>> Rows = new();
        /// <summary>The same printings right after the edit: Undo only runs if nothing changed them since.</summary>
        internal List<Dictionary<string, object?>>? After;
        /// <summary>The collection table the snapshot belongs to.</summary>
        public string Table => CollTag;
        /// <summary>The printings (Scryfall ids) it covers.</summary>
        public IReadOnlyCollection<string> Printings => Sids;
    }

    /// <summary>
    /// Collection editing — THE one place every change goes through, whatever
    /// button, key, menu or cell started it (v1 lesson: gestures that each did
    /// their own thing drifted apart).
    ///
    /// • A row is one printing + finish + language + condition (see
    ///   <see cref="RowKey"/>); etched is a real finish. A v1 foil row of an
    ///   etched-only printing is the etched row (rewritten as etched when edited).
    /// • Copies used by decks or the Trade Binder (UsedCount) are never removed
    ///   or moved to another row.
    /// • Before the first change of the session, collection.db is backed up to
    ///   Documents\BoE_V2\Backups (the newest 10 backups are kept).
    /// • <see cref="Snapshot"/> / <see cref="Restore"/> give the page its Undo.
    /// Works for the main collection and the six special collections.
    /// </summary>
    public static partial class CollectionEditService
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
            "MtgoCards" => "MtgoCollection",
            "ArenaCards" => "ArenaCollection",
            _ => "Collection",
        };

        /// <summary>"mtgo" / "arena" for the online collection tables, else null (paper).</summary>
        public static string? OnlineGameOf(string collTag) => collTag switch
        {
            "MtgoCollection" => OnlineGame.Mtgo,
            "ArenaCollection" => OnlineGame.Arena,
            _ => null,
        };

        /// <summary>An online collection (MTGO or Arena): no language, condition or etched.</summary>
        public static bool IsOnline(string collTag) => OnlineGameOf(collTag) != null;

        /// <summary>
        /// The key of a row in this table. Online rows have no language or
        /// condition, so theirs are always the defaults (English · Unknown).
        /// </summary>
        public static RowKey KeyFor(string collTag, string sid, string? finish, string? language, string? condition) =>
            IsOnline(collTag) ? RowKey.Of(sid, finish, null, null) : RowKey.Of(sid, finish, language, condition);

        /// <summary>The pool card for a printing (from the pool table matching a collection tag).</summary>
        public static object? FindPoolCard(string collTag, string scryfallId)
        {
            if (string.IsNullOrEmpty(scryfallId)) return null;
            try
            {
                if (IsOnline(collTag))
                {
                    using var odb = new OnlineDbContext();
                    return odb.OnlineCards.AsNoTracking().FirstOrDefault(c => c.ScryfallId == scryfallId);
                }
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

        /// <summary>
        /// Which finishes a printing exists in, in this table's game. Paper:
        /// Scryfall's finishes. MTGO: non-foil if it has an MTGO id, foil
        /// (Premium) if it has an MTGO foil id. Arena: non-foil only.
        /// </summary>
        public static (bool nonFoil, bool foil, bool etched) FinishesOf(string collTag, object poolCard)
        {
            switch (OnlineGameOf(collTag))
            {
                case OnlineGame.Arena:
                    return (true, false, false);
                case OnlineGame.Mtgo:
                    bool nf = GetInt(poolCard, "MtgoId") != null;
                    bool f = GetInt(poolCard, "MtgoFoilId") != null;
                    return (nf || !f, f, false);        // no ids at all: treat as non-foil
                default:
                    return (GetBool(poolCard, "IsNonFoil"), GetBool(poolCard, "IsFoil"), GetBool(poolCard, "IsEtched"));
            }
        }

        /// <summary>Does a printing with these finishes come in <paramref name="finish"/>?</summary>
        public static bool FinishExists(string finish, bool nonFoil, bool foil, bool etched) =>
            CardFinish.Normalize(finish) switch
            {
                CardFinish.Foil => foil,
                CardFinish.Etched => etched,
                _ => nonFoil,
            };

        /// <summary>The stored key (id) of a collection row, or 0.</summary>
        public static int RowId(object row)
        {
            var key = row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);
            return key?.GetValue(row) is int id ? id : 0;
        }

        // ── Counts ──────────────────────────────────────────────────────
        /// <summary>
        /// Owned and in-use copies of one printing in one collection table, per
        /// finish, over every language and condition (a v1 foil row of an
        /// etched-only printing counts as etched).
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
                    if (row.Quantity <= 0) continue;
                    counts.Rows++;
                    int used = Math.Min(row.UsedCount, row.Quantity);
                    switch (CardFinish.Shown(row.Finish, etchedOnly))
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
        /// <summary>
        /// Add <paramref name="qty"/> copies of a pool printing in one finish,
        /// language and condition (into the matching row, or a new one).
        /// </summary>
        public static EditResult Add(string collTag, object poolCard, string finish, int qty,
                                     string? language, string? condition)
        {
            string sid = GetString(poolCard, "ScryfallId");
            var key = KeyFor(collTag, sid, finish, language, condition);
            string label = Label(poolCard, key, collTag);
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");

            // The finish must exist for this printing — checked against the card
            // database itself, never only the object the page passed in.
            var (nf, f, e) = FinishesOf(collTag, FindPoolCard(collTag, sid) ?? poolCard);
            if (!FinishExists(key.Finish, nf, f, e))
                return Fail($"{Name(poolCard)} ({GetString(poolCard, "SetCode").ToUpperInvariant()} #{GetString(poolCard, "CollectorNumber")}) " +
                            $"doesn't come in {CardFinish.Display(key.Finish)} — nothing added.");

            bool etchedOnly = e && !f;
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var row = FindExact(Rows(db, collTag, sid), key, etchedOnly);
                decimal? price = PriceOf(collTag, poolCard, key.Finish);

                if (row == null)
                {
                    row = NewRow(collTag, poolCard);
                    row.Finish = key.Finish;
                    row.Language = key.Language;
                    row.Condition = key.Condition;
                    row.Quantity = qty;
                    row.Price = price;
                    row.DateAdded = DateTime.Now;
                    row.DateModified = DateTime.Now;
                    db.Add(row.Entity);
                }
                else
                {
                    row.WriteKey(key);              // tidy: normalized values, v1 foil → etched
                    row.Quantity += qty;
                    if (price.HasValue) row.Price = price;
                    row.DateModified = DateTime.Now;
                }
                db.SaveChanges();
                return new EditResult
                {
                    Changed = qty,
                    Message = $"Added {qty} × {label}  (this row now {row.Quantity}).",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not add {label}: {ex.Message}");
            }
        }

        // ── Remove (by printing + finish) ───────────────────────────────
        /// <summary>
        /// Remove up to <paramref name="qty"/> copies (int.MaxValue = all) of a
        /// printing in one finish. The row is the one with this language and
        /// condition. With <paramref name="allowFallback"/> (removing from the
        /// pool table), if there is no such row but the finish has exactly one
        /// row, that row is used; several rows → refused, with the rows listed.
        /// Copies used by decks or the Trade Binder stay.
        /// </summary>
        public static EditResult Remove(string collTag, string scryfallId, string name, string finish,
                                        int qty, bool etchedOnly, string? language, string? condition,
                                        bool allowFallback)
        {
            var key = KeyFor(collTag, scryfallId, finish, language, condition);
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            try
            {
                using var db = new CollectionDbContext();
                var rows = Rows(db, collTag, scryfallId);
                var row = FindExact(rows, key, etchedOnly);
                if ((row == null || row.Quantity <= 0) && !allowFallback)
                    return Fail($"You don't own any {name} ({TextFor(collTag, key)}).");
                if (row == null || row.Quantity <= 0)
                {
                    var same = rows.Where(r => r.Quantity > 0 && ShownFinish(r, etchedOnly) == key.Finish).ToList();
                    if (same.Count == 0)
                        return Fail($"You don't own any {CardFinish.Display(key.Finish)} {name}.");
                    if (same.Count > 1)
                        return Fail($"{CardFinish.Display(key.Finish)} {name} is in {same.Count} rows (" +
                                    string.Join("; ", same.Select(r => $"{r.Quantity} {r.Language} · {r.Condition}")) +
                                    ") — select the row in the collection table.");
                    row = same[0];
                }
                return TakeFrom(db, collTag, row, qty, name, etchedOnly);
            }
            catch (Exception ex)
            {
                return Fail($"Could not remove {name}: {ex.Message}");
            }
        }

        /// <summary>Remove up to <paramref name="qty"/> copies from one exact row (int.MaxValue = all unused).</summary>
        public static EditResult RemoveFromRow(string collTag, int rowId, int qty, bool etchedOnly)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            try
            {
                using var db = new CollectionDbContext();
                var row = FindById(db, collTag, rowId);
                if (row == null) return Fail("That row is no longer in the collection.");
                return TakeFrom(db, collTag, row, qty, row.Name, etchedOnly);
            }
            catch (Exception ex)
            {
                return Fail($"Could not remove: {ex.Message}");
            }
        }

        /// <summary>Every unused copy of a printing: all finishes, languages and conditions.</summary>
        public static EditResult RemoveAllOfPrinting(string collTag, string scryfallId, string name, bool etchedOnly)
        {
            try
            {
                using var db = new CollectionDbContext();
                var rows = Rows(db, collTag, scryfallId).Where(r => r.Quantity > 0).ToList();
                if (rows.Count == 0) return Fail($"You don't own any {name}.");

                int removed = 0, kept = 0;
                var touched = new List<RowKey>();
                foreach (var row in rows)
                {
                    int used = Math.Min(Math.Max(row.UsedCount, 0), row.Quantity);
                    int take = row.Quantity - used;
                    kept += used;
                    if (take <= 0) { touched.Add(KeyOf(row, etchedOnly)); continue; }
                    EnsureBackup();
                    row.Quantity -= take;
                    row.DateModified = DateTime.Now;
                    touched.Add(KeyOf(row, etchedOnly));
                    if (row.Quantity <= 0) db.Remove(row.Entity);
                    removed += take;
                }
                if (removed == 0)
                    return Fail($"Every copy of {name} is in use ({UsedBy(db, collTag, scryfallId)}) — nothing removed.");
                db.SaveChanges();
                string inUse = kept > 0 ? $"; {kept} in use ({UsedBy(db, collTag, scryfallId)}) kept" : "";
                return new EditResult { Changed = removed, Message = $"Removed {removed} × {name} (all unused copies{inUse}).", Touched = touched };
            }
            catch (Exception ex)
            {
                return Fail($"Could not remove {name}: {ex.Message}");
            }
        }

        private static EditResult TakeFrom(CollectionDbContext db, string collTag, Row row, int qty, string name, bool etchedOnly)
        {
            var key = KeyOf(row, etchedOnly);
            string label = $"{name} ({TextFor(collTag, key)})";
            int used = Math.Min(Math.Max(row.UsedCount, 0), row.Quantity);
            int free = row.Quantity - used;
            if (free <= 0)
                return Fail($"All {row.Quantity} {label} are in use ({UsedBy(db, collTag, row.ScryfallId)}) — nothing removed.", key);

            EnsureBackup();
            int take = Math.Min(qty == int.MaxValue ? free : qty, free);
            row.WriteKey(key);
            row.Quantity -= take;
            row.DateModified = DateTime.Now;
            if (row.Quantity <= 0) db.Remove(row.Entity);
            db.SaveChanges();

            bool partial = qty != int.MaxValue && take < qty;
            string left = row.Quantity > 0 ? $"this row now {row.Quantity}" : "row removed";
            string inUse = used > 0 ? $"; {used} in use ({UsedBy(db, collTag, row.ScryfallId)}) kept" : "";
            return new EditResult
            {
                Changed = take,
                Message = $"Removed {take} × {label}  ({left}{inUse}).",
                Warning = partial,
                Touched = { key },
            };
        }

        // ── Set quantity (double-click the Qty cell) ────────────────────
        /// <summary>
        /// Set one row to exactly <paramref name="newQty"/> copies. Never below
        /// the copies in use by decks or the Trade Binder. 0 removes the row
        /// (when nothing is in use).
        /// </summary>
        public static EditResult SetQuantity(string collTag, int rowId, int newQty, bool etchedOnly)
        {
            if (newQty < 0) return Fail("Quantity can't be negative.");
            try
            {
                using var db = new CollectionDbContext();
                var row = FindById(db, collTag, rowId);
                if (row == null) return Fail("That row is no longer in the collection.");
                var key = KeyOf(row, etchedOnly);
                string label = $"{row.Name} ({TextFor(collTag, key)})";

                int old = row.Quantity;
                int used = Math.Min(Math.Max(row.UsedCount, 0), old);
                int target = Math.Max(newQty, used);
                if (target == old)
                    return new EditResult
                    {
                        Message = newQty < used
                            ? $"{label}: {used} in use ({UsedBy(db, collTag, row.ScryfallId)}) — can't go below {used}."
                            : $"{label}: quantity unchanged ({old}).",
                        Warning = newQty < used,
                        Touched = { key },
                    };

                EnsureBackup();
                row.WriteKey(key);
                row.Quantity = target;
                row.DateModified = DateTime.Now;
                if (target == 0) db.Remove(row.Entity);
                db.SaveChanges();

                string msg = target == 0 ? $"Removed all {old} × {label}." : $"{label}: quantity {old} → {target}.";
                if (newQty < used) msg += $"  {used} in use ({UsedBy(db, collTag, row.ScryfallId)}) kept.";
                return new EditResult { Changed = Math.Abs(target - old), Message = msg, Warning = newQty < used, Touched = { key } };
            }
            catch (Exception ex)
            {
                return Fail($"Could not change the quantity: {ex.Message}");
            }
        }

        // ── Move copies: change finish, language or condition ───────────
        /// <summary>
        /// Move up to <paramref name="qty"/> copies (int.MaxValue = all unused)
        /// of one row to another finish, language and/or condition (null = keep).
        /// They join the row that already has those values, or become a new row
        /// (notes, favorite and date added come along). Copies in use stay put.
        /// </summary>
        public static EditResult Move(string collTag, int rowId, int qty,
                                      string? newFinish, string? newLanguage, string? newCondition)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            try
            {
                using var db = new CollectionDbContext();
                var row = FindById(db, collTag, rowId);
                if (row == null) return Fail("That row is no longer in the collection.");

                object? pool = FindPoolCard(collTag, row.ScryfallId);
                var fins = pool != null ? FinishesOf(collTag, pool) : (true, true, true);
                if (pool == null && newFinish != null)
                    return Fail($"{row.Name}: not found in the card pool, so its finish can't be changed.");
                bool etchedOnly = fins.Item3 && !fins.Item2;
                var from = KeyOf(row, etchedOnly);
                var to = KeyFor(collTag, row.ScryfallId, newFinish ?? from.Finish,
                                newLanguage ?? from.Language, newCondition ?? from.Condition);
                string name = row.Name;

                if (to == from)
                    return new EditResult { Message = $"{name} is already {TextFor(collTag, from)}.", Touched = { from } };
                if (pool != null && to.Finish != from.Finish)
                {
                    bool exists = FinishExists(to.Finish, fins.Item1, fins.Item2, fins.Item3);
                    if (!exists)
                        return Fail($"{name}: this printing doesn't exist in {CardFinish.Display(to.Finish)}.", from);
                }

                int used = Math.Min(Math.Max(row.UsedCount, 0), row.Quantity);
                int free = row.Quantity - used;
                if (free <= 0)
                    return Fail($"All {row.Quantity} {name} ({TextFor(collTag, from)}) are in use ({UsedBy(db, collTag, row.ScryfallId)}) — nothing changed.", from);
                int take = Math.Min(qty == int.MaxValue ? free : qty, free);

                EnsureBackup();
                decimal? price = to.Finish != from.Finish && pool != null ? PriceOf(collTag, pool, to.Finish) : row.Price;
                var target = FindExact(Rows(db, collTag, row.ScryfallId).Where(r => r.Id != row.Id), to, etchedOnly);

                if (target == null && take == row.Quantity)
                {
                    // The whole row changes: just relabel it.
                    row.WriteKey(to);
                    row.Price = price;
                    row.DateModified = DateTime.Now;
                }
                else
                {
                    if (target == null)
                    {
                        target = CloneRow(db, collTag, row);
                        target.WriteKey(to);
                        target.Quantity = 0;
                        target.Price = price;
                        db.Add(target.Entity);
                    }
                    target.Quantity += take;
                    target.DateModified = DateTime.Now;
                    // Merging the whole row away: keep its notes and favorite.
                    if (take == row.Quantity)
                    {
                        if (target.Notes.Length == 0) target.Notes = row.Notes;
                        if (target.Storage.Length == 0) target.Storage = row.Storage;
                        if (row.IsFavorite) target.IsFavorite = true;
                    }
                    row.WriteKey(from);
                    row.Quantity -= take;
                    row.DateModified = DateTime.Now;
                    if (row.Quantity <= 0) db.Remove(row.Entity);
                }
                db.SaveChanges();

                string what = Describe(from, to);
                string kept = used > 0 ? $"  {used} in use ({UsedBy(db, collTag, row.ScryfallId)}) stay {TextFor(collTag, from)}." : "";
                bool partial = qty != int.MaxValue && take < qty;
                return new EditResult
                {
                    Changed = take,
                    Message = $"{name}: {take} {(take == 1 ? "copy" : "copies")} {what}.{kept}",
                    Warning = partial,
                    Touched = { to },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not change the row: {ex.Message}");
            }
        }

        private static string Describe(RowKey from, RowKey to)
        {
            var parts = new List<string>();
            if (from.Finish != to.Finish) parts.Add($"{CardFinish.Display(from.Finish)} → {CardFinish.Display(to.Finish)}");
            if (from.Language != to.Language) parts.Add($"{from.Language} → {to.Language}");
            if (from.Condition != to.Condition) parts.Add($"{from.Condition} → {to.Condition}");
            return string.Join(", ", parts);
        }

        // ── Notes and favorite (the row itself, nothing moves) ─────────
        /// <summary>Set a row's notes.</summary>
        public static EditResult SetNotes(string collTag, int rowId, string notes, bool etchedOnly) =>
            SetText(collTag, rowId, "Notes", "notes", notes, etchedOnly);

        /// <summary>Set a row's storage location (where the cards are kept).</summary>
        public static EditResult SetStorage(string collTag, int rowId, string storage, bool etchedOnly) =>
            SetText(collTag, rowId, "StorageLocation", "storage", storage, etchedOnly);

        /// <summary>One free-text field of a row (Notes, StorageLocation). Nothing moves.</summary>
        private static EditResult SetText(string collTag, int rowId, string field, string label, string value, bool etchedOnly)
        {
            value = (value ?? "").Trim();
            try
            {
                using var db = new CollectionDbContext();
                var row = FindById(db, collTag, rowId);
                if (row == null) return Fail("That row is no longer in the collection.");
                var prop = row.Entity.GetType().GetProperty(field);
                if (prop == null) return Fail($"This table has no {label}.");
                var key = KeyOf(row, etchedOnly);
                if ((prop.GetValue(row.Entity) as string ?? "") == value)
                    return new EditResult { Message = $"{row.Name}: {label} unchanged.", Touched = { key } };
                EnsureBackup();
                prop.SetValue(row.Entity, value);
                row.DateModified = DateTime.Now;
                db.SaveChanges();
                return new EditResult
                {
                    Changed = 1,
                    Message = value.Length == 0 ? $"{row.Name} ({TextFor(collTag, key)}): {label} cleared."
                                                : $"{row.Name} ({TextFor(collTag, key)}): {label} saved.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not save the {label}: {ex.Message}");
            }
        }

        /// <summary>Mark or unmark a row as a favorite.</summary>
        public static EditResult SetFavorite(string collTag, int rowId, bool favorite, bool etchedOnly)
        {
            try
            {
                using var db = new CollectionDbContext();
                var row = FindById(db, collTag, rowId);
                if (row == null) return Fail("That row is no longer in the collection.");
                var key = KeyOf(row, etchedOnly);
                if (row.IsFavorite == favorite) return new EditResult { Touched = { key } };
                EnsureBackup();
                row.IsFavorite = favorite;
                row.DateModified = DateTime.Now;
                db.SaveChanges();
                return new EditResult
                {
                    Changed = 1,
                    Message = $"{row.Name} ({TextFor(collTag, key)}): {(favorite ? "marked as a favorite" : "no longer a favorite")}.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not change the favorite: {ex.Message}");
            }
        }

        // ── Undo ────────────────────────────────────────────────────────
        /// <summary>Every row of these printings, as stored now (take it BEFORE an edit).</summary>
        public static EditSnapshot? Snapshot(string collTag, IEnumerable<string> scryfallIds)
        {
            var snap = new EditSnapshot { CollTag = collTag };
            foreach (var s in scryfallIds) if (!string.IsNullOrEmpty(s)) snap.Sids.Add(s);
            if (snap.Sids.Count == 0) return null;
            try
            {
                snap.Rows = Capture(collTag, snap.Sids);
                return snap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Snapshot: {ex.Message}");
                return null;
            }
        }

        /// <summary>Record the printings as they are right AFTER the edit (Undo's safety check).</summary>
        public static void MarkAfter(EditSnapshot snap)
        {
            try { snap.After = Capture(snap.CollTag, snap.Sids); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Snapshot after: {ex.Message}");
                snap.After = null;
            }
        }

        private static List<Dictionary<string, object?>> Capture(string collTag, IEnumerable<string> sids)
        {
            using var db = new CollectionDbContext();
            var list = new List<Dictionary<string, object?>>();
            foreach (var row in RowsMany(db, collTag, sids))
                list.Add(db.Entry(row.Entity).Properties
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
            return list;
        }

        /// <summary>Same rows with the same stored values (order doesn't matter)?</summary>
        private static bool SameRows(List<Dictionary<string, object?>> a, List<Dictionary<string, object?>> b, string keyName)
        {
            if (a.Count != b.Count) return false;
            var byId = b.ToDictionary(v => v[keyName]!);
            foreach (var va in a)
            {
                if (!byId.TryGetValue(va[keyName]!, out var vb)) return false;
                foreach (var (name, value) in va)
                    if (!vb.TryGetValue(name, out var other) || !Equals(value, other)) return false;
            }
            return true;
        }

        /// <summary>Would <see cref="Restore"/> apply (the printings are still exactly as the edit left them)?</summary>
        internal static bool CanRestore(EditSnapshot snap)
        {
            using var db = new CollectionDbContext();
            string keyName = db.Model.FindEntityType(EntryType(snap.CollTag))!.FindPrimaryKey()!.Properties[0].Name;
            return snap.After != null && SameRows(Capture(snap.CollTag, snap.Sids), snap.After, keyName);
        }

        /// <summary>
        /// Put the printings back exactly as the snapshot had them: rows made
        /// since are deleted, changed or deleted rows get their old values and
        /// ids back. Returns the restored rows' keys (to re-select them).
        /// </summary>
        public static EditResult Restore(EditSnapshot snap, string description)
        {
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var type = EntryType(snap.CollTag);
                var entityType = db.Model.FindEntityType(type)!;
                string keyName = entityType.FindPrimaryKey()!.Properties[0].Name;

                // Only if these printings are still exactly as the edit left
                // them — never overwrite a change made since (an import, a deck).
                if (snap.After == null || !SameRows(Capture(snap.CollTag, snap.Sids), snap.After, keyName))
                    return Fail($"Can't undo \"{description}\": those cards have changed since.");

                var touched = RestoreInto(db, snap);
                db.SaveChanges();
                return new EditResult { Changed = 1, Message = $"Undone: {description}", Touched = touched };
            }
            catch (Exception ex)
            {
                return Fail($"Could not undo: {ex.Message}");
            }
        }

        /// <summary>
        /// Put the snapshot's rows back on <paramref name="db"/> (not saved — the
        /// caller saves, so it can be part of a bigger change). Returns their keys.
        /// </summary>
        internal static List<RowKey> RestoreInto(CollectionDbContext db, EditSnapshot snap)
        {
            {
                var type = EntryType(snap.CollTag);
                var entityType = db.Model.FindEntityType(type)!;
                string keyName = entityType.FindPrimaryKey()!.Properties[0].Name;

                var current = RowsMany(db, snap.CollTag, snap.Sids);
                var byId = current.ToDictionary(r => r.Id);
                var keep = new HashSet<int>(snap.Rows.Select(v => v[keyName] is int i ? i : 0));

                foreach (var r in current.Where(r => !keep.Contains(r.Id)))
                    db.Remove(r.Entity);

                var touched = new List<RowKey>();
                var etchedCache = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var values in snap.Rows)
                {
                    int id = values[keyName] is int i ? i : 0;
                    object entity;
                    if (byId.TryGetValue(id, out var existing))
                    {
                        entity = existing.Entity;
                        var entry = db.Entry(entity);
                        foreach (var (name, value) in values)
                            if (name != keyName) entry.Property(name).CurrentValue = value;
                    }
                    else
                    {
                        // A row deleted since: back with its old id and values.
                        entity = Activator.CreateInstance(type)!;
                        foreach (var (name, value) in values)
                            type.GetProperty(name)?.SetValue(entity, value);
                        db.Add(entity);
                    }
                    var row = new Row(entity);
                    touched.Add(KeyOf(row, EtchedOnlyPrinting(snap.CollTag, row.ScryfallId, etchedCache)));
                }
                return touched;
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
            public int Id => RowId(Entity);
            public string ScryfallId => P("ScryfallId")?.GetValue(Entity) as string ?? "";
            public string Name => P("Name")?.GetValue(Entity) as string ?? "";
            public string Finish { get => P("Finish")?.GetValue(Entity) as string ?? ""; set => P("Finish")?.SetValue(Entity, value); }
            public string Language { get => P("Language")?.GetValue(Entity) as string ?? ""; set => P("Language")?.SetValue(Entity, value); }
            public string Condition { get => P("Condition")?.GetValue(Entity) as string ?? ""; set => P("Condition")?.SetValue(Entity, value); }
            public string Notes { get => P("Notes")?.GetValue(Entity) as string ?? ""; set => P("Notes")?.SetValue(Entity, value); }
            public string Storage { get => P("StorageLocation")?.GetValue(Entity) as string ?? ""; set => P("StorageLocation")?.SetValue(Entity, value); }
            public bool IsFavorite { get => P("IsFavorite")?.GetValue(Entity) is bool b && b; set => P("IsFavorite")?.SetValue(Entity, value); }
            public int Quantity { get => P("Quantity")?.GetValue(Entity) is int q ? q : 0; set => P("Quantity")?.SetValue(Entity, value); }
            public int UsedCount
            {
                get => P("UsedCount")?.GetValue(Entity) is int u ? u : 0;
                set => P("UsedCount")?.SetValue(Entity, value);          // only CollectionEditService.Claims sets it
            }
            public decimal? Price { get => P("Price")?.GetValue(Entity) as decimal?; set => P("Price")?.SetValue(Entity, value); }
            public DateTime DateAdded { set => P("DateAdded")?.SetValue(Entity, value); }
            public DateTime DateModified { set => P("DateModified")?.SetValue(Entity, value); }

            /// <summary>Store the key's normalized finish, language and condition.</summary>
            public void WriteKey(RowKey key)
            {
                Finish = key.Finish;
                Language = key.Language;
                Condition = key.Condition;
            }
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
                "MtgoCollection" => db.OnlineCollectionEntries
                    .Where(e => e.Game == OnlineGame.Mtgo && e.ScryfallId == sid).ToList(),
                "ArenaCollection" => db.OnlineCollectionEntries
                    .Where(e => e.Game == OnlineGame.Arena && e.ScryfallId == sid).ToList(),
                BinderTable => db.TradeBinderEntries.Where(e => e.ScryfallId == sid).ToList(),
                WantTable => db.WantListEntries.Where(e => e.ScryfallId == sid).ToList(),
                _ => db.CollectionEntries.Where(e => e.ScryfallId == sid).ToList(),
            };
            return rows.Select(r => new Row(r)).ToList();
        }

        /// <summary>
        /// The rows of many printings at once (in batches of 400 — one query
        /// per printing would crawl on an import of thousands).
        /// </summary>
        private static List<Row> RowsMany(CollectionDbContext db, string collTag, IEnumerable<string> sids)
        {
            var result = new List<Row>();
            var all = sids.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            for (int i = 0; i < all.Count; i += 400)
            {
                var chunk = all.Skip(i).Take(400).ToList();
                IEnumerable<object> rows = collTag switch
                {
                    "CollTokens" => db.TokenCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "CollPlanes" => db.PlanarCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "CollSchemes" => db.SchemeCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "CollVanguards" => db.VanguardCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "CollArtSeries" => db.ArtSeriesCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "CollConspiracies" => db.ConspiracyCollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    "MtgoCollection" => db.OnlineCollectionEntries
                        .Where(e => e.Game == OnlineGame.Mtgo && chunk.Contains(e.ScryfallId)).ToList(),
                    "ArenaCollection" => db.OnlineCollectionEntries
                        .Where(e => e.Game == OnlineGame.Arena && chunk.Contains(e.ScryfallId)).ToList(),
                    BinderTable => db.TradeBinderEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    WantTable => db.WantListEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                    _ => db.CollectionEntries.Where(e => chunk.Contains(e.ScryfallId)).ToList(),
                };
                result.AddRange(rows.Select(r => new Row(r)));
            }
            return result;
        }

        /// <summary>Every printing (Scryfall id) with a row in a table.</summary>
        private static List<string> AllSids(CollectionDbContext db, string collTag) => collTag switch
        {
            "CollTokens" => db.TokenCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "CollPlanes" => db.PlanarCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "CollSchemes" => db.SchemeCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "CollVanguards" => db.VanguardCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "CollArtSeries" => db.ArtSeriesCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "CollConspiracies" => db.ConspiracyCollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            "MtgoCollection" => db.OnlineCollectionEntries.Where(e => e.Game == OnlineGame.Mtgo).Select(e => e.ScryfallId).Distinct().ToList(),
            "ArenaCollection" => db.OnlineCollectionEntries.Where(e => e.Game == OnlineGame.Arena).Select(e => e.ScryfallId).Distinct().ToList(),
            BinderTable => db.TradeBinderEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            WantTable => db.WantListEntries.Select(e => e.ScryfallId).Distinct().ToList(),
            _ => db.CollectionEntries.Select(e => e.ScryfallId).Distinct().ToList(),
        };

        private static Row? FindById(CollectionDbContext db, string collTag, int id) =>
            id <= 0 ? null : db.Find(EntryType(collTag), id) is { } e ? new Row(e) : null;

        private static string ShownFinish(Row r, bool etchedOnly) => CardFinish.Shown(r.Finish, etchedOnly);

        private static RowKey KeyOf(Row r, bool etchedOnly) =>
            RowKey.Of(r.ScryfallId, ShownFinish(r, etchedOnly), r.Language, r.Condition);

        /// <summary>The row with exactly this finish, language and condition (v1 foil = etched for etched-only printings).</summary>
        private static Row? FindExact(IEnumerable<Row> rows, RowKey key, bool etchedOnly) =>
            rows.FirstOrDefault(r => KeyOf(r, etchedOnly) == key);

        /// <summary>Is this printing etched-only (no foil version)? Cached per call.</summary>
        private static bool EtchedOnlyPrinting(string collTag, string sid, Dictionary<string, bool> cache)
        {
            if (cache.TryGetValue(sid, out bool v)) return v;
            if (IsOnline(collTag)) return cache[sid] = false;    // online rows are stored as they are
            var pool = FindPoolCard(collTag, sid);
            v = pool != null && GetBool(pool, "IsEtched") && !GetBool(pool, "IsFoil");
            cache[sid] = v;
            return v;
        }

        private static Type EntryType(string collTag) => collTag switch
        {
            "CollTokens" => typeof(TokenCollectionEntry),
            "CollPlanes" => typeof(PlanarCollectionEntry),
            "CollSchemes" => typeof(SchemeCollectionEntry),
            "CollVanguards" => typeof(VanguardCollectionEntry),
            "CollArtSeries" => typeof(ArtSeriesCollectionEntry),
            "CollConspiracies" => typeof(ConspiracyCollectionEntry),
            "MtgoCollection" or "ArenaCollection" => typeof(OnlineCollectionEntry),
            BinderTable => typeof(TradeBinderEntry),
            WantTable => typeof(WantListEntry),
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
                "Quantity", "FoilQuantity", "Finish", "Price", "UsedCount", "Condition", "Language",
                "Notes", "DateAdded", "DateModified", "RowIndex", "IsFoil", "IsNonFoil", "IsEtched",
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
            var (nf, f, e) = FinishesOf(collTag, poolCard);
            type.GetProperty("IsFoilAvailable")?.SetValue(entity, f || e);
            type.GetProperty("IsNonFoilAvailable")?.SetValue(entity, nf);
            if (OnlineGameOf(collTag) is { } game)
                type.GetProperty("Game")?.SetValue(entity, game);        // MTGO or Arena row
            return new Row(entity);
        }

        /// <summary>A copy of a row's stored fields (not its id, and no copies in use).</summary>
        private static Row CloneRow(CollectionDbContext db, string collTag, Row source)
        {
            var type = EntryType(collTag);
            var entity = Activator.CreateInstance(type)!;
            var key = db.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties[0].Name;
            foreach (var p in db.Entry(source.Entity).Properties)
            {
                string n = p.Metadata.Name;
                if (n == key || n == "UsedCount") continue;
                type.GetProperty(n)?.SetValue(entity, p.CurrentValue);
            }
            return new Row(entity);                 // the caller Adds it
        }

        // ── Helpers ─────────────────────────────────────────────────────
        private static EditResult Fail(string message, RowKey? key = null)
        {
            var r = new EditResult { Message = message, Warning = true };
            if (key != null) r.Touched.Add(key);
            return r;
        }

        /// <summary>
        /// This finish's price: paper in USD (the one price rule); MTGO in
        /// event tickets (Scryfall has one tix price, the regular version);
        /// Arena has no prices.
        /// </summary>
        private static decimal? PriceOf(string collTag, object card, string finish) => OnlineGameOf(collTag) switch
        {
            OnlineGame.Arena => null,
            OnlineGame.Mtgo => finish == CardFinish.NonFoil
                ? card.GetType().GetProperty("PriceTix")?.GetValue(card) as decimal?
                : null,
            _ => PaperPriceOf(card, finish),
        };

        private static decimal? PaperPriceOf(object card, string finish) =>
            CardFinish.PriceFor(finish,
                card.GetType().GetProperty("PriceUsd")?.GetValue(card) as decimal?,
                card.GetType().GetProperty("PriceUsdFoil")?.GetValue(card) as decimal?,
                card.GetType().GetProperty("PriceUsdEtched")?.GetValue(card) as decimal?);

        private static string Name(object card) => GetString(card, "Name");

        private static string Label(object card, RowKey key, string collTag)
        {
            string set = GetString(card, "SetCode").ToUpperInvariant();
            string no = GetString(card, "CollectorNumber");
            string what = IsOnline(collTag) ? CardFinish.Display(key.Finish) : key.Text;   // online: no language/condition
            return $"{GetString(card, "Name")} ({set} #{no}, {what})";
        }

        /// <summary>"Deck A, Deck B" — who uses this printing (main collection only).</summary>
        private static string UsedBy(CollectionDbContext db, string collTag, string sid)
        {
            if (collTag != CardsTable) return "in use";
            try
            {
                var names = db.DeckUsages.Where(u => u.ScryfallId == sid && (u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched) > 0)
                    .Select(u => u.DeckName).Distinct().ToList();
                return names.Count == 0 ? "used by decks" : "used by " + string.Join(", ", names);
            }
            catch { return "used by decks"; }
        }

        /// <summary>"Foil · English · Near Mint" — online rows just "Foil" (no language or condition).</summary>
        private static string TextFor(string collTag, RowKey key) =>
            IsOnline(collTag) ? CardFinish.Display(key.Finish) : key.Text;

        private static int? GetInt(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) as int?;

        private static bool GetBool(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) is bool b && b;

        private static string GetString(object o, string prop) =>
            o.GetType().GetProperty(prop)?.GetValue(o) as string ?? "";
    }
}