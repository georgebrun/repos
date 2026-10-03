using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Edit → Collection → Trade Binder (cards only).
    ///
    /// Binder copies come from one exact collection row (printing, finish,
    /// language, condition) and are CLAIMED by the binder the way a deck
    /// claims copies: a usage row with DeckId "__TRADE_BINDER__" (v1's
    /// pseudo-deck, so v1 binder claims carry over). They count as Used, so no
    /// deck can take them, and the "Used in" table shows "Trade Binder".
    ///
    /// The binder's own row (TradeBinderEntries) carries the asking price and
    /// notes; its quantity and the binder's claim move together, in one save.
    ///
    ///  • Add — free copies of the collection row go into the binder.
    ///  • Remove — out of the binder, back to Available in the collection.
    ///  • Traded/Sold — out of the binder AND the collection.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>The binder's claims in DeckUsages (v1's pseudo-deck id).</summary>
        public const string TradeBinderId = "__TRADE_BINDER__";
        public const string TradeBinderName = "Trade Binder";
        /// <summary>The binder's table tag (as the View → Trade Binder table).</summary>
        public const string BinderTable = "TradeBinder";

        /// <summary>A binder row's key (v1 rows: no language = English; v1 foil of an etched-only printing = etched).</summary>
        private static RowKey BinderKey(TradeBinderEntry e, bool etchedOnly) =>
            RowKey.Of(e.ScryfallId, CardFinish.Shown(e.Finish, etchedOnly), e.Language, e.Condition);

        private static TradeBinderEntry? FindBinderEntry(CollectionDbContext db, RowKey key, bool etchedOnly) =>
            db.TradeBinderEntries.Where(e => e.ScryfallId == key.ScryfallId).ToList()
              .FirstOrDefault(e => BinderKey(e, etchedOnly) == key);

        private static void WriteBinderKey(TradeBinderEntry e, RowKey key)
        {
            e.Finish = key.Finish;
            e.IsFoil = key.Finish != CardFinish.NonFoil;         // v1's field, kept in step
            e.Language = key.Language;
            e.Condition = key.Condition;
        }

        /// <summary>A new binder row carrying the collection row's card data (every field the two share).</summary>
        private static TradeBinderEntry NewBinderEntry(Row from)
        {
            var entry = new TradeBinderEntry();
            var skip = new HashSet<string>(StringComparer.Ordinal)
            {
                "Quantity", "Notes", "DateAdded", "AskingPrice", "Finish", "Language", "Condition", "IsFoil", "Price",
            };
            var src = from.Entity.GetType();
            foreach (var to in typeof(TradeBinderEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!to.CanWrite || skip.Contains(to.Name)) continue;
                if (to.GetCustomAttribute<KeyAttribute>() != null || to.GetCustomAttribute<NotMappedAttribute>() != null) continue;
                var p = src.GetProperty(to.Name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanRead || p.PropertyType != to.PropertyType) continue;
                to.SetValue(entry, p.GetValue(from.Entity));
            }
            entry.Quantity = 0;
            entry.Notes = "";
            entry.DateAdded = DateTime.Now;
            return entry;
        }

        /// <summary>
        /// The collection rows with this key (usually one; "NM" next to "Near
        /// Mint" are two rows of one key), leaving out rows removed in this context.
        /// </summary>
        private static List<Row> RowsWithKey(CollectionDbContext db, RowKey key, bool etchedOnly) =>
            Rows(db, CardsTable, key.ScryfallId)
                .Where(r => KeyOf(r, etchedOnly) == key && db.Entry(r.Entity).State != EntityState.Deleted)
                .ToList();

        // ══════════════════════════════════════════════════════════════════
        // ADD
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Put up to <paramref name="qty"/> free copies of exactly this
        /// collection row in the Trade Binder (they become Used).
        /// </summary>
        public static EditResult AddToBinder(RowKey key, int qty, string name)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            string sid = key.ScryfallId;
            string label = $"{name} ({key.Text})";
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                Recompute(db, CardsTable, new[] { sid }, cache);      // Used up to date (v1 claims placed)
                db.SaveChanges();
                bool eo = EtchedOnlyPrinting(CardsTable, sid, cache);

                var rows = RowsWithKey(db, key, eo);
                int owned = rows.Sum(r => Math.Max(0, r.Quantity));
                if (owned <= 0)
                    return Fail($"{label} isn't in your collection.", key);
                var row = rows.First(r => r.Quantity > 0);
                int free = Math.Max(0, owned - rows.Sum(r => Math.Max(0, r.UsedCount)));
                int take = Math.Min(qty, free);
                if (take <= 0)
                    return Fail($"All {owned} {label} are in use ({UsedBy(db, CardsTable, sid)}) — none free for the Trade Binder.", key);

                // The binder's claim on the collection row …
                var claim = GetOrAddClaim(db, TradeBinderId, CardsTable, key);
                Tidy(claim, eo);
                SetClaim(claim, key.Finish, ClaimOf(claim, key.Finish, false) + take);
                claim.DeckName = TradeBinderName;
                claim.DeckType = TradeBinderName;
                claim.DateRecorded = DateTime.Now;

                // … and its binder row, saved together.
                var entry = FindBinderEntry(db, key, eo);
                if (entry == null)
                {
                    entry = NewBinderEntry(row);
                    db.TradeBinderEntries.Add(entry);
                }
                WriteBinderKey(entry, key);
                entry.Quantity += take;
                entry.Price = row.Price;                    // today's market price of this finish
                db.SaveChanges();
                Recompute(db, CardsTable, new[] { sid }, cache);
                db.SaveChanges();

                return new EditResult
                {
                    Changed = take,
                    Warning = take < qty,
                    Message = take < qty
                        ? $"Put {take} of {qty} × {label} in the Trade Binder — only {take} free (binder now {entry.Quantity})."
                        : $"Put {take} × {label} in the Trade Binder (binder now {entry.Quantity}).",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not put {name} in the Trade Binder: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // REMOVE / TRADED-SOLD
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Take up to <paramref name="qty"/> copies (int.MaxValue = all) of a
        /// binder row out of the binder. <paramref name="traded"/> = they're
        /// gone (traded or sold): removed from the collection too; otherwise
        /// they go back to Available in the collection.
        /// </summary>
        public static EditResult RemoveFromBinder(RowKey key, int qty, string name, bool traded)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            string sid = key.ScryfallId;
            string label = $"{name} ({key.Text})";
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                Recompute(db, CardsTable, new[] { sid }, cache);
                db.SaveChanges();
                bool eo = EtchedOnlyPrinting(CardsTable, sid, cache);

                var entry = FindBinderEntry(db, key, eo);
                if (entry == null || entry.Quantity <= 0)
                    return Fail($"{label} isn't in the Trade Binder.", key);
                int take = Math.Min(qty, entry.Quantity);

                // Free the binder's claims: on this exact collection row first,
                // then on any other row of the printing + finish (where a v1
                // binder claim was placed).
                var claims = db.DeckUsages.Where(u => u.DeckId == TradeBinderId && u.ScryfallId == sid).ToList()
                    .Where(u => !IsV1(u) && TableOf(u) == CardsTable)
                    .OrderBy(u => Matches(u, CardsTable, key) ? 0 : 1)
                    .ToList();
                // A claim on another row is freed only where no binder row of
                // its own backs it (a v1 claim placed there), never another
                // binder row's copies.
                var backed = db.TradeBinderEntries.Where(e => e.ScryfallId == sid).ToList()
                    .GroupBy(e => BinderKey(e, eo)).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity));
                var released = new List<(RowKey Key, int N)>();
                int left = take;
                foreach (var u in claims)
                {
                    if (left <= 0) break;
                    Tidy(u, eo);
                    var claimKey = RowKey.Of(sid, key.Finish, u.Language, u.Condition);
                    int have = ClaimOf(u, key.Finish, false);
                    if (claimKey != key) have = Math.Max(0, have - backed.GetValueOrDefault(claimKey));
                    int n = Math.Min(left, have);
                    if (n <= 0) continue;
                    SetClaim(u, key.Finish, ClaimOf(u, key.Finish, false) - n);
                    released.Add((RowKey.Of(sid, key.Finish, u.Language, u.Condition), n));
                    left -= n;
                }
                foreach (var u in claims.Where(u => u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched <= 0))
                    db.DeckUsages.Remove(u);

                WriteBinderKey(entry, key);
                entry.Quantity -= take;
                int stays = entry.Quantity;
                if (entry.Quantity <= 0) db.TradeBinderEntries.Remove(entry);
                db.SaveChanges();
                Recompute(db, CardsTable, new[] { sid }, cache);
                db.SaveChanges();

                int notOwned = 0;
                if (traded)
                {
                    // Gone: out of the collection rows they were claimed from
                    // (copies the binder had no claim for: its own row).
                    var plan = released.ToList();
                    int unclaimed = take - released.Sum(r => r.N);
                    if (unclaimed > 0) plan.Add((key, unclaimed));
                    foreach (var (k, n) in plan)
                    {
                        int need = n;
                        foreach (var row in RowsWithKey(db, k, eo))
                        {
                            if (need <= 0) break;
                            int gone = Math.Min(need, Math.Max(0, row.Quantity - Math.Max(0, row.UsedCount)));
                            if (gone <= 0) continue;
                            row.WriteKey(k);
                            row.Quantity -= gone;
                            row.DateModified = DateTime.Now;
                            if (row.Quantity <= 0) db.Remove(row.Entity);
                            need -= gone;
                        }
                        notOwned += need;
                    }
                    db.SaveChanges();
                    Recompute(db, CardsTable, new[] { sid }, cache);
                    db.SaveChanges();
                }

                bool partial = qty != int.MaxValue && take < qty;
                string inBinder = stays > 0 ? $" ({stays} still in the binder)" : "";
                string msg = traded
                    ? $"Traded/Sold {take} × {label} — gone from the binder and your collection{inBinder}."
                    : $"Took {take} × {label} out of the Trade Binder — back to Available in your collection{inBinder}.";
                if (partial) msg += $" Only {take} were in the binder.";
                if (notOwned > 0) msg += $" {notOwned} weren't in your collection, so only the binder changed for those.";
                return new EditResult { Changed = take, Warning = partial || notOwned > 0, Message = msg, Touched = { key } };
            }
            catch (Exception ex)
            {
                return Fail($"Could not change the Trade Binder: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ASKING PRICE AND NOTES (the binder row itself)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Set a binder row's asking price per copy (null = none: the market price applies).</summary>
        public static EditResult SetAskingPrice(int entryId, decimal? price)
        {
            if (price < 0) return Fail("The asking price can't be negative.");
            if (price.HasValue) price = Math.Round(price.Value, 2);
            try
            {
                using var db = new CollectionDbContext();
                var e = db.TradeBinderEntries.Find(entryId);
                if (e == null) return Fail("That row is no longer in the Trade Binder.");
                var key = BinderKey(e, EtchedOnlyPrinting(CardsTable, e.ScryfallId, new Dictionary<string, bool>()));
                string label = $"{e.Name} ({key.Text})";
                if (e.AskingPrice == price) return new EditResult { Message = $"{label}: asking price unchanged.", Touched = { key } };
                EnsureBackup();
                e.AskingPrice = price;
                db.SaveChanges();
                return new EditResult
                {
                    Changed = 1,
                    Message = price.HasValue ? $"{label}: asking ${price.Value:F2} each."
                                             : $"{label}: asking price cleared (the market price applies).",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not save the asking price: {ex.Message}");
            }
        }

        /// <summary>Set a binder row's notes.</summary>
        public static EditResult SetBinderNotes(int entryId, string notes)
        {
            notes = (notes ?? "").Trim();
            try
            {
                using var db = new CollectionDbContext();
                var e = db.TradeBinderEntries.Find(entryId);
                if (e == null) return Fail("That row is no longer in the Trade Binder.");
                var key = BinderKey(e, EtchedOnlyPrinting(CardsTable, e.ScryfallId, new Dictionary<string, bool>()));
                string label = $"{e.Name} ({key.Text})";
                if ((e.Notes ?? "") == notes) return new EditResult { Message = $"{label}: notes unchanged.", Touched = { key } };
                EnsureBackup();
                e.Notes = notes;
                db.SaveChanges();
                return new EditResult
                {
                    Changed = 1,
                    Message = notes.Length == 0 ? $"{label}: notes cleared." : $"{label}: notes saved.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not save the notes: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // UNDO
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Before a binder edit: the binder's claims, collection rows and
        /// binder rows of these printings only (null when it can't be taken).
        /// Undo with <see cref="MarkClaimsAfter"/> / <see cref="RestoreClaims"/>.
        /// </summary>
        public static ClaimSnapshot? TakeBinderSnapshot(IEnumerable<string> scryfallIds)
        {
            var sids = new HashSet<string>(scryfallIds.Where(s => !string.IsNullOrEmpty(s)), StringComparer.Ordinal);
            if (sids.Count == 0) return null;
            try
            {
                using var db = new CollectionDbContext();
                var snap = new ClaimSnapshot { DeckId = TradeBinderId, Sids = sids, Claims = ClaimsOf(db, TradeBinderId, sids) };
                if (Snapshot(CardsTable, sids) is not { } cards || Snapshot(BinderTable, sids) is not { } binder) return null;
                snap.Rows.Add(cards);
                snap.Rows.Add(binder);
                return snap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Binder snapshot: {ex.Message}");
                return null;
            }
        }
    }
}
