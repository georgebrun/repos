using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>One side of a pair, as a token row shows it ("Angel", × copies).</summary>
    public sealed class OtherSide
    {
        public int PairId { get; init; }
        public RowKey Key { get; init; } = RowKey.Of("", null, null, null);
        public string Name { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string CollectorNumber { get; init; } = "";
        /// <summary>Copies of the pair in effect (never more than either row has).</summary>
        public int Count { get; init; }
        /// <summary>"Angel (TFRA #3)".</summary>
        public string Text => $"{Name} ({SetCode.ToUpperInvariant()} #{CollectorNumber})";
    }

    /// <summary>A token of the card pool, as the Two-Sided window lists it: what you have of it.</summary>
    public sealed class TokenChoice
    {
        public TokenCard Card { get; init; } = new();
        public string Name => Card.Name;
        public string SetCode => Card.SetCode.ToUpperInvariant();
        public string Number => Card.CollectorNumber;
        public double NumberSort => double.TryParse(new string(Card.CollectorNumber.TakeWhile(char.IsDigit).ToArray()), out var d) ? d : 9999;
        public string PT => CardFaces.PowerToughness(Card.Power, Card.Toughness);
        /// <summary>Copies in Collection → Tokens (any finish, language, condition).</summary>
        public int Owned { get; init; }
        /// <summary>Of those, not on a pair.</summary>
        public int OffPair { get; init; }
        /// <summary>Of those, claimed by decks (either side).</summary>
        public int InUse { get; init; }
        /// <summary>"Treasure, Goblin": what your copies are paired with.</summary>
        public string PairedWith { get; init; } = "";
        public string OwnedText => Owned == 0 ? "" : Owned.ToString();
        public string OffPairText => Owned == 0 ? "" : OffPair.ToString();
        public string InUseText => InUse == 0 ? "" : InUse.ToString();
    }

    /// <summary>
    /// Two-sided tokens: two Collection → Tokens rows that are the two sides
    /// of one physical card (<see cref="TokenPair"/>, per copy).
    ///
    ///  • Each side keeps its own row; the row shows the other side
    ///    ("Treasure // Angel").
    ///  • A deck claiming one side takes the card: the other row counts that
    ///    copy as used (Recompute folds it into UsedCount, so everything that
    ///    reads Used / Available follows). The same deck may hold both sides
    ///    of one card (one side out at a time).
    ///  • A pair's copies in effect never exceed either row's quantity, read
    ///    in pair order; pairs aren't deleted when copies go (Undo brings the
    ///    copies — and the pair — back). Unpair removes one.
    /// </summary>
    public static partial class CollectionEditService
    {
        private static RowKey KeyA(TokenPair p) => RowKey.Of(p.ASid, p.AFinish, p.ALanguage, p.ACondition);
        private static RowKey KeyB(TokenPair p) => RowKey.Of(p.BSid, p.BFinish, p.BLanguage, p.BCondition);

        /// <summary>The pairs, their copies in effect, and how claims fall on them.</summary>
        private sealed class PairState
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

            public RowKey Other(TokenPair p, RowKey k) => KeyA(p) == k ? KeyB(p) : KeyA(p);
            public string OtherName(TokenPair p, RowKey k) => KeyA(p) == k ? p.BName : p.AName;

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
        private static PairState LoadPairState(CollectionDbContext db, Dictionary<string, bool> cache)
        {
            var st = new PairState();
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
                RowKey a = KeyA(p), b = KeyB(p);
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

        /// <summary>The printings paired with any of these (to recompute their Used too).</summary>
        private static List<string> WithPartners(PairState st, IEnumerable<string> sids)
        {
            var set = new HashSet<string>(sids.Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);
            foreach (var p in st.Pairs)
            {
                if (set.Contains(p.ASid)) set.Add(p.BSid);
                else if (set.Contains(p.BSid)) set.Add(p.ASid);
            }
            return set.ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        // DISPLAY: "Treasure // Angel" (cached; reloaded after changes)
        // ══════════════════════════════════════════════════════════════════
        private static readonly object _pairLock = new();
        private static Dictionary<RowKey, List<OtherSide>>? _sidesByKey;
        private static Dictionary<string, List<OtherSide>>? _sidesBySid;

        /// <summary>Pairs or token rows changed: the "// other side" text is read again next time.</summary>
        public static void PairsChanged()
        {
            lock (_pairLock) { _sidesByKey = null; _sidesBySid = null; }
        }

        private static void EnsureSides()
        {
            lock (_pairLock)
            {
                if (_sidesByKey != null) return;
                var byKey = new Dictionary<RowKey, List<OtherSide>>();
                var bySid = new Dictionary<string, List<OtherSide>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var db = new CollectionDbContext();
                    var st = LoadPairState(db, new Dictionary<string, bool>(StringComparer.Ordinal));
                    foreach (var p in st.Pairs)
                    {
                        int n = st.Eff.GetValueOrDefault(p.TokenPairId);
                        if (n <= 0) continue;
                        void Add(RowKey self, RowKey other, string name, string set, string no)
                        {
                            var side = new OtherSide { PairId = p.TokenPairId, Key = other, Name = name, SetCode = set, CollectorNumber = no, Count = n };
                            if (!byKey.TryGetValue(self, out var l)) byKey[self] = l = new List<OtherSide>();
                            l.Add(side);
                            if (!bySid.TryGetValue(self.ScryfallId, out var s)) bySid[self.ScryfallId] = s = new List<OtherSide>();
                            if (!s.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) s.Add(side);
                        }
                        Add(KeyA(p), KeyB(p), p.BName, p.BSet, p.BNumber);
                        Add(KeyB(p), KeyA(p), p.AName, p.ASet, p.ANumber);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Token pairs: {ex.Message}");
                }
                _sidesByKey = byKey;
                _sidesBySid = bySid;
            }
        }

        /// <summary>The other sides of exactly this token row (empty: single-sided).</summary>
        public static List<OtherSide> OtherSidesOf(RowKey key)
        {
            EnsureSides();
            lock (_pairLock)
                return _sidesByKey!.TryGetValue(key, out var l) ? l.ToList() : new List<OtherSide>();
        }

        /// <summary>The other sides of any of your rows of this token printing (a deck's token line).</summary>
        public static List<OtherSide> OtherSidesOfPrinting(string sid)
        {
            EnsureSides();
            lock (_pairLock)
                return _sidesBySid!.TryGetValue(sid ?? "", out var l) ? l.ToList() : new List<OtherSide>();
        }

        /// <summary>The other sides of a collection token row or a deck's token line.</summary>
        public static List<OtherSide> OtherSidesOfRow(object row) => row switch
        {
            TokenCollectionEntry e => OtherSidesOf(RowKey.Of(e.ScryfallId, e.Finish, e.Language, e.Condition)),
            DeckCard d when d.IsTokenLine => OtherSidesOfPrinting(d.ScryfallId),
            _ => new List<OtherSide>(),
        };

        /// <summary>
        /// The Name column: "Treasure // Angel" for a paired token row (or deck
        /// token line), "Treasure // Angel ×1, Goblin ×1" with several pairs or
        /// single copies left over; anything else its own name.
        /// </summary>
        public static string NameShown(object? row)
        {
            if (row == null) return "";
            string name = GetString(row, "Name");
            if (row is not TokenCollectionEntry && !(row is DeckCard d && d.IsTokenLine)) return name;
            List<OtherSide> sides;
            try { sides = OtherSidesOfRow(row); }
            catch { return name; }
            if (sides.Count == 0) return name;
            int qty = row is TokenCollectionEntry t ? t.Quantity : 0;
            bool counts = row is TokenCollectionEntry && (sides.Count > 1 || sides[0].Count < qty);
            return name + " // " + string.Join(", ", sides.Select(s => counts ? $"{s.Name} ×{s.Count}" : s.Name));
        }

        /// <summary>
        /// Physical cards among these token rows: copies, less one for each
        /// pair whose two sides are both in the list (Totals: "19 rows · 10 cards").
        /// </summary>
        public static int PhysicalCards(IEnumerable<object> rows)
        {
            var list = rows.OfType<TokenCollectionEntry>().ToList();
            int copies = list.Sum(r => Math.Max(0, r.Quantity));
            var keys = new HashSet<RowKey>(list.Select(r => RowKey.Of(r.ScryfallId, r.Finish, r.Language, r.Condition)));
            int both = 0;
            var seen = new HashSet<int>();
            foreach (var k in keys)
                foreach (var s in OtherSidesOf(k))
                    if (keys.Contains(s.Key) && seen.Add(s.PairId)) both += s.Count;
            return copies - both;
        }

        // ══════════════════════════════════════════════════════════════════
        // PAIR / UNPAIR
        // ══════════════════════════════════════════════════════════════════
        /// <summary>A token row's key (exact finish, language, condition).</summary>
        public static RowKey TokenKey(object row) =>
            RowKey.Of(GetString(row, "ScryfallId"), GetString(row, "Finish"), GetString(row, "Language"), GetString(row, "Condition"));

        /// <summary>"Angel 3/3 (TFRA #3)".</summary>
        private static string SideText(object row)
        {
            string pt = GetString(row, "Power").Length > 0 || GetString(row, "Toughness").Length > 0
                ? $" {GetString(row, "Power")}/{GetString(row, "Toughness")}" : "";
            return $"{GetString(row, "Name")}{pt} ({GetString(row, "SetCode").ToUpperInvariant()} #{GetString(row, "CollectorNumber")})";
        }

        /// <summary>Copies of this token row not yet on a pair.</summary>
        public static int UnpairedCopies(object row)
        {
            var key = TokenKey(row);
            int qty = GetInt(row, "Quantity") ?? 0;
            return Math.Max(0, qty - OtherSidesOf(key).Sum(s => s.Count));
        }

        /// <summary>Real double-faced tokens are one card already: never paired.</summary>
        public static bool IsRealDoubleFaced(object row) =>
            string.Equals(GetString(row, "Layout"), "double_faced_token", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Pair <paramref name="count"/> copies of two token rows as the two
        /// sides of one card. Refused when either row hasn't that many copies
        /// off a pair, or when decks already hold both sides as separate cards
        /// (free one first).
        /// </summary>
        public static EditResult Pair(object rowA, object rowB, int count)
        {
            if (rowA is not TokenCollectionEntry a || rowB is not TokenCollectionEntry b)
                return Fail("Only rows of Collection → Tokens can be paired.");
            var ka = TokenKey(a); var kb = TokenKey(b);
            if (ka == kb) return Fail("A row can't be its own other side.");
            if (IsRealDoubleFaced(a) || IsRealDoubleFaced(b))
                return Fail($"{(IsRealDoubleFaced(a) ? a.Name : b.Name)} is already one double-faced card in Scryfall: it needs no other side.");
            if (count <= 0) return Fail("Pair at least one copy.");
            int fa = UnpairedCopies(a), fb = UnpairedCopies(b);
            if (count > Math.Min(fa, fb))
                return Fail($"Only {Math.Min(fa, fb)} cop{(Math.Min(fa, fb) == 1 ? "y" : "ies")} can be paired: " +
                            $"{SideText(a)} has {fa} off a pair, {SideText(b)} has {fb}.");
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                // Same two rows already paired: add to that pair.
                var p = db.TokenPairs.AsEnumerable().FirstOrDefault(x =>
                    (KeyA(x) == ka && KeyB(x) == kb) || (KeyA(x) == kb && KeyB(x) == ka));
                bool added = p == null;
                if (p == null)
                {
                    p = new TokenPair
                    {
                        ASid = ka.ScryfallId, AFinish = ka.Finish, ALanguage = ka.Language, ACondition = ka.Condition,
                        AName = a.Name, ASet = a.SetCode, ANumber = a.CollectorNumber,
                        BSid = kb.ScryfallId, BFinish = kb.Finish, BLanguage = kb.Language, BCondition = kb.Condition,
                        BName = b.Name, BSet = b.SetCode, BNumber = b.CollectorNumber,
                        Count = count, DateAdded = DateTime.Now,
                    };
                    db.TokenPairs.Add(p);
                }
                else
                {
                    // A pair whose copies went (and are back) counts what's in effect, plus these.
                    var before = LoadPairState(db, cache);
                    p.Count = before.Eff.GetValueOrDefault(p.TokenPairId) + count;
                }
                db.SaveChanges();
                Recompute(db, TokensTable, new[] { ka.ScryfallId, kb.ScryfallId }, cache);
                db.SaveChanges();

                // Decks holding both sides as two cards: one card can't be both.
                var over = RowsMany(db, TokensTable, new[] { ka.ScryfallId, kb.ScryfallId })
                    .Where(r => r.UsedCount > r.Quantity && (KeyOf(r, false) == ka || KeyOf(r, false) == kb)).ToList();
                if (over.Count > 0)
                {
                    string who = UsedBy(db, TokensTable, ka.ScryfallId) + "; " + UsedBy(db, TokensTable, kb.ScryfallId);
                    if (added) db.TokenPairs.Remove(p); else p.Count -= count;
                    db.SaveChanges();
                    Recompute(db, TokensTable, new[] { ka.ScryfallId, kb.ScryfallId }, cache);
                    db.SaveChanges();
                    PairsChanged();
                    return Fail($"Not paired: decks hold {a.Name} and {b.Name} as two cards ({who}). " +
                                "Free one of them first, then pair them.");
                }
                PairsChanged();
                return new EditResult
                {
                    Changed = count,
                    Message = $"Paired {count} × {a.Name} // {b.Name}: one card, two sides.",
                    Touched = { ka, kb },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not pair them: {ex.Message}");
            }
        }

        /// <summary>Take <paramref name="count"/> copies off a pair (all of them: the pair goes).</summary>
        public static EditResult Unpair(int pairId, int count = int.MaxValue)
        {
            try
            {
                EnsureBackup();
                using var db = new CollectionDbContext();
                var p = db.TokenPairs.FirstOrDefault(x => x.TokenPairId == pairId);
                if (p == null) return Fail("That pair isn't there any more.");
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                int eff = LoadPairState(db, cache).Eff.GetValueOrDefault(pairId);
                int n = Math.Min(count, Math.Max(eff, 1));
                if (n >= eff) db.TokenPairs.Remove(p);
                else p.Count = eff - n;
                db.SaveChanges();
                Recompute(db, TokensTable, new[] { p.ASid, p.BSid }, cache);
                db.SaveChanges();
                PairsChanged();
                return new EditResult
                {
                    Changed = Math.Max(1, n),
                    Message = $"Unpaired {(n >= eff ? "" : n + " × ")}{p.AName} // {p.BName}: each side is its own card again.",
                    Touched = { KeyA(p), KeyB(p) },
                };
            }
            catch (Exception ex)
            {
                return Fail($"Could not unpair them: {ex.Message}");
            }
        }

        /// <summary>
        /// After token rows changed (copies added, removed, moved): Used is
        /// worked out again for them and their other sides, since a pair's
        /// copies in effect follow the rows.
        /// </summary>
        public static void RefreshPairs(IEnumerable<string> sids)
        {
            try
            {
                using var db = new CollectionDbContext();
                if (!db.TokenPairs.Any()) { PairsChanged(); return; }
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                TrimPairs(db, cache);
                Recompute(db, TokensTable, sids.ToList(), cache);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token pairs refresh: {ex.Message}");
            }
            PairsChanged();
        }

        /// <summary>
        /// A pair whose copies went (Undo of the add, removed, a lower
        /// quantity) goes with them, or keeps only the copies still there —
        /// adding the same tokens again never pairs them by itself.
        /// </summary>
        private static void TrimPairs(CollectionDbContext db, Dictionary<string, bool> cache)
        {
            var st = LoadPairState(db, cache);
            bool changed = false;
            foreach (var p in db.TokenPairs.ToList())
            {
                int eff = st.Eff.GetValueOrDefault(p.TokenPairId);
                if (eff >= p.Count) continue;
                if (eff <= 0) db.TokenPairs.Remove(p);
                else p.Count = eff;
                changed = true;
            }
            if (changed) db.SaveChanges();
        }

        /// <summary>
        /// Collection → Tokens opened: pairs whose copies went some other way
        /// (an import, a deck page, Edit → Sets) are tidied up, and Used worked
        /// out again for the tokens they were on.
        /// </summary>
        public static void TidyPairs()
        {
            try
            {
                using var db = new CollectionDbContext();
                if (!db.TokenPairs.Any()) { PairsChanged(); return; }
                var sids = db.TokenPairs.AsNoTracking().ToList().SelectMany(p => new[] { p.ASid, p.BSid }).ToList();
                var cache = new Dictionary<string, bool>(StringComparer.Ordinal);
                TrimPairs(db, cache);
                Recompute(db, TokensTable, sids, cache);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token pairs tidy: {ex.Message}");
            }
            PairsChanged();
        }

        /// <summary>The pairs any of these printings are on, as stored now (Undo's snapshot).</summary>
        internal static List<TokenPair> PairsOfPrintings(IEnumerable<string> sids)
        {
            var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new CollectionDbContext();
                return db.TokenPairs.AsNoTracking().ToList()
                    .Where(p => set.Contains(p.ASid) || set.Contains(p.BSid)).ToList();
            }
            catch { return new List<TokenPair>(); }
        }

        /// <summary>
        /// Undo: the pairs of these printings put back as the snapshot had
        /// them (pairs on other tokens are left alone).
        /// </summary>
        internal static void RestorePairs(CollectionDbContext db, IEnumerable<string> sids, List<TokenPair> before)
        {
            var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
            foreach (var p in db.TokenPairs.ToList().Where(p => set.Contains(p.ASid) || set.Contains(p.BSid)))
                db.TokenPairs.Remove(p);
            foreach (var p in before)
                db.TokenPairs.Add(new TokenPair
                {
                    ASid = p.ASid, AFinish = p.AFinish, ALanguage = p.ALanguage, ACondition = p.ACondition,
                    AName = p.AName, ASet = p.ASet, ANumber = p.ANumber,
                    BSid = p.BSid, BFinish = p.BFinish, BLanguage = p.BLanguage, BCondition = p.BCondition,
                    BName = p.BName, BSet = p.BSet, BNumber = p.BNumber,
                    Count = p.Count, DateAdded = p.DateAdded,
                });
            db.SaveChanges();
            PairsChanged();
        }

        /// <summary>An other side to print: its printing and name.</summary>
        public sealed record OtherSideRef(string ScryfallId, string Name);

        /// <summary>The distinct other sides of a token row, deck token line or pool token (print slips).</summary>
        public static List<OtherSideRef> OtherSideRefs(object card)
        {
            var sides = card is TokenCollectionEntry || card is DeckCard
                ? OtherSidesOfRow(card)
                : OtherSidesOfPrinting(GetString(card, "ScryfallId"));
            return sides.GroupBy(s => s.Key.ScryfallId, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new OtherSideRef(g.Key, g.First().Name)).ToList();
        }

        /// <summary>One pair as it stands: its two rows and the copies in effect.</summary>
        public sealed record PairLink(int Id, RowKey A, RowKey B, string AName, string BName, int Count);

        /// <summary>Every pair with its copies in effect now (to see which pairs an edit broke).</summary>
        public static List<PairLink> PairLinks()
        {
            try
            {
                using var db = new CollectionDbContext();
                var st = LoadPairState(db, new Dictionary<string, bool>(StringComparer.Ordinal));
                return st.Pairs.Select(p => new PairLink(p.TokenPairId, KeyA(p), KeyB(p), p.AName, p.BName,
                                                        st.Eff.GetValueOrDefault(p.TokenPairId))).ToList();
            }
            catch { return new List<PairLink>(); }
        }

        /// <summary>Copies per token row (key) now.</summary>
        public static Dictionary<RowKey, int> TokenQuantities() =>
            TokenRows().GroupBy(TokenKey).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

        /// <summary>Every pair (Export pairs, the Other side window).</summary>
        public static List<TokenPair> AllPairs()
        {
            try
            {
                using var db = new CollectionDbContext();
                return db.TokenPairs.AsNoTracking().OrderBy(p => p.AName).ThenBy(p => p.BName).ToList();
            }
            catch { return new List<TokenPair>(); }
        }

        /// <summary>Your token rows (the Other side window's list, the Back side box).</summary>
        public static List<TokenCollectionEntry> TokenRows()
        {
            try
            {
                using var db = new CollectionDbContext();
                return db.TokenCollectionEntries.AsNoTracking().Where(e => e.Quantity > 0)
                    .OrderBy(e => e.Name).ThenBy(e => e.SetCode).ToList();
            }
            catch { return new List<TokenCollectionEntry>(); }
        }

        /// <summary>A token row by its exact key (after an add: the row the copies went to).</summary>
        public static TokenCollectionEntry? TokenRow(RowKey key) =>
            TokenRows().FirstOrDefault(r => TokenKey(r) == key);

        /// <summary>
        /// Who holds this token row's copies, with the other side's decks:
        /// "used by Deck A; other side (Angel) in Deck B".
        /// </summary>
        private static string TokenUsedBy(CollectionDbContext db, string sid)
        {
            var parts = new List<string>();
            var names = db.DeckUsages.Where(u => u.ScryfallId == sid && u.CollectionTable == TokensTable &&
                                                 (u.EnteredNonFoil + u.EnteredFoil + u.EnteredEtched) > 0)
                .Select(u => u.DeckName).Distinct().ToList();
            if (names.Count > 0) parts.Add("used by " + string.Join(", ", names));
            var st = LoadPairState(db, new Dictionary<string, bool>(StringComparer.Ordinal));
            foreach (var k in st.ByKey.Keys.Where(k => string.Equals(k.ScryfallId, sid, StringComparison.OrdinalIgnoreCase)))
                foreach (var p in st.PairsOf(k))
                    if (st.Alloc.TryGetValue((st.Other(p, k), p.TokenPairId), out var a) && a.Count > 0)
                        parts.Add($"other side ({st.OtherName(p, k)}) in " +
                                  string.Join(", ", a.Keys.Select(d => st.DeckNames.GetValueOrDefault(d, "a deck"))));
            return parts.Count == 0 ? "in use" : string.Join("; ", parts);
        }

        /// <summary>The "Used in" table's lines for a token row's other sides: the decks holding the card by its other side.</summary>
        private static List<RowUse> OtherSideUses(RowKey key)
        {
            var list = new List<RowUse>();
            using var db = new CollectionDbContext();
            var st = LoadPairState(db, new Dictionary<string, bool>(StringComparer.Ordinal));
            var decks = DeckIndexService.AllDecks()
                .Where(d => !string.IsNullOrEmpty(d.Deck.DeckId))
                .GroupBy(d => d.Deck.DeckId).ToDictionary(g => g.Key, g => g.First());
            foreach (var p in st.PairsOf(key))
            {
                if (!st.Alloc.TryGetValue((st.Other(p, key), p.TokenPairId), out var a)) continue;
                foreach (var (deckId, n) in a)
                {
                    if (n <= 0) continue;
                    string name = st.DeckNames.GetValueOrDefault(deckId, "a deck"), type = "", path = "";
                    if (decks.TryGetValue(deckId, out var d))
                    {
                        path = d.Path;
                        name = string.IsNullOrWhiteSpace(d.Deck.Name) ? System.IO.Path.GetFileNameWithoutExtension(d.Path) : d.Deck.Name;
                        type = DeckFormats.For(d.Deck).Name;
                    }
                    list.Add(new RowUse { DeckId = deckId, DeckName = name, DeckType = type,
                                          Part = $"Other side ({st.OtherName(p, key)})", Copies = n, DeckPath = path });
                }
            }
            return list;
        }

        // ══════════════════════════════════════════════════════════════════
        // THE TWO-SIDED WINDOW: pool tokens of the release, and pairing one
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// The first two-sided tokens: Dark Ascension's Human // Wolf (FNM
        /// promo, released 3 February 2012). Commander decks have had them
        /// since 2014, boosters since Modern Horizons (2019). Tokens released
        /// before this never have another side, so adding one doesn't ask.
        /// </summary>
        public const string TwoSidedSince = "2012-02-03";

        /// <summary>
        /// Could this token have another token on its back? Not a real
        /// double-faced token (one card already), and released on or after
        /// <see cref="TwoSidedSince"/> (an unknown date: yes).
        /// </summary>
        public static bool MayHaveOtherSide(object token)
        {
            if (IsRealDoubleFaced(token)) return false;
            string day = GetString(token, "ReleasedAt");
            return day.Length == 0 || string.CompareOrdinal(day, TwoSidedSince) >= 0;
        }

        /// <summary>The token this printing was last paired with (to pick it again when adding more), or null.</summary>
        public static string? LastPartnerOf(string sid) =>
            OtherSidesOfPrinting(sid).Select(s => s.Key.ScryfallId).FirstOrDefault();

        /// <summary>The release a set belongs to (as View → Sets groups them), and its name.</summary>
        public static (string Code, string Name) ReleaseOfSet(string setCode)
        {
            string rel = SetFamilies.ReleaseOf(setCode.ToLowerInvariant());
            string name = SetFamilies.Info(rel)?.Name ?? rel.ToUpperInvariant();
            return (rel, name);
        }

        /// <summary>
        /// The card pool's tokens to pick the other side from: the release's
        /// token sets (all its sub-sets), or every token. Real double-faced
        /// tokens and this row's own printing are left out. Each with what you
        /// own of it, how much is off a pair, in use, and paired with.
        /// </summary>
        public static List<TokenChoice> TokenChoices(TokenCollectionEntry row, bool sameRelease) =>
            TokenChoices(row.ScryfallId, row.SetCode, row.ReleasedAt, sameRelease);

        /// <summary>The same, for a token being added (its printing, set and release day).</summary>
        public static List<TokenChoice> TokenChoices(string sid, string setCode, string releasedAt, bool sameRelease)
        {
            List<TokenCard> pool;
            using (var adb = new AppDbContext())
            {
                IQueryable<TokenCard> q = adb.TokenCards.AsNoTracking();
                if (sameRelease)
                {
                    var codes = SetFamilies.SetsOfRelease(ReleaseOfSet(setCode).Code);
                    if (codes.Count == 0)
                    {
                        // No set list yet (Update Database saves it): the same set code without the token "t", or the same day.
                        string c = setCode.ToLowerInvariant();
                        string bare = c.Length == 4 && c[0] == 't' ? c[1..] : c;
                        codes = new List<string> { c, bare, "t" + bare };
                        string day = releasedAt;
                        q = q.Where(t => codes.Contains(t.SetCode.ToLower()) || (day != "" && t.ReleasedAt == day));
                    }
                    else q = q.Where(t => codes.Contains(t.SetCode.ToLower()));
                }
                pool = q.Where(t => t.Layout != "double_faced_token" && t.ScryfallId != sid).ToList();
            }

            var mine = TokenRows();
            var bySid = mine.GroupBy(r => r.ScryfallId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var list = new List<TokenChoice>();
            foreach (var t in pool)
            {
                bySid.TryGetValue(t.ScryfallId, out var rows);
                rows ??= new List<TokenCollectionEntry>();
                var paired = rows.SelectMany(r => OtherSidesOf(TokenKey(r))).Select(s => s.Name)
                                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                list.Add(new TokenChoice
                {
                    Card = t,
                    Owned = rows.Sum(r => r.Quantity),
                    OffPair = rows.Sum(UnpairedCopies),
                    InUse = rows.Sum(r => Math.Min(r.Quantity, Math.Max(0, r.UsedCount))),
                    PairedWith = string.Join(", ", paired),
                });
            }
            return list;
        }

        /// <summary>
        /// Your row of this printing to pair with: one with enough copies off
        /// a pair, the same finish, language and condition as the row first.
        /// Null when none has enough (copies have to be added first).
        /// </summary>
        public static TokenCollectionEntry? RowToPairWith(TokenCollectionEntry row, string sid, int count)
        {
            var key = TokenKey(row);
            return TokenRows()
                .Where(r => string.Equals(r.ScryfallId, sid, StringComparison.OrdinalIgnoreCase) && TokenKey(r) != key &&
                            UnpairedCopies(r) >= count)
                .OrderBy(r => TokenKey(r) with { ScryfallId = key.ScryfallId } == key ? 0 : 1)
                .ThenBy(r => CardLanguage.Normalize(r.Language) == CardLanguage.Default ? 0 : 1)
                .FirstOrDefault();
        }

        /// <summary>The finish to add the other side in: the row's own when that printing has it, else its first.</summary>
        public static string FinishToAdd(TokenCollectionEntry row, TokenCard other)
        {
            var (nf, f, e) = FinishesOf(TokensTable, other);
            string want = CardFinish.Normalize(row.Finish);
            if (FinishExists(want, nf, f, e)) return want;
            return nf ? CardFinish.NonFoil : f ? CardFinish.Foil : CardFinish.Etched;
        }

        /// <summary>
        /// Pair the row with a pool token: with your copies of it when you
        /// have enough off a pair, else after <paramref name="add"/> puts the
        /// copies in your collection (same language and condition as the row;
        /// the page adds them, so Undo can take them back).
        /// </summary>
        public static EditResult PairWithPrinting(TokenCollectionEntry row, TokenCard other, int count,
                                                  Func<TokenCard, string, int, string, string, EditResult> add)
        {
            if (string.Equals(other.Layout, "double_faced_token", StringComparison.OrdinalIgnoreCase))
                return Fail($"{other.Name} is one double-faced card already: it can't be another token's back.");
            var target = RowToPairWith(row, other.ScryfallId, count);
            string added = "";
            if (target == null)
            {
                string fin = FinishToAdd(row, other);
                var r = add(other, fin, count, row.Language, row.Condition);
                if (r.Changed <= 0) return r;
                added = r.Message + "  ";
                target = RowToPairWith(row, other.ScryfallId, count);
                if (target == null) return Fail(added + "The copies were added, but couldn't be paired.");
            }
            var p = Pair(row, target, count);
            return new EditResult
            {
                Changed = p.Changed,
                Warning = p.Warning || p.Changed <= 0,
                Message = added + p.Message,
                Touched = p.Touched,
            };
        }

        // ══════════════════════════════════════════════════════════════════
        // EXPORT / IMPORT PAIRS (share with a group: by set and collector number)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>A pair read from a file, matched to your tokens.</summary>
        public sealed class ImportedPair
        {
            public string NameA { get; init; } = "";
            public string SetA { get; init; } = "";
            public string NoA { get; init; } = "";
            public string NameB { get; init; } = "";
            public string SetB { get; init; } = "";
            public string NoB { get; init; } = "";
            public int Count { get; init; } = 1;
            public TokenCollectionEntry? RowA { get; set; }
            public TokenCollectionEntry? RowB { get; set; }
            /// <summary>Cards that can be paired from your copies (0: not both yours, or all paired already).</summary>
            public int CanPair { get; set; }
            /// <summary>"Treasure (TFRA #15) / Angel (TFRA #3)".</summary>
            public string Text => $"{NameA} ({SetA.ToUpperInvariant()} #{NoA})  /  {NameB} ({SetB.ToUpperInvariant()} #{NoB})";
        }

        private const string PairsHeader = "Side A,Set A,No. A,Side B,Set B,No. B,Cards";

        /// <summary>Your pairs as a CSV file's text: names, set codes and collector numbers, cards.</summary>
        public static string ExportPairsCsv(out int count)
        {
            var links = PairLinks().Where(l => l.Count > 0).ToList();
            var byId = AllPairs().ToDictionary(p => p.TokenPairId);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(PairsHeader);
            count = 0;
            // One line per pair of printings (copies in different finishes or conditions add up).
            foreach (var g in links.Where(l => byId.ContainsKey(l.Id)).Select(l => (P: byId[l.Id], l.Count))
                                   .GroupBy(x => (x.P.ASid, x.P.BSid)))
            {
                var p = g.First().P;
                sb.AppendLine(string.Join(",", new[] { p.AName, p.ASet, p.ANumber, p.BName, p.BSet, p.BNumber,
                                                       g.Sum(x => x.Count).ToString() }.Select(Csv)));
                count++;
            }
            return sb.ToString();
        }

        private static string Csv(string v) =>
            v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

        /// <summary>One CSV line's fields (quotes allowed).</summary>
        private static List<string> CsvFields(string line)
        {
            var list = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else cur.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { list.Add(cur.ToString().Trim()); cur.Clear(); }
                else cur.Append(c);
            }
            list.Add(cur.ToString().Trim());
            return list;
        }

        /// <summary>
        /// Read a pairs file and match each pair to your token rows by set and
        /// collector number (rows with copies not yet on a pair; English Near
        /// Mint first). Pairs you don't have both sides of come back with
        /// CanPair 0. Null: not a pairs file.
        /// </summary>
        public static List<ImportedPair>? ReadPairsCsv(string text)
        {
            var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0 || !lines[0].StartsWith("Side A", StringComparison.OrdinalIgnoreCase)) return null;
            var rows = TokenRows().Where(r => !IsRealDoubleFaced(r)).ToList();
            var left = rows.ToDictionary(r => r, UnpairedCopies);
            TokenCollectionEntry? Find(string set, string no) => rows
                .Where(r => string.Equals(r.SetCode, set, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.CollectorNumber, no, StringComparison.OrdinalIgnoreCase) && left[r] > 0)
                .OrderBy(r => CardLanguage.Normalize(r.Language) == CardLanguage.Default ? 0 : 1)
                .ThenByDescending(r => left[r])
                .FirstOrDefault();
            var list = new List<ImportedPair>();
            foreach (var line in lines.Skip(1))
            {
                var f = CsvFields(line);
                if (f.Count < 6) continue;
                int n = f.Count > 6 && int.TryParse(f[6], out var c) && c > 0 ? c : 1;
                var ip = new ImportedPair { NameA = f[0], SetA = f[1], NoA = f[2], NameB = f[3], SetB = f[4], NoB = f[5], Count = n };
                ip.RowA = Find(ip.SetA, ip.NoA);
                ip.RowB = Find(ip.SetB, ip.NoB);
                if (ip.RowA != null && ip.RowB != null && TokenKey(ip.RowA) != TokenKey(ip.RowB))
                {
                    ip.CanPair = Math.Min(n, Math.Min(left[ip.RowA], left[ip.RowB]));
                    left[ip.RowA] -= ip.CanPair;
                    left[ip.RowB] -= ip.CanPair;
                }
                list.Add(ip);
            }
            return list;
        }

        /// <summary>Pair what the file matched. One combined result.</summary>
        public static EditResult PairImported(IEnumerable<ImportedPair> pairs)
        {
            int done = 0;
            var notes = new List<string>();
            var touched = new List<RowKey>();
            foreach (var p in pairs.Where(p => p.CanPair > 0 && p.RowA != null && p.RowB != null))
            {
                var r = Pair(p.RowA!, p.RowB!, p.CanPair);
                if (r.Changed > 0) { done += r.Changed; touched.AddRange(r.Touched); }
                else notes.Add(r.Message);
            }
            var result = new EditResult
            {
                Changed = done,
                Warning = notes.Count > 0,
                Message = (done == 0 ? "No pairs added." : $"Paired {done} two-sided card{(done == 1 ? "" : "s")} from the file.") +
                          (notes.Count > 0 ? "  " + string.Join("  ", notes.Take(3)) : ""),
            };
            result.Touched.AddRange(touched);
            return result;
        }
    }
}
