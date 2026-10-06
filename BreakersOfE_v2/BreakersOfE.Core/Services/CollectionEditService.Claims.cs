using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>A deck's copies of one printing in one finish, and how many are claimed from the collection.</summary>
    public sealed class ClaimLine
    {
        /// <summary>"Collection" (cards) or "CollTokens" (tokens).</summary>
        public string Table { get; init; } = "";
        public string ScryfallId { get; init; } = "";
        public string Finish { get; init; } = CardFinish.NonFoil;
        public string Name { get; set; } = "";
        public string SetCode { get; set; } = "";
        public string CollectorNumber { get; set; } = "";
        /// <summary>Copies the deck lists (all its parts).</summary>
        public int Demand { get; set; }
        /// <summary>Copies claimed from the collection for this deck.</summary>
        public int Claimed { get; set; }
        public int Needed => Math.Max(0, Demand - Claimed);
        /// <summary>"Lightning Bolt (M10 #146, Non-Foil)".</summary>
        public string Text => $"{Name} ({SetCode.ToUpperInvariant()} #{CollectorNumber}, {CardFinish.Display(Finish)})";
    }

    /// <summary>One deck that claims copies of a collection row (the "Used in" table).</summary>
    public sealed class RowUse
    {
        public string DeckId { get; init; } = "";
        public string DeckName { get; init; } = "";
        /// <summary>"Commander", "Constructed — Modern", …</summary>
        public string DeckType { get; init; } = "";
        /// <summary>Where the printing sits in the deck: "Main deck", "Command zone, sideboard", …</summary>
        public string Part { get; init; } = "";
        public int Copies { get; init; }
        /// <summary>The deck file (empty when it can't be found any more).</summary>
        public string DeckPath { get; init; } = "";
        /// <summary>"Blessed Benediction ×1 (Commander · main deck)".</summary>
        public string Text => $"{DeckName} ×{Copies}" + (DeckType.Length > 0 ? $" ({DeckType} · {Part.ToLowerInvariant()})" : "");
    }

    /// <summary>A deck's claims and the collection rows they touch, as they were before an edit (Undo).</summary>
    public sealed class ClaimSnapshot
    {
        internal string DeckId = "";
        /// <summary>Only these printings' claims (the Trade Binder: just the cards an edit touched); null = all.</summary>
        internal HashSet<string>? Sids;
        internal List<DeckUsage> Claims = new();
        internal List<DeckUsage>? ClaimsAfter;
        internal List<EditSnapshot> Rows = new();
        /// <summary>The printings (Scryfall ids) whose collection rows it covers.</summary>
        public IReadOnlyCollection<string> Printings => Rows.SelectMany(r => r.Printings).Distinct().ToList();
    }

    /// <summary>
    /// Decks CLAIM collection copies (Edit → Decks). A claim is recorded in
    /// collection.db only (DeckUsages), never on the deck file: one usage row
    /// per deck × exact collection row (table, printing, language,
    /// condition), with the copies claimed per finish.
    ///
    /// A collection row's UsedCount is DERIVED: the sum of every deck's
    /// claims on it (<see cref="Recompute"/>), written after each change —
    /// never set any other way. Copies in use can't be removed from the
    /// collection (the rest of this service already refuses).
    ///
    /// A deck never claims more than it lists: <see cref="SyncClaims"/> runs
    /// after every deck change and frees any extra (a card removed, a count
    /// lowered, an Undo).
    ///
    /// v1 usage rows (no table, language or condition recorded) are converted
    /// the first time their card is touched (<see cref="ConvertV1"/>): their
    /// copies are placed on the card's actual rows of that finish.
    /// </summary>
    public static partial class CollectionEditService
    {
        public const string CardsTable = "Collection";
        public const string TokensTable = "CollTokens";

        /// <summary>The collection table a deck line's copies come from.</summary>
        public static string TableOf(DeckCard c) => c.IsTokenLine ? TokensTable : CardsTable;

        private static string TableOf(DeckUsage u) =>
            string.IsNullOrEmpty(u.CollectionTable) ? CardsTable : u.CollectionTable;

        private static readonly string[] Finishes = { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched };

        /// <summary>A usage row written by v1: no collection table, language or condition recorded.</summary>
        private static bool IsV1(DeckUsage u) => string.IsNullOrEmpty(u.CollectionTable);

        /// <summary>Does this v2 usage row claim this exact collection row?</summary>
        private static bool Matches(DeckUsage u, string table, RowKey key) =>
            !IsV1(u) && TableOf(u) == table &&
            string.Equals(u.ScryfallId, key.ScryfallId, StringComparison.OrdinalIgnoreCase) &&
            CardLanguage.Normalize(u.Language) == key.Language &&
            CardCondition.Normalize(u.Condition) == key.Condition;

        /// <summary>
        /// v1 → v2, for one card: each v1 usage row's copies are placed on the
        /// card's actual collection rows of that finish (English first, worst
        /// condition first, up to what each row has left), as v2 claims. Copies
        /// that don't fit anywhere stay claimed on the first row of that finish
        /// (or English · Near Mint), so nothing claimed is lost. True if anything changed.
        /// </summary>
        private static bool ConvertV1(CollectionDbContext db, string sid, Dictionary<string, bool> cache)
        {
            var old = db.DeckUsages.Where(u => u.ScryfallId == sid && u.CollectionTable == "").ToList();
            if (old.Count == 0) return false;
            string table = CardsTable;                           // v1 claims were cards only
            bool eo = EtchedOnlyPrinting(table, sid, cache);
            var keys = Rows(db, table, sid)
                .GroupBy(r => KeyOf(r, eo))
                .Select(g => (Key: g.Key, Qty: g.Sum(r => Math.Max(0, r.Quantity))))
                .ToList();
            db.DeckUsages.Where(u => u.ScryfallId == sid).Load();
            var v2 = db.DeckUsages.Local.Where(u => !IsV1(u) && u.ScryfallId == sid).ToList();
            var left = keys.ToDictionary(k => k.Key,
                k => k.Qty - v2.Where(u => Matches(u, table, k.Key)).Sum(u => ClaimOf(u, k.Key.Finish, eo)));

            foreach (var u in old)
            {
                foreach (var f in Finishes)
                {
                    int n = ClaimOf(u, f, eo);                   // v1 foil of an etched-only printing = etched
                    if (n <= 0) continue;
                    var targets = keys.Select(k => k.Key).Where(k => k.Finish == f)
                        .OrderBy(k => k.Language == CardLanguage.Default ? 0 : 1)
                        .ThenBy(k => WorstFirst(k.Condition)).ToList();
                    foreach (var k in targets)
                    {
                        int take = Math.Min(n, Math.Max(0, left[k]));
                        if (take <= 0) continue;
                        AddToClaim(db, u.DeckId, u.DeckName, u.DeckType, table, k, take);
                        left[k] -= take;
                        n -= take;
                        if (n == 0) break;
                    }
                    if (n > 0)
                        AddToClaim(db, u.DeckId, u.DeckName, u.DeckType, table,
                            targets.FirstOrDefault() ?? RowKey.Of(sid, f, CardLanguage.Default, CardCondition.Default), n);
                }
                db.DeckUsages.Remove(u);
            }
            return true;
        }

        /// <summary>This deck's usage row for one exact collection row, made if needed (sees unsaved rows too).</summary>
        private static DeckUsage GetOrAddClaim(CollectionDbContext db, string deckId, string table, RowKey key)
        {
            db.DeckUsages.Where(u => u.DeckId == deckId && u.ScryfallId == key.ScryfallId).Load();
            var hit = db.DeckUsages.Local.FirstOrDefault(u => u.DeckId == deckId && Matches(u, table, key) &&
                                                              db.Entry(u).State != EntityState.Deleted);
            if (hit != null) return hit;
            hit = new DeckUsage
            {
                DeckId = deckId,
                ScryfallId = key.ScryfallId,
                CollectionTable = table,
                Language = key.Language,
                Condition = key.Condition,
            };
            db.DeckUsages.Add(hit);
            return hit;
        }

        private static void AddToClaim(CollectionDbContext db, string deckId, string deckName, string deckType,
                                       string table, RowKey key, int n)
        {
            var u = GetOrAddClaim(db, deckId, table, key);
            SetClaim(u, key.Finish, ClaimOf(u, key.Finish, false) + n);
            if (u.DeckName.Length == 0) u.DeckName = deckName;
            if (u.DeckType.Length == 0) u.DeckType = deckType;
            u.DateRecorded = DateTime.Now;
        }

        /// <summary>Claimed copies of one finish on a usage row (v1 foil of an etched-only printing = etched).</summary>
        private static int ClaimOf(DeckUsage u, string finish, bool etchedOnly) => finish switch
        {
            CardFinish.Etched => u.EnteredEtched + (etchedOnly ? u.EnteredFoil : 0),
            CardFinish.Foil => etchedOnly ? 0 : u.EnteredFoil,
            _ => u.EnteredNonFoil,
        };

        private static void SetClaim(DeckUsage u, string finish, int n)
        {
            n = Math.Max(0, n);
            switch (finish)
            {
                case CardFinish.Etched: u.EnteredEtched = n; break;
                case CardFinish.Foil: u.EnteredFoil = n; break;
                default: u.EnteredNonFoil = n; break;
            }
        }

        /// <summary>Store a usage row in v2 form: table, normalized language/condition, etched as etched.</summary>
        private static void Tidy(DeckUsage u, bool etchedOnly)
        {
            if (IsV1(u)) return;                 // ConvertV1 places these first
            u.Language = CardLanguage.Normalize(u.Language);
            u.Condition = CardCondition.Normalize(u.Condition);
            if (etchedOnly && u.EnteredFoil > 0)
            {
                u.EnteredEtched += u.EnteredFoil;
                u.EnteredFoil = 0;
            }
        }

        /// <summary>
        /// Condition order for claiming: worst first (Damaged … Near Mint), so
        /// the nicest copies stay free; Unknown last.
        /// </summary>
        private static int WorstFirst(string condition)
        {
            var all = CardCondition.All.ToList();
            string c = CardCondition.Normalize(condition);
            if (c == CardCondition.Unknown) return int.MaxValue;
            int i = all.IndexOf(c);
            return i < 0 ? int.MaxValue - 1 : -i;          // higher index (worse) first
        }

        // ══════════════════════════════════════════════════════════════════
        // STATUS
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Every printing + finish the deck lists or has claimed: copies listed vs. claimed.</summary>
        public static List<ClaimLine> ClaimStatus(Deck deck)
        {
            var lines = new Dictionary<(string, string, string), ClaimLine>();
            ClaimLine Line(string table, string sid, string finish) =>
                lines.TryGetValue((table, sid, finish), out var l) ? l
                    : lines[(table, sid, finish)] = new ClaimLine { Table = table, ScryfallId = sid, Finish = finish };

            foreach (var c in deck.Cards)
            {
                if (string.IsNullOrEmpty(c.ScryfallId)) continue;
                foreach (var f in Finishes)
                {
                    int n = c.CountOf(f);
                    if (n <= 0) continue;
                    var l = Line(TableOf(c), c.ScryfallId, f);
                    l.Demand += n;
                    l.Name = c.Name;
                    l.SetCode = c.SetCode;
                    l.CollectorNumber = c.CollectorNumber;
                }
            }

            if (!string.IsNullOrEmpty(deck.DeckId))
            {
                try
                {
                    using var db = new CollectionDbContext();
                    var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                    foreach (var u in db.DeckUsages.AsNoTracking().Where(u => u.DeckId == deck.DeckId).ToList())
                    {
                        string table = TableOf(u);
                        bool eo = u.EnteredFoil > 0 && EtchedOnlyPrinting(table, u.ScryfallId, cache);
                        foreach (var f in Finishes)
                        {
                            int n = ClaimOf(u, f, eo);
                            if (n > 0) Line(table, u.ScryfallId, f).Claimed += n;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Claim status: {ex.Message}");
                }
            }
            return lines.Values.ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        // USED = SUM OF CLAIMS
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Set UsedCount on every row of these printings from all decks' claims.
        /// Call after claims were saved (queries don't see unsaved new rows).
        /// </summary>
        private static void Recompute(CollectionDbContext db, string table, IEnumerable<string> sids,
                                      Dictionary<string, bool> etchedCache)
        {
            foreach (var sid in sids.Where(s => !string.IsNullOrEmpty(s)).Distinct())
            {
                if (table == CardsTable && ConvertV1(db, sid, etchedCache)) db.SaveChanges();
                var rows = Rows(db, table, sid);
                if (rows.Count == 0) continue;
                bool eo = EtchedOnlyPrinting(table, sid, etchedCache);
                var claims = db.DeckUsages.Where(u => u.ScryfallId == sid).ToList();
                // Rows that share a key (e.g. "NM" next to "Near Mint") share its claims:
                // filled in order up to each row's quantity, the rest on the last.
                foreach (var g in rows.GroupBy(r => KeyOf(r, eo)))
                {
                    int used = claims.Where(u => Matches(u, table, g.Key)).Sum(u => ClaimOf(u, g.Key.Finish, eo));
                    var list = g.ToList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        int take = i == list.Count - 1 ? used : Math.Min(used, Math.Max(0, list[i].Quantity));
                        if (list[i].UsedCount != take) list[i].UsedCount = take;
                        used -= take;
                    }
                }
            }
        }

        private static string DeckTypeText(Deck deck) => DeckFormats.For(deck).Name;

        /// <summary>Claim up to <paramref name="n"/> free copies of one row for the deck (inside an open context).</summary>
        private static int ClaimFrom(CollectionDbContext db, Deck deck, string table, Row row, RowKey key, int n, bool etchedOnly)
        {
            int free = Math.Max(0, row.Quantity - Math.Max(0, row.UsedCount));
            int take = Math.Min(n, free);
            if (take <= 0) return 0;
            var u = GetOrAddClaim(db, deck.DeckId, table, key);
            Tidy(u, etchedOnly);
            SetClaim(u, key.Finish, ClaimOf(u, key.Finish, false) + take);
            u.DeckName = deck.Name;
            u.DeckType = DeckTypeText(deck);
            u.DateRecorded = DateTime.Now;
            row.UsedCount = row.UsedCount + take;           // recomputed after saving; keeps "free" right meanwhile
            return take;
        }

        // ══════════════════════════════════════════════════════════════════
        // CLAIM
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Collection → Deck: claim up to <paramref name="n"/> copies of exactly this collection row.</summary>
        public static EditResult ClaimRow(string table, Deck deck, RowKey key, int n, string name)
        {
            if (string.IsNullOrEmpty(deck.DeckId)) return Fail("Save the deck first.");
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                Recompute(db, table, new[] { key.ScryfallId }, cache);
                db.SaveChanges();
                bool eo = EtchedOnlyPrinting(table, key.ScryfallId, cache);
                var row = FindExact(Rows(db, table, key.ScryfallId), key, eo);
                if (row == null) return Fail($"{name}: that collection row isn't there any more.");
                int got = ClaimFrom(db, deck, table, row, key, n, eo);
                if (got == 0)
                    return Fail($"{name} ({key.Text}): no free copies — {UsedBy(db, table, key.ScryfallId)}.");
                db.SaveChanges();
                Recompute(db, table, new[] { key.ScryfallId }, cache);
                db.SaveChanges();
                return new EditResult
                {
                    Changed = got,
                    Warning = got < n,
                    Message = got < n
                        ? $"Claimed {got} of {n} × {name} ({key.Text}) — only {got} free."
                        : $"Claimed {got} × {name} ({key.Text}) for {deck.Name}.",
                    Touched = { key },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not claim {name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Deck → Collection, "Use copies I own": claim free copies of this
        /// printing and finish until the deck has <paramref name="want"/> more —
        /// English first, then the worst condition first. Never another printing.
        /// </summary>
        public static EditResult ClaimOwned(string table, Deck deck, string sid, string finish, int want, string name)
        {
            if (string.IsNullOrEmpty(deck.DeckId)) return Fail("Save the deck first.");
            if (want <= 0) return new EditResult { Message = $"{name}: nothing more to claim." };
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                Recompute(db, table, new[] { sid }, cache);
                db.SaveChanges();
                bool eo = EtchedOnlyPrinting(table, sid, cache);
                var candidates = Rows(db, table, sid)
                    .Select(r => (row: r, key: KeyOf(r, eo)))
                    .Where(x => x.key.Finish == finish && x.row.Quantity - Math.Max(0, x.row.UsedCount) > 0)
                    .OrderBy(x => x.key.Language == CardLanguage.Default ? 0 : 1)
                    .ThenBy(x => WorstFirst(x.key.Condition))
                    .ToList();
                int got = 0;
                var touched = new List<RowKey>();
                foreach (var (row, key) in candidates)
                {
                    if (got >= want) break;
                    int n = ClaimFrom(db, deck, table, row, key, want - got, eo);
                    if (n > 0) { got += n; touched.Add(key); }
                }
                if (got == 0)
                    return Fail($"{name} ({CardFinish.Display(finish)}): no free copies of this printing in your collection.");
                db.SaveChanges();
                Recompute(db, table, new[] { sid }, cache);
                db.SaveChanges();
                var result = new EditResult
                {
                    Changed = got,
                    Warning = got < want,
                    Message = got < want
                        ? $"{name} ({CardFinish.Display(finish)}): used {got} owned — {want - got} still missing."
                        : $"{name} ({CardFinish.Display(finish)}): used {got} owned.",
                };
                result.Touched.AddRange(touched);
                return result;
            }
            catch (Exception ex)
            {
                return Fail($"Could not claim {name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Deck → Collection, "Add as new copies": the copies go into the
        /// collection (a new or matching row) and are claimed for the deck.
        /// </summary>
        public static EditResult AddAndClaim(string table, Deck deck, object poolCard, string finish, int qty,
                                             string? language, string? condition)
        {
            if (string.IsNullOrEmpty(deck.DeckId)) return Fail("Save the deck first.");
            var added = Add(table, poolCard, finish, qty, language, condition);
            if (added.Changed <= 0) return added;
            string sid = GetString(poolCard, "ScryfallId");
            var key = KeyFor(table, sid, finish, language, condition);
            var claimed = ClaimRow(table, deck, key, added.Changed, Name(poolCard));
            return new EditResult
            {
                Changed = added.Changed,
                Warning = claimed.Warning,
                Message = claimed.Warning
                    ? $"{added.Message} {claimed.Message}"
                    : $"Added {added.Changed} × {Label(poolCard, key, table)} to the collection for {deck.Name}.",
                Touched = { key },
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // FREE
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// After every deck change: a deck never claims more than it lists.
        /// Extra claims are freed (other languages first, then the best
        /// condition first — the reverse of claiming). Also keeps the deck's
        /// name and type current on its usage rows. Returns copies freed, or
        /// −1 when it failed (<paramref name="error"/> says why) — the page
        /// must say so: the collection may still claim copies the deck no
        /// longer lists.
        /// </summary>
        public static int SyncClaims(Deck deck, out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(deck.DeckId)) return 0;
            try
            {
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                // v1 usage rows of this deck: placed on real rows first.
                var v1 = db.DeckUsages.Where(u => u.DeckId == deck.DeckId && u.CollectionTable == "")
                           .Select(u => u.ScryfallId).Distinct().ToList();
                if (v1.Count > 0)
                {
                    EnsureBackup();
                    foreach (var sid in v1) ConvertV1(db, sid, cache);
                    db.SaveChanges();
                }
                var converted = v1.Select(sid => (Table: CardsTable, Sid: sid)).ToList();
                var claims = db.DeckUsages.Where(u => u.DeckId == deck.DeckId).ToList();
                if (claims.Count == 0) return 0;
                // Claims and Used counts change together, or not at all.
                using var tx = db.Database.BeginTransaction();

                var demand = new Dictionary<(string, string, string), int>();
                foreach (var c in deck.Cards)
                    foreach (var f in Finishes)
                        if (c.CountOf(f) > 0 && !string.IsNullOrEmpty(c.ScryfallId))
                        {
                            var k = (TableOf(c), c.ScryfallId, f);
                            demand[k] = demand.GetValueOrDefault(k) + c.CountOf(f);
                        }

                int freed = 0;
                var touched = new HashSet<(string Table, string Sid)>();
                string type = DeckTypeText(deck);
                foreach (var group in claims.GroupBy(u => (Table: TableOf(u), u.ScryfallId)))
                {
                    bool eo = group.Any(u => u.EnteredFoil > 0) && EtchedOnlyPrinting(group.Key.Table, group.Key.ScryfallId, cache);
                    foreach (var u in group)
                    {
                        Tidy(u, eo);
                        if (u.DeckName != deck.Name) u.DeckName = deck.Name;
                        if (u.DeckType != type) u.DeckType = type;
                    }
                    foreach (var f in Finishes)
                    {
                        int have = group.Sum(u => ClaimOf(u, f, false));
                        int excess = have - demand.GetValueOrDefault((group.Key.Table, group.Key.ScryfallId, f));
                        if (excess <= 0) continue;
                        // Free the copies you'd least want claimed: other languages, then the best condition.
                        foreach (var u in group.OrderBy(u => u.Language == CardLanguage.Default ? 1 : 0)
                                               .ThenByDescending(u => WorstFirst(u.Condition)))
                        {
                            if (excess <= 0) break;
                            int n = Math.Min(excess, ClaimOf(u, f, false));
                            if (n <= 0) continue;
                            SetClaim(u, f, ClaimOf(u, f, false) - n);
                            excess -= n;
                            freed += n;
                            touched.Add(group.Key);
                        }
                    }
                }
                foreach (var u in claims.Where(u => u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched <= 0))
                    db.DeckUsages.Remove(u);
                if (freed > 0) EnsureBackup();
                db.SaveChanges();
                // Used counts: cards with claims freed, and cards whose v1 claims were placed.
                foreach (var t in touched.Concat(converted).GroupBy(t => t.Table))
                    Recompute(db, t.Key, t.Select(x => x.Sid).Distinct(), cache);
                db.SaveChanges();
                tx.Commit();
                return freed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync claims: {ex.Message}");
                error = ex.Message;
                return -1;
            }
        }

        /// <summary>Tear down: free every copy the deck claims. Returns copies freed, or −1 when it failed.</summary>
        public static int ReleaseAll(string deckId)
        {
            if (string.IsNullOrEmpty(deckId)) return 0;
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var claims = db.DeckUsages.Where(u => u.DeckId == deckId).ToList();
                if (claims.Count == 0) return 0;
                int freed = claims.Sum(u => u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched);
                var touched = claims.Select(u => (Table: TableOf(u), u.ScryfallId)).Distinct().ToList();
                // Freeing the claims and fixing Used happen together, or not at all.
                using var tx = db.Database.BeginTransaction();
                db.DeckUsages.RemoveRange(claims);
                db.SaveChanges();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var t in touched.GroupBy(t => t.Table))
                    Recompute(db, t.Key, t.Select(x => x.ScryfallId), cache);
                db.SaveChanges();
                tx.Commit();
                return freed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Release all: {ex.Message}");
                return -1;
            }
        }

        /// <summary>Free up to <paramref name="n"/> claimed copies of one printing + finish (the line stays in the deck).</summary>
        public static EditResult Release(Deck deck, string table, string sid, string finish, int n, string name)
        {
            if (string.IsNullOrEmpty(deck.DeckId)) return Fail("Nothing claimed.");
            try
            {
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                if (table == CardsTable && ConvertV1(db, sid, cache)) { EnsureBackup(); db.SaveChanges(); }
                var group = db.DeckUsages.Where(u => u.DeckId == deck.DeckId && u.ScryfallId == sid).ToList()
                              .Where(u => TableOf(u) == table).ToList();
                bool eo = group.Any(u => u.EnteredFoil > 0) && EtchedOnlyPrinting(table, sid, cache);
                foreach (var u in group) Tidy(u, eo);
                int freed = 0;
                foreach (var u in group.OrderBy(u => u.Language == CardLanguage.Default ? 1 : 0)
                                       .ThenByDescending(u => WorstFirst(u.Condition)))
                {
                    if (freed >= n) break;
                    int take = Math.Min(n - freed, ClaimOf(u, finish, false));
                    if (take <= 0) continue;
                    SetClaim(u, finish, ClaimOf(u, finish, false) - take);
                    freed += take;
                }
                if (freed == 0) return Fail($"{name} ({CardFinish.Display(finish)}): no claimed copies.");
                foreach (var u in group.Where(u => u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched <= 0))
                    db.DeckUsages.Remove(u);
                EnsureBackup();
                db.SaveChanges();
                Recompute(db, table, new[] { sid }, cache);
                db.SaveChanges();
                return new EditResult { Changed = freed, Message = $"{name} ({CardFinish.Display(finish)}): freed {freed} — back to Available in the collection." };
            }
            catch (Exception ex)
            {
                return Fail($"Could not free {name}: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // USED IN (read-only): which decks claim a collection row
        // ══════════════════════════════════════════════════════════════════
        /// <summary>The collection table of a collection row object (only cards and tokens are claimed).</summary>
        public static string? TableOfEntry(object row) => row switch
        {
            CollectionEntry => CardsTable,
            TokenCollectionEntry => TokensTable,
            _ => null,
        };

        /// <summary>
        /// The decks claiming copies of exactly this collection row (its
        /// finish, language and condition), most copies first. Deck names,
        /// types and parts come from the deck files (the deck index).
        /// </summary>
        public static List<RowUse> UsesOfRow(object rowEntity)
        {
            var list = new List<RowUse>();
            if (TableOfEntry(rowEntity) is not { } table) return list;
            try
            {
                var row = new Row(rowEntity);
                string sid = row.ScryfallId;
                if (sid.Length == 0) return list;
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                bool eo = EtchedOnlyPrinting(table, sid, cache);
                var key = KeyOf(row, eo);

                List<DeckUsage> claims;
                using (var db = new CollectionDbContext())
                    claims = db.DeckUsages.AsNoTracking().Where(u => u.ScryfallId == sid).ToList();

                var decks = DeckIndexService.AllDecks()
                    .Where(d => !string.IsNullOrEmpty(d.Deck.DeckId))
                    .GroupBy(d => d.Deck.DeckId)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var g in claims.Where(u => Matches(u, table, key)).GroupBy(u => u.DeckId))
                {
                    int copies = g.Sum(u => ClaimOf(u, key.Finish, eo));
                    if (copies <= 0) continue;
                    if (g.Key == TradeBinderId)
                    {
                        // Not a deck: no file to open.
                        list.Add(new RowUse { DeckId = g.Key, DeckName = TradeBinderName, Part = "For trade", Copies = copies });
                        continue;
                    }
                    string name = g.First().DeckName, type = g.First().DeckType, part = "", path = "";
                    if (decks.TryGetValue(g.Key, out var d))
                    {
                        path = d.Path;
                        name = string.IsNullOrWhiteSpace(d.Deck.Name) ? System.IO.Path.GetFileNameWithoutExtension(d.Path) : d.Deck.Name;
                        type = DeckFormats.For(d.Deck).Name;
                        var parts = d.Deck.Cards
                            .Where(c => string.Equals(c.ScryfallId, sid, StringComparison.OrdinalIgnoreCase))
                            .Select(DeckEditService.SectionOf).Distinct().OrderBy(s => s)
                            .Select(DeckEditService.SectionName).ToList();
                        part = parts.Count == 0 ? "not in the deck list" : string.Join(", ", parts);
                        if (part.Length > 0) part = char.ToUpperInvariant(part[0]) + part[1..];
                    }
                    else
                    {
                        part = "deck file not found";
                    }
                    list.Add(new RowUse { DeckId = g.Key, DeckName = name, DeckType = type, Part = part, Copies = copies, DeckPath = path });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Used in: {ex.Message}");
            }
            return list.OrderByDescending(u => u.Copies).ThenBy(u => u.DeckName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        // UNDO (claims + the collection rows they touch)
        // ══════════════════════════════════════════════════════════════════
        private static List<DeckUsage> ClaimsOf(CollectionDbContext db, string deckId, HashSet<string>? sids = null)
        {
            var all = db.DeckUsages.AsNoTracking().Where(u => u.DeckId == deckId).ToList();
            return sids == null ? all : all.Where(u => sids.Contains(u.ScryfallId)).ToList();
        }

        /// <summary>
        /// The deck's claims and the rows of every printing they (or
        /// <paramref name="extra"/>) touch, before an edit.
        /// </summary>
        public static ClaimSnapshot? TakeClaimSnapshot(Deck deck, IEnumerable<(string Table, string Sid)> extra)
        {
            if (string.IsNullOrEmpty(deck.DeckId)) return null;
            try
            {
                using var db = new CollectionDbContext();
                var snap = new ClaimSnapshot { DeckId = deck.DeckId, Claims = ClaimsOf(db, deck.DeckId) };
                var printings = snap.Claims.Select(u => (Table: TableOf(u), Sid: u.ScryfallId))
                    .Concat(extra).Where(p => !string.IsNullOrEmpty(p.Sid)).Distinct().ToList();
                foreach (var t in printings.GroupBy(p => p.Table))
                    if (Snapshot(t.Key, t.Select(p => p.Sid)) is { } rows) snap.Rows.Add(rows);
                return snap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Claim snapshot: {ex.Message}");
                return null;
            }
        }

        /// <summary>Record the claims and rows right AFTER the edit (Undo's safety check).</summary>
        public static void MarkClaimsAfter(ClaimSnapshot snap)
        {
            foreach (var r in snap.Rows) MarkAfter(r);
            try
            {
                using var db = new CollectionDbContext();
                snap.ClaimsAfter = ClaimsOf(db, snap.DeckId, snap.Sids);
            }
            catch { snap.ClaimsAfter = null; }
        }

        /// <summary>Did the edit change the collection at all (claims or rows)?</summary>
        public static bool ClaimsChanged(ClaimSnapshot snap) =>
            snap.ClaimsAfter == null || !SameClaims(snap.Claims, snap.ClaimsAfter) ||
            snap.Rows.Any(r => r.After == null || r.After.Count != r.Rows.Count ||
                               !r.Rows.Zip(r.After).All(p => p.First.Count == p.Second.Count &&
                                   p.First.All(kv => p.Second.TryGetValue(kv.Key, out var v) && Equals(kv.Value, v))));

        private static bool SameClaims(List<DeckUsage> a, List<DeckUsage> b)
        {
            static string Sig(DeckUsage u) =>
                $"{u.DeckUsageId}|{u.ScryfallId}|{u.CollectionTable}|{CardLanguage.Normalize(u.Language)}|" +
                $"{CardCondition.Normalize(u.Condition)}|{u.EnteredNonFoil}|{u.EnteredFoil}|{u.EnteredEtched}";
            return a.Select(Sig).OrderBy(s => s).SequenceEqual(b.Select(Sig).OrderBy(s => s));
        }

        /// <summary>
        /// Undo: the deck's claims and the collection rows back as they were —
        /// all or nothing. Refused when any of them changed since the edit.
        /// Claims come back with their own ids, so older Undo steps still match.
        /// </summary>
        public static EditResult RestoreClaims(ClaimSnapshot snap, string description,
                                               IReadOnlyList<EditSnapshot>? extra = null)
        {
            extra ??= Array.Empty<EditSnapshot>();
            try
            {
                // Check everything first, so nothing is half put back.
                using (var check = new CollectionDbContext())
                {
                    if (snap.ClaimsAfter == null || !SameClaims(ClaimsOf(check, snap.DeckId, snap.Sids), snap.ClaimsAfter))
                        return Fail(snap.DeckId == TradeBinderId
                            ? $"Can't undo \"{description}\": the Trade Binder's copies of those cards changed since."
                            : $"Can't undo \"{description}\": the deck's collection copies changed since.");
                }
                if (snap.Rows.Any(r => !CanRestore(r)))
                    return Fail($"Can't undo \"{description}\": those cards have changed in the collection since.");
                // Other tables in the same step (the Want List): checked too, restored in the same save.
                if (extra.Any(r => !CanRestore(r)))
                    return Fail($"Can't undo \"{description}\": those cards have changed on the Want List since.");

                EnsureBackup();
                using var db = new CollectionDbContext();
                // Rows and claims go back in ONE save (one SQLite transaction:
                // all or nothing). No explicit transaction around it: the
                // context's save runs a WAL checkpoint, which can't run inside
                // one and made SQLite retry for 30 seconds per save (a freeze).
                foreach (var rows in snap.Rows) RestoreInto(db, rows);
                foreach (var rows in extra) RestoreInto(db, rows);
                // The deck's claims as they were: same ids, same values; newer ones removed.
                var current = db.DeckUsages.Where(u => u.DeckId == snap.DeckId).ToList()
                    .Where(u => snap.Sids == null || snap.Sids.Contains(u.ScryfallId)).ToList();
                var byId = current.ToDictionary(u => u.DeckUsageId);
                var keep = new HashSet<int>(snap.Claims.Select(u => u.DeckUsageId));
                foreach (var u in current.Where(u => !keep.Contains(u.DeckUsageId)))
                    db.DeckUsages.Remove(u);
                foreach (var old in snap.Claims)
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
                db.SaveChanges();                     // rows + claims together

                // Used counts of every card involved, before and after the edit.
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                var printings = snap.Claims.Concat(snap.ClaimsAfter ?? new List<DeckUsage>())
                    .Select(u => (Table: TableOf(u), Sid: u.ScryfallId))
                    .Concat(snap.Rows.SelectMany(r => r.Printings.Select(s => (Table: r.Table, Sid: s))))
                    .Distinct();
                foreach (var t in printings.GroupBy(p => p.Table))
                    Recompute(db, t.Key, t.Select(p => p.Sid), cache);
                db.SaveChanges();
                return new EditResult { Changed = 1, Message = $"Undone: {description}" };
            }
            catch (Exception ex)
            {
                return Fail($"Could not undo: {ex.Message}");
            }
        }
    }
}