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
    /// Edit → Lists → Pool → Want List (cards). A want row is one printing in
    /// one finish (no language or condition — you haven't got it yet), with
    /// the market price of that finish, your offer and notes.
    ///
    ///  • Add / Remove — copies on the want list.
    ///  • Got It — copies leave the want list and go into your collection
    ///    (with the language and condition you pick).
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>The Want List's table tag (as the View → Want List table).</summary>
        public const string WantTable = "WantList";

        /// <summary>A want row's key: printing + finish (language/condition are the defaults).</summary>
        public static RowKey WantKey(string sid, string finish) => RowKey.Of(sid, finish, null, null);

        private static WantListEntry? FindWantEntry(CollectionDbContext db, string sid, string finish, bool etchedOnly) =>
            FindWantEntries(db, sid, finish, etchedOnly).FirstOrDefault();

        /// <summary>Every want row of this printing that shows as this finish (usually one; a v1 foil row
        /// and a newer etched row of an etched-only printing are two).</summary>
        private static List<WantListEntry> FindWantEntries(CollectionDbContext db, string sid, string finish, bool etchedOnly) =>
            db.WantListEntries.Where(e => e.ScryfallId == sid).ToList()
              .Where(e => CardFinish.Shown(e.Finish, etchedOnly) == finish).ToList();

        /// <summary>Take up to <paramref name="n"/> copies off these want rows, in turn (rows at 0 are removed). Returns copies taken.</summary>
        private static int TakeWanted(CollectionDbContext db, List<WantListEntry> rows, string finish, int n)
        {
            int taken = 0;
            foreach (var e in rows)
            {
                if (taken >= n) break;
                int t = Math.Min(n - taken, Math.Max(0, e.Quantity));
                if (t <= 0) continue;
                e.Finish = finish;
                e.Quantity -= t;
                if (e.Quantity <= 0) db.WantListEntries.Remove(e);
                taken += t;
            }
            return taken;
        }

        /// <summary>A new want row carrying the pool card's data (every field the two share).</summary>
        private static WantListEntry NewWantEntry(object poolCard)
        {
            var entry = new WantListEntry();
            var skip = new HashSet<string>(StringComparer.Ordinal)
            {
                "Quantity", "Finish", "Price", "IsFoil", "OfferPrice", "Notes", "DateAdded",
            };
            var src = poolCard.GetType();
            foreach (var to in typeof(WantListEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!to.CanWrite || skip.Contains(to.Name)) continue;
                if (to.GetCustomAttribute<KeyAttribute>() != null || to.GetCustomAttribute<NotMappedAttribute>() != null) continue;
                var p = src.GetProperty(to.Name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanRead || p.PropertyType != to.PropertyType) continue;
                to.SetValue(entry, p.GetValue(poolCard));
            }
            var (nf, f, e) = FinishesOf(CardsTable, poolCard);
            entry.IsFoilAvailable = f || e;
            entry.IsNonFoilAvailable = nf;
            entry.Quantity = 0;
            entry.Notes = "";
            entry.DateAdded = DateTime.Now;
            return entry;
        }

        /// <summary>Copies of a printing on the want list, per finish (finish → copies).</summary>
        public static Dictionary<string, int> WantedCopies(string scryfallId, bool etchedOnly)
        {
            var map = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(scryfallId)) return map;
            try
            {
                using var db = new CollectionDbContext();
                foreach (var e in db.WantListEntries.Where(e => e.ScryfallId == scryfallId).ToList())
                {
                    string f = CardFinish.Shown(e.Finish, etchedOnly);
                    map[f] = map.GetValueOrDefault(f) + Math.Max(0, e.Quantity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wanted copies: {ex.Message}");
            }
            return map;
        }

        // ══════════════════════════════════════════════════════════════════
        // ADD / REMOVE
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Put <paramref name="qty"/> copies of a pool printing in one finish on the want list.</summary>
        public static EditResult AddToWantList(object poolCard, string finish, int qty)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            string sid = GetString(poolCard, "ScryfallId");
            finish = CardFinish.Normalize(finish);
            var key = WantKey(sid, finish);
            string label = $"{Name(poolCard)} ({GetString(poolCard, "SetCode").ToUpperInvariant()} #{GetString(poolCard, "CollectorNumber")}, {CardFinish.Display(finish)})";

            var (nf, f, e) = FinishesOf(CardsTable, FindPoolCard(CardsTable, sid) ?? poolCard);   // the card database decides
            bool exists = FinishExists(finish, nf, f, e);
            if (!exists) return Fail($"{Name(poolCard)}: this printing doesn't exist in {CardFinish.Display(finish)}.");
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var entry = FindWantEntry(db, sid, finish, e && !f);
                if (entry == null)
                {
                    entry = NewWantEntry(poolCard);
                    db.WantListEntries.Add(entry);
                }
                entry.Finish = finish;
                entry.IsFoil = finish != CardFinish.NonFoil;
                entry.Quantity += qty;
                entry.Price = PaperPriceOf(poolCard, finish);       // today's market price
                db.SaveChanges();
                return new EditResult
                {
                    Changed = qty,
                    Message = $"Added {qty} × {label} to the Want List (now {entry.Quantity}).",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not add {label} to the Want List: {ex.Message}");
            }
        }

        /// <summary>Take up to <paramref name="qty"/> copies (int.MaxValue = all) of one finish off the want list.</summary>
        public static EditResult RemoveFromWantList(string sid, string finish, int qty, string name)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            finish = CardFinish.Normalize(finish);
            var key = WantKey(sid, finish);
            string label = $"{name} ({CardFinish.Display(finish)})";
            try
            {
                using var db = new CollectionDbContext();
                bool eo = EtchedOnlyPrinting(CardsTable, sid, new Dictionary<string, bool>());
                var entries = FindWantEntries(db, sid, finish, eo);
                int have = entries.Sum(e => Math.Max(0, e.Quantity));
                if (have <= 0) return Fail($"{label} isn't on the Want List.", key);
                EnsureBackup();
                int take = TakeWanted(db, entries, finish, qty);
                int left = have - take;
                db.SaveChanges();
                bool partial = qty != int.MaxValue && take < qty;
                return new EditResult
                {
                    Changed = take,
                    Warning = partial,
                    Message = $"Took {take} × {label} off the Want List" +
                              (left > 0 ? $" ({left} still wanted)." : ".") +
                              (partial ? $" Only {take} were on it." : ""),
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not change the Want List: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // GOT IT (want list → collection)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Got up to <paramref name="qty"/> copies of a wanted finish: they go
        /// into your card collection (this language and condition) and come
        /// off the want list.
        /// </summary>
        public static EditResult GotIt(string sid, string finish, int qty, string name, string? language, string? condition)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            finish = CardFinish.Normalize(finish);
            var key = WantKey(sid, finish);
            string label = $"{name} ({CardFinish.Display(finish)})";
            try
            {
                int wanted;
                using (var db = new CollectionDbContext())
                {
                    bool eo = EtchedOnlyPrinting(CardsTable, sid, new Dictionary<string, bool>());
                    wanted = FindWantEntries(db, sid, finish, eo).Sum(e => Math.Max(0, e.Quantity));
                    if (wanted <= 0) return Fail($"{label} isn't on the Want List.", key);
                }
                var pool = FindPoolCard(CardsTable, sid);
                if (pool == null) return Fail($"{label}: not in the card pool, so it can't go into the collection.", key);

                int take = Math.Min(qty, wanted);
                var added = Add(CardsTable, pool, finish, take, language, condition);
                if (added.Changed <= 0) return added;

                // In the collection now: off the want list. The copies are already
                // in the collection, so a failure here still reports them (and
                // the page keeps its Undo for the whole step).
                var collKey = KeyFor(CardsTable, sid, finish, language, condition);
                int left;
                try
                {
                    using var db = new CollectionDbContext();
                    bool eo = EtchedOnlyPrinting(CardsTable, sid, new Dictionary<string, bool>());
                    var entries = FindWantEntries(db, sid, finish, eo);
                    int have = entries.Sum(e => Math.Max(0, e.Quantity));
                    left = have - TakeWanted(db, entries, finish, added.Changed);
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    var failed = new EditResult
                    {
                        Changed = added.Changed,
                        Warning = true,
                        Message = $"Added {added.Changed} × {name} ({collKey.Text}) to your collection, but could not update the Want List: {ex.Message}",
                    };
                    failed.Touched.Add(key);
                    failed.Touched.Add(collKey);
                    return failed;
                }
                {
                    bool partial = qty != int.MaxValue && take < qty;
                    var result = new EditResult
                    {
                        Changed = added.Changed,
                        Warning = partial,
                        Message = $"Got it: {added.Changed} × {name} ({collKey.Text}) into your collection" +
                                  (left > 0 ? $" — {left} still wanted." : " — off the Want List.") +
                                  (partial ? $" Only {take} were wanted." : ""),
                    };
                    result.Touched.Add(key);
                    result.Touched.Add(collKey);
                    return result;
                }
            }
            catch (Exception ex)
            {
                return Fail($"Could not move {label} into the collection: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // OFFER PRICE AND NOTES (the want row itself)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Set a want row's offer per copy (null = none).</summary>
        public static EditResult SetOfferPrice(int entryId, decimal? price)
        {
            if (price < 0) return Fail("The offer can't be negative.");
            if (price.HasValue) price = Math.Round(price.Value, 2);
            try
            {
                using var db = new CollectionDbContext();
                var e = db.WantListEntries.Find(entryId);
                if (e == null) return Fail("That row is no longer on the Want List.");
                string finish = CardFinish.Shown(e.Finish, EtchedOnlyPrinting(CardsTable, e.ScryfallId, new Dictionary<string, bool>()));
                var key = WantKey(e.ScryfallId, finish);
                string label = $"{e.Name} ({CardFinish.Display(finish)})";
                if (e.OfferPrice == price) return new EditResult { Message = $"{label}: offer unchanged.", Touched = { key } };
                EnsureBackup();
                e.OfferPrice = price;
                db.SaveChanges();
                string over = price.HasValue && e.Price.HasValue && price.Value > e.Price.Value
                    ? $" That's above the market price (${e.Price.Value:F2})." : "";
                return new EditResult
                {
                    Changed = 1,
                    Warning = over.Length > 0,
                    Message = price.HasValue ? $"{label}: offer ${price.Value:F2} each.{over}"
                                             : $"{label}: offer cleared.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not save the offer: {ex.Message}");
            }
        }

        /// <summary>Set a want row's notes.</summary>
        public static EditResult SetWantNotes(int entryId, string notes)
        {
            notes = (notes ?? "").Trim();
            try
            {
                using var db = new CollectionDbContext();
                var e = db.WantListEntries.Find(entryId);
                if (e == null) return Fail("That row is no longer on the Want List.");
                string finish = CardFinish.Shown(e.Finish, EtchedOnlyPrinting(CardsTable, e.ScryfallId, new Dictionary<string, bool>()));
                var key = WantKey(e.ScryfallId, finish);
                string label = $"{e.Name} ({CardFinish.Display(finish)})";
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
        // UNDO across tables (Got It changes the want list AND the collection)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Undo several snapshots (e.g. the want list and the collection) as
        /// one change: all or nothing, refused when any of them changed since.
        /// </summary>
        public static EditResult RestoreAll(IReadOnlyList<EditSnapshot> snaps, string description)
        {
            try
            {
                if (snaps.Any(s => !CanRestore(s)))
                    return Fail($"Can't undo \"{description}\": those cards have changed since.");
                EnsureBackup();
                using var db = new CollectionDbContext();
                var touched = new List<RowKey>();
                foreach (var s in snaps) touched.AddRange(RestoreInto(db, s));
                db.SaveChanges();                       // one save: all tables together
                var result = new EditResult { Changed = 1, Message = $"Undone: {description}" };
                result.Touched.AddRange(touched);
                return result;
            }
            catch (Exception ex)
            {
                return Fail($"Could not undo: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // FIND MISSING (Edit → Decks → Deck → Collection)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Free copies you own of a card NAME in other printings (not
        /// <paramref name="excludeSid"/>): each collection row with copies no
        /// deck or the Trade Binder has claimed.
        /// </summary>
        public static List<OwnedPrinting> OtherPrintingsOwned(string name, string excludeSid)
        {
            var list = new List<OwnedPrinting>();
            if (string.IsNullOrEmpty(name)) return list;
            try
            {
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                var rows = db.CollectionEntries.AsNoTracking()
                    .Where(e => e.Name == name && e.ScryfallId != excludeSid && e.Quantity > e.UsedCount)
                    .ToList();
                foreach (var e in rows)
                {
                    var key = KeyOf(new Row(e), EtchedOnlyPrinting(CardsTable, e.ScryfallId, cache));
                    list.Add(new OwnedPrinting
                    {
                        Key = key,
                        Name = e.Name,
                        SetCode = e.SetCode,
                        CollectorNumber = e.CollectorNumber,
                        Free = Math.Max(0, e.Quantity - Math.Max(0, e.UsedCount)),
                        Price = e.Price,
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Other printings: {ex.Message}");
            }
            return list.OrderBy(o => o.SetCode, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(o => o.CollectorNumber, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(o => o.Key.Finish).ToList();
        }

        /// <summary>
        /// Missing → Want List: raise the want list to <paramref name="need"/>
        /// copies of this printing + finish — never above it, never doubled
        /// (pressing it again adds nothing). The note says which deck it's for.
        /// </summary>
        public static EditResult TopUpWantList(object poolCard, string finish, int need, string deckName)
        {
            finish = CardFinish.Normalize(finish);
            string sid = GetString(poolCard, "ScryfallId");
            var key = WantKey(sid, finish);
            string label = $"{Name(poolCard)} ({GetString(poolCard, "SetCode").ToUpperInvariant()} #{GetString(poolCard, "CollectorNumber")}, {CardFinish.Display(finish)})";
            if (need <= 0) return new EditResult { Message = $"{label}: nothing missing.", Touched = { key } };
            var (nf, f, e) = FinishesOf(CardsTable, FindPoolCard(CardsTable, sid) ?? poolCard);   // the card database decides
            bool exists = FinishExists(finish, nf, f, e);
            if (!exists) return Fail($"{Name(poolCard)}: this printing doesn't exist in {CardFinish.Display(finish)}.", key);
            try
            {
                using var db = new CollectionDbContext();
                var entries = FindWantEntries(db, sid, finish, e && !f);
                int have = entries.Sum(x => Math.Max(0, x.Quantity));
                if (have >= need)
                    return new EditResult { Message = $"{label}: already on the Want List ({have}).", Touched = { key } };
                EnsureBackup();
                var entry = entries.FirstOrDefault();
                if (entry == null)
                {
                    entry = NewWantEntry(poolCard);
                    db.WantListEntries.Add(entry);
                }
                entry.Finish = finish;
                entry.IsFoil = finish != CardFinish.NonFoil;
                entry.Quantity += need - have;
                entry.Price = PaperPriceOf(poolCard, finish);
                string forDeck = $"For: {deckName}";
                if (!(entry.Notes ?? "").Contains(forDeck, StringComparison.OrdinalIgnoreCase))
                    entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? forDeck : $"{entry.Notes}; {forDeck}";
                db.SaveChanges();
                return new EditResult
                {
                    Changed = need - have,
                    Message = $"Want List: {label} {have} → {need}.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not add {label} to the Want List: {ex.Message}", key);
            }
        }
    }

    /// <summary>A collection row of another printing of a card (Find Missing).</summary>
    public sealed class OwnedPrinting
    {
        /// <summary>Printing, finish, language, condition.</summary>
        public RowKey Key { get; init; } = RowKey.Of("", null, null, null);
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string CollectorNumber { get; init; } = "";
        /// <summary>Copies no deck or the Trade Binder has claimed.</summary>
        public int Free { get; init; }
        /// <summary>Market price of that finish (per copy).</summary>
        public decimal? Price { get; init; }
        /// <summary>"C21 #263".</summary>
        public string PrintingText => $"{SetCode.ToUpperInvariant()} #{CollectorNumber}";
    }
}