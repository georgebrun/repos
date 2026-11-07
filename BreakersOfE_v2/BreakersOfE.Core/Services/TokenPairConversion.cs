using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// The one-time move from the old two-sided pairs (two side rows linked by
    /// <see cref="TokenPair"/> records) to two-sided cards with rows of their
    /// own ("Officer // Treasure"). Each pair's copies come off its two side
    /// rows and go on the card's row; each deck's claims on those copies, and
    /// its token lines, move to the card. The collection database and the
    /// decks it changes are backed up first. Runs once: afterwards there are no
    /// TokenPair records left.
    /// </summary>
    public static partial class CollectionEditService
    {

        /// <summary>
        /// Convert the old pairs (if there are any). Returns what was done for
        /// the status line, or null when there was nothing to convert.
        /// </summary>
        public static string? ConvertTokenPairs()
        {
            List<TokenPair> pairs;
            try
            {
                using var check = new CollectionDbContext();
                pairs = check.TokenPairs.AsNoTracking().ToList();
            }
            catch { return null; }                                   // an older collection.db without the table
            if (pairs.Count == 0) return null;

            string backupFolder = Path.Combine(AppFolderService.BackupsFolder,
                                               "Before two-sided cards " + DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
            Directory.CreateDirectory(backupFolder);
            try { BackupCollectionTo(backupFolder); } catch (Exception ex) { return $"Two-sided tokens weren't converted: the backup failed ({ex.Message})."; }

            using var db = new CollectionDbContext();
            var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
            var st = LoadOldPairState(db, cache);
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Deck id → its token lines to change: (printing, finish, copies) off, and cards on.
            var off = new Dictionary<string, List<(string Sid, string Finish, int N)>>(StringComparer.Ordinal);
            var on = new Dictionary<string, List<(TokenCard Card, string Finish, int N)>>(StringComparer.Ordinal);
            int cards = 0;

            var pool = TokenPairing.PoolCards(st.Pairs.SelectMany(p => new[] { p.ASid, p.BSid }));
            foreach (var p in st.Pairs)
            {
                int n = st.Eff.GetValueOrDefault(p.TokenPairId);
                if (n <= 0) continue;
                RowKey ka = OldKeyA(p), kb = OldKeyB(p);
                var rowsA = Rows(db, TokensTable, ka.ScryfallId).Where(r => KeyOf(r, false) == ka && r.Quantity > 0).ToList();
                var rowsB = Rows(db, TokensTable, kb.ScryfallId).Where(r => KeyOf(r, false) == kb && r.Quantity > 0).ToList();
                if (rowsA.Count == 0 || rowsB.Count == 0) continue;
                var ta = pool.GetValueOrDefault(ka.ScryfallId) ?? (rowsA[0].Entity is TokenCollectionEntry ea ? TokenPairing.FromRow(ea) : null);
                var tb = pool.GetValueOrDefault(kb.ScryfallId) ?? (rowsB[0].Entity is TokenCollectionEntry eb ? TokenPairing.FromRow(eb) : null);
                if (ta == null || tb == null) continue;
                var card = TokenPairing.Compose(ta, tb);
                // The card's finish, language and condition: its first side's.
                var first = TokenPairing.Sides(card.ScryfallId)!.Value.A.Equals(ka.ScryfallId, StringComparison.OrdinalIgnoreCase) ? ka : kb;
                var key = RowKey.Of(card.ScryfallId, first.Finish, first.Language, first.Condition);

                var row = FindExact(Rows(db, TokensTable, card.ScryfallId), key, false);
                if (row == null)
                {
                    row = NewRow(TokensTable, card);
                    row.Finish = key.Finish;
                    row.Language = key.Language;
                    row.Condition = key.Condition;
                    row.Quantity = 0;
                    row.DateAdded = p.DateAdded;
                    row.DateModified = DateTime.Now;
                    db.Add(row.Entity);
                }
                row.Quantity += n;
                foreach (var side in new[] { rowsA, rowsB })
                {
                    int left = n;
                    foreach (var r in side)
                    {
                        int take = Math.Min(left, r.Quantity);
                        r.Quantity -= take;
                        r.DateModified = DateTime.Now;
                        left -= take;
                        if (left <= 0) break;
                    }
                }
                touched.Add(ka.ScryfallId); touched.Add(kb.ScryfallId); touched.Add(card.ScryfallId);
                cards += n;

                // Each deck's claims on this pair's copies move to the card (one card, however many sides the deck held).
                st.Alloc.TryGetValue((ka, p.TokenPairId), out var allocA);
                st.Alloc.TryGetValue((kb, p.TokenPairId), out var allocB);
                allocA ??= new Dictionary<string, int>();
                allocB ??= new Dictionary<string, int>();
                foreach (var deck in allocA.Keys.Union(allocB.Keys).ToList())
                {
                    int a = allocA.GetValueOrDefault(deck), b = allocB.GetValueOrDefault(deck);
                    int have = Math.Max(a, b);
                    if (have <= 0) continue;
                    foreach (var (k, m) in new[] { (ka, a), (kb, b) })
                    {
                        if (m <= 0) continue;
                        db.DeckUsages.Where(u => u.DeckId == deck && u.ScryfallId == k.ScryfallId).Load();
                        int left = m;
                        foreach (var u in db.DeckUsages.Local.Where(u => u.DeckId == deck && Matches(u, TokensTable, k)).ToList())
                        {
                            int c = ClaimOf(u, k.Finish, false);
                            int take = Math.Min(left, c);
                            SetClaim(u, k.Finish, c - take);
                            left -= take;
                            if (u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched <= 0) db.DeckUsages.Remove(u);
                            if (left <= 0) break;
                        }
                        if (!off.TryGetValue(deck, out var lo)) off[deck] = lo = new();
                        lo.Add((k.ScryfallId, k.Finish, m));
                    }
                    var cu = GetOrAddClaim(db, deck, TokensTable, key);
                    SetClaim(cu, key.Finish, ClaimOf(cu, key.Finish, false) + have);
                    cu.DeckName = st.DeckNames.GetValueOrDefault(deck, cu.DeckName);
                    cu.DateRecorded = DateTime.Now;
                    if (!on.TryGetValue(deck, out var ln)) on[deck] = ln = new();
                    ln.Add((card, key.Finish, have));
                }
            }
            db.SaveChanges();

            // Side rows left with no copies go; the old pairs go.
            foreach (var sid in touched.ToList())
                foreach (var r in Rows(db, TokensTable, sid).Where(r => r.Quantity <= 0).ToList())
                    db.Remove(r.Entity);
            db.TokenPairs.RemoveRange(db.TokenPairs.ToList());
            db.SaveChanges();
            Recompute(db, TokensTable, touched, cache);
            db.SaveChanges();

            // The decks: their token lines follow the claims.
            int decks = 0;
            var notes = new List<string>();
            foreach (var (path, deck) in DeckIndexService.AllDecks())
            {
                if (string.IsNullOrEmpty(deck.DeckId)) continue;
                bool hasOff = off.TryGetValue(deck.DeckId, out var lo);
                bool hasOn = on.TryGetValue(deck.DeckId, out var ln);
                if (!hasOff && !hasOn) continue;
                try
                {
                    File.Copy(path, Path.Combine(backupFolder, Path.GetFileName(path)), overwrite: true);
                    foreach (var (sid, finish, n) in lo ?? new())
                    {
                        int left = n;
                        foreach (var line in deck.Cards.Where(c => c.IsTokenLine && string.Equals(c.ScryfallId, sid, StringComparison.OrdinalIgnoreCase)).ToList())
                        {
                            int have = FinishQty(line, finish);
                            int take = Math.Min(left, have);
                            SetFinishQty(line, finish, have - take);
                            left -= take;
                            if (line.TotalQuantity <= 0) deck.Cards.Remove(line);
                            if (left <= 0) break;
                        }
                    }
                    foreach (var (card, finish, n) in ln ?? new())
                    {
                        var line = deck.Cards.FirstOrDefault(c => c.IsTokenLine && string.Equals(c.ScryfallId, card.ScryfallId, StringComparison.OrdinalIgnoreCase));
                        if (line == null)
                        {
                            line = TokenPairing.ToDeckCard(card);
                            line.Category = DeckCardCategory.Tokens;
                            line.Quantity = line.FoilQuantity = line.EtchedQuantity = 0;
                            deck.Cards.Add(line);
                        }
                        SetFinishQty(line, finish, FinishQty(line, finish) + n);
                    }
                    deck.FilePath = path;
                    DeckService.Save(deck);
                    decks++;
                }
                catch (Exception ex)
                {
                    notes.Add($"{deck.Name}: {ex.Message}");
                }
            }
            PairsChanged();
            return $"Two-sided tokens: {cards} card{(cards == 1 ? "" : "s")} moved to rows of their own" +
                   (decks > 0 ? $", {decks} deck{(decks == 1 ? "" : "s")} updated" : "") +
                   $". A backup is in {backupFolder}." +
                   (notes.Count > 0 ? "  Some decks couldn't be saved: " + string.Join("; ", notes.Take(3)) : "");
        }

        /// <summary>A copy of collection.db in this folder (the conversion's own backup, never pruned).</summary>
        private static void BackupCollectionTo(string folder)
        {
            string source = AppFolderService.CollectionDatabasePath;
            if (!File.Exists(source)) return;
            string target = Path.Combine(folder, "collection.db");
            using var src = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False");
            using var dst = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={target};Pooling=False");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }

        private static int FinishQty(DeckCard line, string finish) => CardFinish.Normalize(finish) switch
        {
            CardFinish.Foil => line.FoilQuantity,
            CardFinish.Etched => line.EtchedQuantity,
            _ => line.Quantity,
        };

        private static void SetFinishQty(DeckCard line, string finish, int n)
        {
            n = Math.Max(0, n);
            switch (CardFinish.Normalize(finish))
            {
                case CardFinish.Foil: line.FoilQuantity = n; break;
                case CardFinish.Etched: line.EtchedQuantity = n; break;
                default: line.Quantity = n; break;
            }
        }

        // ── The old pairs, read as they were (the conversion places claims as the old model did) ──
        private static RowKey OldKeyA(TokenPair p) => RowKey.Of(p.ASid, p.AFinish, p.ALanguage, p.ACondition);
        private static RowKey OldKeyB(TokenPair p) => RowKey.Of(p.BSid, p.BFinish, p.BLanguage, p.BCondition);

        /// <summary>The pairs, their copies in effect, and how claims fall on them.</summary>
        private sealed class OldPairState
        {
            public List<TokenPair> Pairs = new();
            public Dictionary<int, int> Eff = new();
            public Dictionary<RowKey, List<TokenPair>> ByKey = new();
            public Dictionary<RowKey, int> Qty = new();
            /// <summary>Claims per deck on each paired row.</summary>
            public Dictionary<RowKey, Dictionary<string, int>> Own = new();
            /// <summary>(row, pair) → deck → that row's claims placed on the pair's copies.</summary>
            public Dictionary<(RowKey, int), Dictionary<string, int>> Alloc = new();
            public Dictionary<string, string> DeckNames = new(StringComparer.Ordinal);

            public RowKey Other(TokenPair p, RowKey k) => OldKeyA(p) == k ? OldKeyB(p) : OldKeyA(p);
            public string OtherName(TokenPair p, RowKey k) => OldKeyA(p) == k ? p.BName : p.AName;

            public IEnumerable<TokenPair> PairsOf(RowKey k) =>
                ByKey.TryGetValue(k, out var l) ? l : Enumerable.Empty<TokenPair>();

            private Dictionary<string, int> OwnOf(RowKey k) =>
                Own.TryGetValue(k, out var d) ? d : new Dictionary<string, int>();

            /// <summary>Deck → copies of this row's card used through its other sides.</summary>
            public Dictionary<string, int> PartnerUse(RowKey k)
            {
                var r = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var p in PairsOf(k))
                    if (Alloc.TryGetValue((Other(p, k), p.TokenPairId), out var a))
                        foreach (var (deck, n) in a)
                            r[deck] = r.GetValueOrDefault(deck) + n;
                return r;
            }

            /// <summary>
            /// Copies of the row in use: per deck the larger of its own claims
            /// and its claims of the other sides (a deck holding both sides of
            /// one card uses one card).
            /// </summary>
            public int Used(RowKey k)
            {
                var own = OwnOf(k);
                var partner = PartnerUse(k);
                return own.Keys.Union(partner.Keys)
                    .Sum(d => Math.Max(own.GetValueOrDefault(d), partner.GetValueOrDefault(d)));
            }

            /// <summary>Claiming this row for a deck that already holds its other side: copies that cost nothing more.</summary>
            public int Headroom(RowKey k, string deckId) =>
                Math.Max(0, PartnerUse(k).GetValueOrDefault(deckId) - OwnOf(k).GetValueOrDefault(deckId));

            /// <summary>
            /// Place each paired row's claims: copies that aren't on a pair
            /// first (decks that don't also hold the other side go there
            /// first), then on the pairs — the pair whose other side the
            /// deck holds first.
            /// </summary>
            public void Allocate()
            {
                foreach (var (k, pairs) in ByKey)
                {
                    var own = OwnOf(k);
                    if (own.Count == 0) continue;
                    int single = Math.Max(0, Qty.GetValueOrDefault(k) - pairs.Sum(p => Eff.GetValueOrDefault(p.TokenPairId)));
                    bool HoldsOther(string d, TokenPair p) => OwnOf(Other(p, k)).GetValueOrDefault(d) > 0;
                    var left = pairs.ToDictionary(p => p.TokenPairId, p => Eff.GetValueOrDefault(p.TokenPairId));
                    foreach (var (deck, claims) in own.OrderBy(x => pairs.Any(p => HoldsOther(x.Key, p)) ? 1 : 0)
                                                      .ThenBy(x => x.Key, StringComparer.Ordinal))
                    {
                        int n = claims;
                        int s = Math.Min(n, single);
                        single -= s; n -= s;
                        foreach (var p in pairs.OrderBy(p => HoldsOther(deck, p) ? 0 : 1).ThenBy(p => p.TokenPairId))
                        {
                            if (n <= 0) break;
                            int take = Math.Min(n, left[p.TokenPairId]);
                            if (take <= 0) continue;
                            left[p.TokenPairId] -= take;
                            n -= take;
                            var key = (k, p.TokenPairId);
                            if (!Alloc.TryGetValue(key, out var a)) Alloc[key] = a = new Dictionary<string, int>(StringComparer.Ordinal);
                            a[deck] = a.GetValueOrDefault(deck) + take;
                        }
                    }
                }
            }
        }

        /// <summary>Every pair, with copies in effect and the claims on its rows (inside an open context).</summary>
        private static OldPairState LoadOldPairState(CollectionDbContext db, Dictionary<string, bool> cache)
        {
            var st = new OldPairState();
            try { st.Pairs = db.TokenPairs.AsNoTracking().OrderBy(p => p.TokenPairId).ToList(); }
            catch { return st; }                                // an older collection.db without the table
            if (st.Pairs.Count == 0) return st;

            var sids = st.Pairs.SelectMany(p => new[] { p.ASid, p.BSid }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var rows = RowsMany(db, TokensTable, sids);
            foreach (var r in rows)
            {
                var k = KeyOf(r, EtchedOnlyPrinting(TokensTable, r.ScryfallId, cache));
                st.Qty[k] = st.Qty.GetValueOrDefault(k) + Math.Max(0, r.Quantity);
            }

            // Copies in effect: in pair order, never more than either row has left.
            var left = new Dictionary<RowKey, int>(st.Qty);
            foreach (var p in st.Pairs)
            {
                RowKey a = OldKeyA(p), b = OldKeyB(p);
                int n = a == b ? 0 : Math.Max(0, Math.Min(p.Count, Math.Min(left.GetValueOrDefault(a), left.GetValueOrDefault(b))));
                st.Eff[p.TokenPairId] = n;
                if (n > 0) { left[a] -= n; left[b] -= n; }
                foreach (var k in new[] { a, b })
                {
                    if (!st.ByKey.TryGetValue(k, out var l)) st.ByKey[k] = l = new List<TokenPair>();
                    if (!l.Contains(p)) l.Add(p);
                }
            }

            // Claims on the paired rows, per deck.
            var claims = new List<DeckUsage>();
            for (int i = 0; i < sids.Count; i += 400)
            {
                var chunk = sids.Skip(i).Take(400).ToList();
                claims.AddRange(db.DeckUsages.Where(u => chunk.Contains(u.ScryfallId)).ToList());
            }
            foreach (var k in st.ByKey.Keys)
            {
                bool eo = EtchedOnlyPrinting(TokensTable, k.ScryfallId, cache);
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var u in claims.Where(u => Matches(u, TokensTable, k)))
                {
                    int n = ClaimOf(u, k.Finish, eo);
                    if (n <= 0) continue;
                    d[u.DeckId] = d.GetValueOrDefault(u.DeckId) + n;
                    if (u.DeckName.Length > 0) st.DeckNames[u.DeckId] = u.DeckName;
                }
                if (d.Count > 0) st.Own[k] = d;
            }
            st.Allocate();
            return st;
        }


    }
}
