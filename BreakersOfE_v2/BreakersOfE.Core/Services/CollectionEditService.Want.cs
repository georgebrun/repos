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
    /// one finish (no language or condition — you haven't got it yet) for one
    /// purpose: a deck (ForDeckId) or the Collection (""), with the market
    /// price of that finish, your offer and notes. Several decks can want the
    /// same card: each has its own row.
    ///
    ///  • Add / Remove — copies on the want list (Remove deletes the want only).
    ///  • Got It — copies go into your collection (with the language and
    ///    condition you pick), are claimed for the decks they were for, and
    ///    come off the want list; extra copies stay free in the collection.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>The Want List's table tag (as the View → Want List table).</summary>
        public const string WantTable = "WantList";

        /// <summary>A want row's key: printing + finish (language/condition are the defaults).</summary>
        public static RowKey WantKey(string sid, string finish) => RowKey.Of(sid, finish, null, null);

        private static WantListEntry? FindWantEntry(CollectionDbContext db, string sid, string finish, bool etchedOnly, string? forDeckId) =>
            FindWantEntries(db, sid, finish, etchedOnly, forDeckId).FirstOrDefault();

        /// <summary>Every want row of this printing that shows as this finish (usually one; a v1 foil row
        /// and a newer etched row of an etched-only printing are two), for this deck / the Collection
        /// ("" ) — or for anyone when <paramref name="forDeckId"/> is null.</summary>
        private static List<WantListEntry> FindWantEntries(CollectionDbContext db, string sid, string finish, bool etchedOnly,
                                                           string? forDeckId = null) =>
            db.WantListEntries.Where(e => e.ScryfallId == sid).ToList()
              .Where(e => CardFinish.Shown(e.Finish, etchedOnly) == finish &&
                          (forDeckId == null || (e.ForDeckId ?? "") == forDeckId)).ToList();

        /// <summary>"for Jenson" / "for the Collection".</summary>
        private static string ForText(string forDeckId, string forDeckName) =>
            string.IsNullOrEmpty(forDeckId) ? "for the Collection" : $"for {forDeckName}";

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
                "Quantity", "Finish", "Price", "IsFoil", "OfferPrice", "Notes", "DateAdded", "ForDeckId", "ForDeckName",
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

        /// <summary>Copies of a printing on the want list, per finish (finish → copies): for anyone,
        /// or only for this deck / the Collection ("") when <paramref name="forDeckId"/> is given.</summary>
        public static Dictionary<string, int> WantedCopies(string scryfallId, bool etchedOnly, string? forDeckId = null)
        {
            var map = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(scryfallId)) return map;
            try
            {
                using var db = new CollectionDbContext();
                foreach (var e in db.WantListEntries.Where(e => e.ScryfallId == scryfallId).ToList()
                                    .Where(e => forDeckId == null || (e.ForDeckId ?? "") == forDeckId))
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
        /// <summary>Put <paramref name="qty"/> copies of a pool printing in one finish on the want list,
        /// for a deck (its id and name) or the Collection ("").</summary>
        public static EditResult AddToWantList(object poolCard, string finish, int qty, string forDeckId = "", string forDeckName = "")
        {
            forDeckId ??= ""; forDeckName = forDeckId.Length == 0 ? "" : forDeckName ?? "";
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
                var entry = FindWantEntry(db, sid, finish, e && !f, forDeckId);
                if (entry == null)
                {
                    entry = NewWantEntry(poolCard);
                    entry.ForDeckId = forDeckId;
                    db.WantListEntries.Add(entry);
                }
                entry.ForDeckName = forDeckName;
                entry.Finish = finish;
                entry.IsFoil = finish != CardFinish.NonFoil;
                entry.Quantity += qty;
                entry.Price = PaperPriceOf(poolCard, finish);       // today's market price
                db.SaveChanges();
                return new EditResult
                {
                    Changed = qty,
                    Message = $"Added {qty} × {label} to the Want List {ForText(forDeckId, forDeckName)} (now {entry.Quantity}).",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not add {label} to the Want List: {ex.Message}");
            }
        }

        /// <summary>Take up to <paramref name="qty"/> copies (int.MaxValue = all) of one finish off the want list:
        /// the wants for this deck / the Collection (""), or for anyone when <paramref name="forDeckId"/> is null.
        /// Only the want goes: nothing in the collection or the decks changes.</summary>
        public static EditResult RemoveFromWantList(string sid, string finish, int qty, string name, string? forDeckId = null)
        {
            if (qty < 1) return Fail("Enter a quantity of 1 or more.");
            finish = CardFinish.Normalize(finish);
            var key = WantKey(sid, finish);
            string label = $"{name} ({CardFinish.Display(finish)})";
            try
            {
                using var db = new CollectionDbContext();
                bool eo = EtchedOnlyPrinting(CardsTable, sid, new Dictionary<string, bool>());
                var entries = FindWantEntries(db, sid, finish, eo, forDeckId);
                int have = entries.Sum(e => Math.Max(0, e.Quantity));
                if (have <= 0) return Fail($"{label} isn't on the Want List.", key);
                if (forDeckId != null && entries.Count > 0) label += " " + ForText(forDeckId, entries[0].ForDeckName);
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
        // GOT IT (want list → collection → the decks it was for)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>The wants for one printing + finish: decks first (A→Z), then the Collection.</summary>
        public static List<WantNeed> WantsFor(string sid, string finish)
        {
            var list = new List<WantNeed>();
            if (string.IsNullOrEmpty(sid)) return list;
            finish = CardFinish.Normalize(finish);
            try
            {
                List<WantListEntry> rows;
                using (var db = new CollectionDbContext())
                {
                    bool eo = EtchedOnlyPrinting(CardsTable, sid, new Dictionary<string, bool>());
                    rows = FindWantEntries(db, sid, finish, eo).Where(e => e.Quantity > 0).ToList();
                }
                var decks = DecksById();
                foreach (var g in rows.GroupBy(e => e.ForDeckId ?? ""))
                {
                    string id = g.Key;
                    var first = g.First();
                    var need = new WantNeed
                    {
                        EntryIds = g.Select(e => e.WantListEntryId).ToList(),
                        ForDeckId = id,
                        ForName = id.Length == 0 ? "Collection" : first.ForDeckName,
                        Wanted = g.Sum(e => Math.Max(0, e.Quantity)),
                        DeckMissing = -1,
                    };
                    if (id.Length > 0)
                    {
                        if (decks.TryGetValue(id, out var deck))
                        {
                            need.ForName = string.IsNullOrWhiteSpace(deck.Name) ? need.ForName : deck.Name;
                            need.DeckMissing = ClaimStatus(deck)
                                .Where(l => l.Table == CardsTable && l.Finish == finish &&
                                            string.Equals(l.ScryfallId, sid, StringComparison.OrdinalIgnoreCase))
                                .Sum(l => l.Needed);
                        }
                        else need.DeckGone = true;
                    }
                    list.Add(need);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wants for: {ex.Message}");
            }
            return list.OrderBy(n => n.IsCollection ? 1 : 0)
                       .ThenBy(n => n.ForName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Your decks by their stable id (the deck files, cached).</summary>
        private static Dictionary<string, Deck> DecksById() =>
            DeckIndexService.AllDecks()
                .Where(d => !string.IsNullOrEmpty(d.Deck.DeckId))
                .GroupBy(d => d.Deck.DeckId)
                .ToDictionary(g => g.Key, g => g.First().Deck);

        /// <summary>
        /// Got It: <paramref name="received"/> copies of a printing in one finish
        /// arrived. They go into your card collection (this language and
        /// condition). Then, for each want in <paramref name="assign"/> (in
        /// order: a want and how many of the copies are for it), those copies
        /// come off that want; for a deck they're also claimed for it (up to
        /// what the deck still misses of this printing). Copies not assigned stay
        /// free in the collection. Wants not assigned stay on the Want List.
        /// </summary>
        public static EditResult GotIt(string sid, string finish, int received, IReadOnlyList<(WantNeed Want, int Copies)> assign,
                                       string name, string? language, string? condition)
        {
            if (received < 1) return Fail("Enter how many copies you got (1 or more).");
            finish = CardFinish.Normalize(finish);
            var key = WantKey(sid, finish);
            string label = $"{name} ({CardFinish.Display(finish)})";
            var pool = FindPoolCard(CardsTable, sid);
            if (pool == null) return Fail($"{label}: not in the card pool, so it can't go into the collection.", key);
            int assigned = assign.Sum(a => Math.Max(0, a.Copies));
            if (assigned > received) return Fail($"{label}: {assigned} copies assigned but only {received} received.", key);

            try
            {
                var added = Add(CardsTable, pool, finish, received, language, condition);
                if (added.Changed <= 0) return added;
                var collKey = KeyFor(CardsTable, sid, finish, language, condition);
                var parts = new List<string>();
                var warnings = new List<string>();
                var decks = DecksById();

                // Off the Want List: each assigned want, its own rows.
                using (var db = new CollectionDbContext())
                {
                    foreach (var (want, copies) in assign)
                    {
                        if (copies <= 0) continue;
                        var rows = db.WantListEntries.Where(e => want.EntryIds.Contains(e.WantListEntryId)).ToList();
                        int left = copies;
                        foreach (var e in rows)
                        {
                            int t = Math.Min(left, Math.Max(0, e.Quantity));
                            e.Quantity -= t;
                            if (e.Quantity <= 0) db.WantListEntries.Remove(e);
                            left -= t;
                            if (left == 0) break;
                        }
                    }
                    db.SaveChanges();
                }

                // Claimed for the decks they were for (never more than a deck still misses).
                foreach (var (want, copies) in assign)
                {
                    if (copies <= 0) continue;
                    if (want.IsCollection) { parts.Add($"{copies} for the Collection"); continue; }
                    if (!decks.TryGetValue(want.ForDeckId, out var deck))
                    {
                        parts.Add($"{copies} for {want.ForName}");
                        warnings.Add($"{want.ForName}: deck file not found, so nothing was claimed for it");
                        continue;
                    }
                    int missing = ClaimStatus(deck)
                        .Where(l => l.Table == CardsTable && l.Finish == finish &&
                                    string.Equals(l.ScryfallId, sid, StringComparison.OrdinalIgnoreCase))
                        .Sum(l => l.Needed);
                    int claim = Math.Min(copies, missing);
                    int got = 0;
                    if (claim > 0)
                    {
                        var r = ClaimRow(CardsTable, deck, collKey, claim, name);
                        got = Math.Max(0, r.Changed);
                        if (r.Warning || got < claim) warnings.Add(r.Message);
                    }
                    parts.Add(got > 0 ? $"{got} claimed for {deck.Name}" : $"{copies} for {deck.Name}");
                    if (copies > got)
                        warnings.Add(missing == 0
                            ? $"{deck.Name} doesn't list this printing in {CardFinish.Display(finish)} (or has it already), so {copies - got} stay free"
                            : $"{copies - got} for {deck.Name} stay free (it only missed {missing})");
                }
                int extra = received - assigned;
                if (extra > 0) parts.Add($"{extra} extra, free in your collection");

                var result = new EditResult
                {
                    Changed = added.Changed,
                    Warning = warnings.Count > 0,
                    Message = $"Got {received} × {name} ({collKey.Text}): " +
                              (parts.Count > 0 ? string.Join(", ", parts) : "into your collection") + "." +
                              (warnings.Count > 0 ? "  " + string.Join("; ", warnings) + "." : ""),
                };
                result.Touched.Add(key);
                result.Touched.Add(collKey);
                return result;
            }
            catch (Exception ex)
            {
                return Fail($"Could not move {label} into the collection: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // FOR (who a want is for)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Change who these want rows are for (a deck, or "" = the Collection).
        /// A row that lands where a want for the same printing + finish already
        /// is joins it.
        /// </summary>
        public static EditResult SetWantFor(IReadOnlyList<int> entryIds, string forDeckId, string forDeckName)
        {
            forDeckId ??= "";
            forDeckName = forDeckId.Length == 0 ? "" : forDeckName ?? "";
            try
            {
                using var db = new CollectionDbContext();
                var rows = db.WantListEntries.Where(e => entryIds.Contains(e.WantListEntryId)).ToList();
                if (rows.Count == 0) return Fail("Those rows are no longer on the Want List.");
                int changed = 0;
                var touched = new List<RowKey>();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                bool any = false;
                foreach (var e in rows)
                {
                    if ((e.ForDeckId ?? "") == forDeckId) continue;
                    if (!any) { EnsureBackup(); any = true; }
                    bool eo = EtchedOnlyPrinting(CardsTable, e.ScryfallId, cache);
                    string finish = CardFinish.Shown(e.Finish, eo);
                    var there = db.WantListEntries.Where(x => x.ScryfallId == e.ScryfallId && x.WantListEntryId != e.WantListEntryId)
                        .ToList()
                        .FirstOrDefault(x => (x.ForDeckId ?? "") == forDeckId && CardFinish.Shown(x.Finish, eo) == finish &&
                                             !rows.Contains(x));
                    if (there != null)
                    {
                        there.Quantity += Math.Max(0, e.Quantity);
                        if (!there.OfferPrice.HasValue) there.OfferPrice = e.OfferPrice;
                        if (string.IsNullOrWhiteSpace(there.Notes)) there.Notes = e.Notes;
                        db.WantListEntries.Remove(e);
                    }
                    else
                    {
                        e.ForDeckId = forDeckId;
                        e.ForDeckName = forDeckName;
                    }
                    changed++;
                    touched.Add(WantKey(e.ScryfallId, finish));
                }
                if (!any) return new EditResult { Message = $"Already {ForText(forDeckId, forDeckName)}." };
                db.SaveChanges();
                var result = new EditResult
                {
                    Changed = changed,
                    Message = $"{changed} want row{(changed == 1 ? "" : "s")} now {ForText(forDeckId, forDeckName)}.",
                };
                result.Touched.AddRange(touched);
                return result;
            }
            catch (Exception ex)
            {
                return Fail($"Could not change who the wants are for: {ex.Message}");
            }
        }

        /// <summary>
        /// One-time tidy-up from before the For column: a want whose Notes say
        /// "For: Deck Name" (Find Missing wrote that) becomes a want for that
        /// deck, and the note goes. Notes naming several decks, or a deck that
        /// isn't found, are left as they are. Returns how many rows changed.
        /// </summary>
        public static int ConvertForNotes()
        {
            try
            {
                using var db = new CollectionDbContext();
                var rows = db.WantListEntries.Where(e => e.ForDeckId == "" && e.Notes.Contains("For:")).ToList();
                if (rows.Count == 0) return 0;
                var byName = DeckIndexService.AllDecks()
                    .Where(d => !string.IsNullOrEmpty(d.Deck.DeckId) && !string.IsNullOrWhiteSpace(d.Deck.Name))
                    .GroupBy(d => d.Deck.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Select(x => x.Deck.DeckId).Distinct().Count() == 1)      // a name two decks share: can't tell
                    .ToDictionary(g => g.Key, g => g.First().Deck, StringComparer.OrdinalIgnoreCase);
                int changed = 0;
                foreach (var e in rows)
                {
                    var parts = e.Notes.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                    var fors = parts.Where(p => p.StartsWith("For:", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (fors.Count != 1) continue;
                    string deckName = fors[0][4..].Trim();
                    if (!byName.TryGetValue(deckName, out var deck)) continue;
                    // Another want for that deck and finish already? Join it.
                    var there = db.WantListEntries.Local.Concat(db.WantListEntries.Where(x => x.ScryfallId == e.ScryfallId).ToList())
                        .Distinct()
                        .FirstOrDefault(x => x != e && x.ScryfallId == e.ScryfallId && x.Finish == e.Finish &&
                                             x.ForDeckId == deck.DeckId && db.Entry(x).State != EntityState.Deleted);
                    string rest = string.Join("; ", parts.Where(p => !ReferenceEquals(p, fors[0])));
                    if (there != null)
                    {
                        there.Quantity += Math.Max(0, e.Quantity);
                        db.WantListEntries.Remove(e);
                    }
                    else
                    {
                        e.ForDeckId = deck.DeckId;
                        e.ForDeckName = deck.Name;
                        e.Notes = rest;
                    }
                    changed++;
                }
                if (changed > 0) { EnsureBackup(); db.SaveChanges(); }
                return changed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Convert For notes: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Keep deck names on want rows current (a deck renamed since). Never throws.</summary>
        public static void RefreshWantDeckNames()
        {
            try
            {
                var decks = DecksById();
                using var db = new CollectionDbContext();
                bool any = false;
                foreach (var e in db.WantListEntries.Where(e => e.ForDeckId != "").ToList())
                    if (decks.TryGetValue(e.ForDeckId, out var d) && !string.IsNullOrWhiteSpace(d.Name) && e.ForDeckName != d.Name)
                    {
                        e.ForDeckName = d.Name;
                        any = true;
                    }
                if (any) db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Want deck names: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // UNDO for Got It: the want list, the collection AND every deck's
        // claims on those printings, all together
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Every deck's claims on these printings before Got It.</summary>
        public static UsageSnapshot? TakeUsageSnapshot(IEnumerable<string> sids)
        {
            var snap = new UsageSnapshot();
            foreach (var s in sids) if (!string.IsNullOrEmpty(s)) snap.Sids.Add(s);
            if (snap.Sids.Count == 0) return null;
            try
            {
                using var db = new CollectionDbContext();
                snap.Before = UsagesOf(db, snap.Sids);
                return snap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Usage snapshot: {ex.Message}");
                return null;
            }
        }

        public static void MarkUsagesAfter(UsageSnapshot snap)
        {
            try
            {
                using var db = new CollectionDbContext();
                snap.After = UsagesOf(db, snap.Sids);
            }
            catch { snap.After = null; }
        }

        private static List<DeckUsage> UsagesOf(CollectionDbContext db, HashSet<string> sids)
        {
            var list = sids.ToList();                       // a plain list: SQL "IN (…)"
            return db.DeckUsages.AsNoTracking().Where(u => list.Contains(u.ScryfallId)).ToList();
        }

        /// <summary>Undo Got It: rows (want list + collection) and every deck's claims back, all or nothing.</summary>
        public static EditResult RestoreWithUsages(UsageSnapshot usages, IReadOnlyList<EditSnapshot> rows, string description)
        {
            try
            {
                using (var check = new CollectionDbContext())
                    if (usages.After == null || !SameClaims(UsagesOf(check, usages.Sids), usages.After))
                        return Fail($"Can't undo \"{description}\": the decks' copies of those cards changed since.");
                if (rows.Any(r => !CanRestore(r)))
                    return Fail($"Can't undo \"{description}\": those cards have changed since.");

                EnsureBackup();
                using var db = new CollectionDbContext();
                var touched = new List<RowKey>();
                foreach (var r in rows) touched.AddRange(RestoreInto(db, r));
                var sidList = usages.Sids.ToList();
                var current = db.DeckUsages.Where(u => sidList.Contains(u.ScryfallId)).ToList();
                var byId = current.ToDictionary(u => u.DeckUsageId);
                var keep = new HashSet<int>(usages.Before.Select(u => u.DeckUsageId));
                foreach (var u in current.Where(u => !keep.Contains(u.DeckUsageId))) db.DeckUsages.Remove(u);
                foreach (var old in usages.Before)
                {
                    if (!byId.TryGetValue(old.DeckUsageId, out var u))
                    {
                        u = new DeckUsage { DeckUsageId = old.DeckUsageId };
                        db.DeckUsages.Add(u);
                    }
                    u.ScryfallId = old.ScryfallId; u.DeckId = old.DeckId; u.DeckName = old.DeckName; u.DeckType = old.DeckType;
                    u.Quantity = old.Quantity; u.FoilQuantity = old.FoilQuantity;
                    u.EnteredNonFoil = old.EnteredNonFoil; u.EnteredFoil = old.EnteredFoil; u.EnteredEtched = old.EnteredEtched;
                    u.CollectionTable = old.CollectionTable; u.Language = old.Language; u.Condition = old.Condition;
                    u.Category = old.Category; u.DateRecorded = old.DateRecorded;
                }
                db.SaveChanges();                                   // rows + claims in one save
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                Recompute(db, CardsTable, usages.Sids, cache);       // Used counts from the claims put back
                db.SaveChanges();
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
        /// Missing → Want List: raise the deck's wants to <paramref name="need"/>
        /// copies of this printing + finish — never above it, never doubled
        /// (pressing it again adds nothing). The want is for the deck.
        /// </summary>
        public static EditResult TopUpWantList(object poolCard, string finish, int need, Deck deck)
        {
            finish = CardFinish.Normalize(finish);
            string sid = GetString(poolCard, "ScryfallId");
            var key = WantKey(sid, finish);
            string forId = deck.DeckId ?? "";
            string label = $"{Name(poolCard)} ({GetString(poolCard, "SetCode").ToUpperInvariant()} #{GetString(poolCard, "CollectorNumber")}, {CardFinish.Display(finish)})";
            if (need <= 0) return new EditResult { Message = $"{label}: nothing missing.", Touched = { key } };
            var (nf, f, e) = FinishesOf(CardsTable, FindPoolCard(CardsTable, sid) ?? poolCard);   // the card database decides
            bool exists = FinishExists(finish, nf, f, e);
            if (!exists) return Fail($"{Name(poolCard)}: this printing doesn't exist in {CardFinish.Display(finish)}.", key);
            try
            {
                using var db = new CollectionDbContext();
                var entries = FindWantEntries(db, sid, finish, e && !f, forId);
                int have = entries.Sum(x => Math.Max(0, x.Quantity));
                if (have >= need)
                    return new EditResult { Message = $"{label}: already on the Want List for {deck.Name} ({have}).", Touched = { key } };
                EnsureBackup();
                var entry = entries.FirstOrDefault();
                if (entry == null)
                {
                    entry = NewWantEntry(poolCard);
                    entry.ForDeckId = forId;
                    db.WantListEntries.Add(entry);
                }
                entry.ForDeckName = forId.Length == 0 ? "" : deck.Name;
                entry.Finish = finish;
                entry.IsFoil = finish != CardFinish.NonFoil;
                entry.Quantity += need - have;
                entry.Price = PaperPriceOf(poolCard, finish);
                db.SaveChanges();
                return new EditResult
                {
                    Changed = need - have,
                    Message = $"Want List: {label} for {deck.Name} {have} → {need}.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not add {label} to the Want List: {ex.Message}", key);
            }
        }
    }

    /// <summary>One want for a printing + finish: a deck's, or the Collection's (Got It).</summary>
    public sealed class WantNeed
    {
        /// <summary>The want rows (usually one).</summary>
        public List<int> EntryIds { get; init; } = new();
        /// <summary>The deck's id, or "" for the Collection.</summary>
        public string ForDeckId { get; init; } = "";
        public string ForName { get; set; } = "";
        public bool IsCollection => ForDeckId.Length == 0;
        /// <summary>Copies on the Want List for it.</summary>
        public int Wanted { get; init; }
        /// <summary>Copies of this printing + finish the deck lists but hasn't claimed (−1: the Collection, or unknown).</summary>
        public int DeckMissing { get; set; } = -1;
        /// <summary>The deck's file isn't there any more.</summary>
        public bool DeckGone { get; set; }
    }

    /// <summary>Every deck's claims on some printings, before and after Got It (its Undo).</summary>
    public sealed class UsageSnapshot
    {
        internal HashSet<string> Sids = new(StringComparer.OrdinalIgnoreCase);
        internal List<DeckUsage> Before = new();
        internal List<DeckUsage>? After;
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