using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>A token of the card pool, as the Two-Sided window lists it: what you have of it.</summary>
    public sealed class TokenChoice
    {
        public TokenCard Card { get; init; } = new();
        public string Name => Card.Name;
        public string SetCode => Card.SetCode.ToUpperInvariant();
        public string Number => Card.CollectorNumber;
        public double NumberSort => double.TryParse(new string(Card.CollectorNumber.TakeWhile(char.IsDigit).ToArray()), out var d) ? d : 9999;
        public string PT => CardFaces.PowerToughness(Card.Power, Card.Toughness);
        /// <summary>Single copies in Collection → Tokens (any finish, language, condition).</summary>
        public int Owned { get; init; }
        /// <summary>Of those, free (not in decks): the ones that can be paired.</summary>
        public int OffPair { get; init; }
        /// <summary>Of those, claimed by decks.</summary>
        public int InUse { get; init; }
        /// <summary>"Treasure, Goblin": the two-sided cards you have with this token on one side.</summary>
        public string PairedWith { get; init; } = "";
        public string OwnedText => Owned == 0 ? "" : Owned.ToString();
        public string OffPairText => Owned == 0 ? "" : OffPair.ToString();
        public string InUseText => InUse == 0 ? "" : InUse.ToString();
    }

    /// <summary>
    /// Two-sided tokens: two of Scryfall's tokens printed back to back are one
    /// card with its own collection row, "Officer // Treasure" (see
    /// <see cref="TokenPairing"/>). Pairing takes copies off the two single
    /// rows and puts them on the card's row; unpairing gives them back. The
    /// card's row is a row like any other: decks claim it, Used and Available
    /// follow, and removing it removes the card.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>Nothing about pairs is cached any more (kept for the pages that call it).</summary>
        public static void PairsChanged() { }

        /// <summary>The Name column (a two-sided card's row is named "Officer // Treasure" already).</summary>
        public static string NameShown(object? row) => row == null ? "" : GetString(row, "Name");

        /// <summary>Physical cards among these token rows: their copies (a two-sided card is one row).</summary>
        public static int PhysicalCards(IEnumerable<object> rows) =>
            rows.OfType<TokenCollectionEntry>().Sum(r => Math.Max(0, r.Quantity));

        /// <summary>A token row's key (exact finish, language, condition).</summary>
        public static RowKey TokenKey(object row) =>
            RowKey.Of(GetString(row, "ScryfallId"), GetString(row, "Finish"), GetString(row, "Language"), GetString(row, "Condition"));

        /// <summary>Real double-faced tokens are one card already: never paired.</summary>
        public static bool IsRealDoubleFaced(object row) =>
            string.Equals(GetString(row, "Layout"), "double_faced_token", StringComparison.OrdinalIgnoreCase);

        /// <summary>A two-sided card's row ("Officer // Treasure").</summary>
        public static bool IsPairRow(object row) => TokenPairing.IsPair(row);

        /// <summary>Copies of a row not in decks.</summary>
        public static int FreeCopies(object row) =>
            Math.Max(0, (GetInt(row, "Quantity") ?? 0) - Math.Max(0, GetInt(row, "UsedCount") ?? 0));

        /// <summary>Copies of a single token row that can be paired: its free copies (none for a two-sided card or a double-faced token).</summary>
        public static int UnpairedCopies(object row) =>
            IsPairRow(row) || IsRealDoubleFaced(row) ? 0 : FreeCopies(row);

        /// <summary>"Angel 3/3 (TFRA #3)".</summary>
        private static string SideText(object row)
        {
            string pt = GetString(row, "Power").Length > 0 || GetString(row, "Toughness").Length > 0
                ? $" {GetString(row, "Power")}/{GetString(row, "Toughness")}" : "";
            return $"{GetString(row, "Name")}{pt} ({GetString(row, "SetCode").ToUpperInvariant()} #{GetString(row, "CollectorNumber")})";
        }

        /// <summary>The two-sided card of two single rows (the pool's card data; the rows' own when the pool hasn't it).</summary>
        private static TokenCard ComposeRows(TokenCollectionEntry a, TokenCollectionEntry b)
        {
            var pool = TokenPairing.PoolCards(new[] { a.ScryfallId, b.ScryfallId });
            return TokenPairing.Compose(pool.GetValueOrDefault(a.ScryfallId) ?? TokenPairing.FromRow(a),
                                        pool.GetValueOrDefault(b.ScryfallId) ?? TokenPairing.FromRow(b));
        }

        // ══════════════════════════════════════════════════════════════════
        // PAIR / UNPAIR / ADD A TWO-SIDED CARD
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Make <paramref name="count"/> two-sided cards from two single token
        /// rows: the copies come off both rows (free copies only) and go on the
        /// card's own row, in the first row's finish, language and condition.
        /// Run it inside the page's Undo step (the three printings).
        /// </summary>
        public static EditResult Pair(object rowA, object rowB, int count)
        {
            if (rowA is not TokenCollectionEntry a || rowB is not TokenCollectionEntry b)
                return Fail("Only rows of Collection → Tokens can be paired.");
            if (string.Equals(a.ScryfallId, b.ScryfallId, StringComparison.OrdinalIgnoreCase))
                return Fail("A token can't be its own other side.");
            if (IsPairRow(a) || IsPairRow(b))
                return Fail($"{(IsPairRow(a) ? a.Name : b.Name)} is a two-sided card already: unpair it first.");
            if (IsRealDoubleFaced(a) || IsRealDoubleFaced(b))
                return Fail($"{(IsRealDoubleFaced(a) ? a.Name : b.Name)} is already one double-faced card in Scryfall: it needs no other side.");
            if (count <= 0) return Fail("Pair at least one copy.");
            if (CardFinish.Normalize(a.Finish) != CardFinish.Normalize(b.Finish))
                return Fail($"Both sides of a card share its finish: {SideText(a)} is {CardFinish.Display(a.Finish)}, {SideText(b)} is {CardFinish.Display(b.Finish)}.");
            int fa = FreeCopies(a), fb = FreeCopies(b);
            if (count > Math.Min(fa, fb))
                return Fail($"Only {Math.Min(fa, fb)} cop{(Math.Min(fa, fb) == 1 ? "y" : "ies")} can be paired (copies in decks stay as they are): " +
                            $"{SideText(a)} has {fa} free, {SideText(b)} has {fb}.");
            var card = ComposeRows(a, b);
            var ra = RemoveFromRow(TokensTable, a.TokenCollectionEntryId, count, false);
            if (ra.Changed < count) return Fail($"Could not take {count} × {a.Name} off its row: {ra.Message}");
            var rb = RemoveFromRow(TokensTable, b.TokenCollectionEntryId, count, false);
            if (rb.Changed < count) return Fail($"Could not take {count} × {b.Name} off its row: {rb.Message}");
            var add = Add(TokensTable, card, a.Finish, count, a.Language, a.Condition);
            if (add.Changed <= 0) return add;
            string note = CardLanguage.Normalize(a.Language) != CardLanguage.Normalize(b.Language) ||
                          CardCondition.Normalize(a.Condition) != CardCondition.Normalize(b.Condition)
                ? $" ({b.Name}'s copies were {CardLanguage.Normalize(b.Language)} · {CardCondition.Normalize(b.Condition)}: the card is {CardLanguage.Normalize(a.Language)} · {CardCondition.Normalize(a.Condition)})" : "";
            return new EditResult
            {
                Changed = count,
                Warning = note.Length > 0,
                Message = $"Paired {count} × {card.Name}: one card, two sides (its own row now).{note}",
                Touched = { add.Touched.FirstOrDefault() ?? TokenKey(a), TokenKey(a), TokenKey(b) },
            };
        }

        /// <summary>
        /// Take <paramref name="count"/> two-sided cards apart (free copies only):
        /// each side goes back to its own single row, same finish, language and
        /// condition. Run it inside the page's Undo step (the three printings).
        /// </summary>
        public static EditResult Unpair(object pairRow, int count = int.MaxValue)
        {
            if (pairRow is not TokenCollectionEntry p || TokenPairing.Sides(p.ScryfallId) is not { } sides)
                return Fail("Only a two-sided card can be unpaired.");
            int free = FreeCopies(p);
            if (free <= 0) return Fail($"Every copy of {p.Name} is in a deck: free one first, then unpair it.");
            int n = Math.Min(count, free);
            var pool = TokenPairing.PoolCards(new[] { sides.A, sides.B });
            var (nameA, nameB) = TokenPairing.SideNames(p.Name);
            if (!pool.TryGetValue(sides.A, out var ta) || !pool.TryGetValue(sides.B, out var tb))
                return Fail($"{p.Name}: the card pool hasn't both sides ({nameA}, {nameB}) to put back. Update Database, then try again.");
            var r = RemoveFromRow(TokensTable, p.TokenCollectionEntryId, n, false);
            if (r.Changed <= 0) return r;
            var r1 = Add(TokensTable, ta, p.Finish, r.Changed, p.Language, p.Condition);
            var r2 = Add(TokensTable, tb, p.Finish, r.Changed, p.Language, p.Condition);
            return new EditResult
            {
                Changed = r.Changed,
                Warning = r1.Changed <= 0 || r2.Changed <= 0,
                Message = r1.Changed > 0 && r2.Changed > 0
                    ? $"Unpaired {r.Changed} × {p.Name}: {nameA} and {nameB} are single tokens again." + (n < count && count != int.MaxValue ? $" (only {n} free)" : "")
                    : $"Unpaired {r.Changed} × {p.Name}, but: {r1.Message} {r2.Message}",
                Touched = { TokenKey(p), RowKey.Of(ta.ScryfallId, p.Finish, p.Language, p.Condition), RowKey.Of(tb.ScryfallId, p.Finish, p.Language, p.Condition) },
            };
        }

        /// <summary>Add <paramref name="qty"/> two-sided cards made of these two pool tokens (one row of its own).</summary>
        public static EditResult AddPair(TokenCard x, TokenCard y, string finish, int qty, string? language, string? condition)
        {
            if (string.Equals(x.ScryfallId, y.ScryfallId, StringComparison.OrdinalIgnoreCase))
                return Fail("A token can't be its own other side.");
            if (TokenPairing.IsPairSid(x.ScryfallId) || TokenPairing.IsPairSid(y.ScryfallId))
                return Fail("That's a two-sided card already.");
            foreach (var t in new[] { x, y })
                if (string.Equals(t.Layout, "double_faced_token", StringComparison.OrdinalIgnoreCase))
                    return Fail($"{t.Name} is one double-faced card already: it can't be another token's back.");
            return Add(TokensTable, TokenPairing.Compose(x, y), finish, qty, language, condition);
        }

        /// <summary>The finishes a two-sided card of these two tokens comes in (both sides have to).</summary>
        public static (bool NonFoil, bool Foil, bool Etched) PairFinishes(TokenCard x, TokenCard y)
        {
            var (n1, f1, e1) = FinishesOf(TokensTable, x);
            var (n2, f2, e2) = FinishesOf(TokensTable, y);
            return (n1 && n2, f1 && f2, e1 && e2);
        }

        // ══════════════════════════════════════════════════════════════════
        // WHAT A TOKEN IS PAIRED WITH (slips, suggestions, the Two-Sided window)
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Your token rows (the Two-Sided window's list).</summary>
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

        /// <summary>Your two-sided cards with this token on one side.</summary>
        public static List<TokenCollectionEntry> PairRowsWith(string sid) =>
            TokenRows().Where(r => TokenPairing.Sides(r.ScryfallId) is { } s &&
                                   (string.Equals(s.A, sid, StringComparison.OrdinalIgnoreCase) || string.Equals(s.B, sid, StringComparison.OrdinalIgnoreCase)))
                       .ToList();

        /// <summary>The other side of a two-sided card, given one side ("" when it isn't one of its sides).</summary>
        public static string OtherSideOf(string pairSid, string sid) =>
            TokenPairing.Sides(pairSid) is { } s
                ? string.Equals(s.A, sid, StringComparison.OrdinalIgnoreCase) ? s.B
                : string.Equals(s.B, sid, StringComparison.OrdinalIgnoreCase) ? s.A : ""
                : "";

        /// <summary>An other side to print: its printing and name.</summary>
        public sealed record OtherSideRef(string ScryfallId, string Name);

        /// <summary>A two-sided card's back (its second side), for print slips; empty for anything else.</summary>
        public static List<OtherSideRef> OtherSideRefs(object card)
        {
            if (TokenPairing.Sides(GetString(card, "ScryfallId")) is not { } s) return new List<OtherSideRef>();
            var (_, nameB) = TokenPairing.SideNames(GetString(card, "Name"));
            return new List<OtherSideRef> { new(s.B, nameB) };
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
        /// double-faced token or a two-sided card (one card already), and
        /// released on or after <see cref="TwoSidedSince"/> (an unknown date: yes).
        /// </summary>
        public static bool MayHaveOtherSide(object token)
        {
            if (IsRealDoubleFaced(token) || IsPairRow(token)) return false;
            string day = GetString(token, "ReleasedAt");
            return day.Length == 0 || string.CompareOrdinal(day, TwoSidedSince) >= 0;
        }

        /// <summary>The token this printing was last paired with (to pick it again when adding more), or null.</summary>
        public static string? LastPartnerOf(string sid) =>
            PairRowsWith(sid).OrderByDescending(r => r.DateModified).Select(r => OtherSideOf(r.ScryfallId, sid))
                             .FirstOrDefault(s => s.Length > 0);

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
        /// tokens and this row's own printing are left out. Each with your
        /// single copies, how many are free, in use, and your two-sided cards with it.
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
            var singles = mine.Where(r => !IsPairRow(r)).GroupBy(r => r.ScryfallId, StringComparer.OrdinalIgnoreCase)
                              .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var pairNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in mine.Where(IsPairRow))
            {
                if (TokenPairing.Sides(r.ScryfallId) is not { } s) continue;
                var (na, nb) = TokenPairing.SideNames(r.Name);
                void Note(string side, string other)
                {
                    if (!pairNames.TryGetValue(side, out var l)) pairNames[side] = l = new List<string>();
                    if (!l.Contains(other, StringComparer.OrdinalIgnoreCase)) l.Add(other);
                }
                Note(s.A, nb);
                Note(s.B, na);
            }
            var list = new List<TokenChoice>();
            foreach (var t in pool)
            {
                singles.TryGetValue(t.ScryfallId, out var rows);
                rows ??= new List<TokenCollectionEntry>();
                list.Add(new TokenChoice
                {
                    Card = t,
                    Owned = rows.Sum(r => r.Quantity),
                    OffPair = rows.Sum(FreeCopies),
                    InUse = rows.Sum(r => Math.Min(r.Quantity, Math.Max(0, r.UsedCount))),
                    PairedWith = string.Join(", ", pairNames.GetValueOrDefault(t.ScryfallId) ?? new List<string>()),
                });
            }
            return list;
        }

        /// <summary>
        /// Your single row of this printing to pair with: one with enough free
        /// copies, the same finish, language and condition as the row first.
        /// Null when none has enough (copies have to be added first).
        /// </summary>
        public static TokenCollectionEntry? RowToPairWith(TokenCollectionEntry row, string sid, int count)
        {
            var key = TokenKey(row);
            return TokenRows()
                .Where(r => string.Equals(r.ScryfallId, sid, StringComparison.OrdinalIgnoreCase) && TokenKey(r) != key &&
                            CardFinish.Normalize(r.Finish) == key.Finish && UnpairedCopies(r) >= count)
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
        /// Pair the row with a pool token: with your free single copies of it
        /// when you have enough, else straight from the pool (the row's copies
        /// and the new other sides make the card; nothing extra is added).
        /// Run it inside the page's Undo step.
        /// </summary>
        public static EditResult PairWithPrinting(TokenCollectionEntry row, TokenCard other, int count)
        {
            if (string.Equals(other.Layout, "double_faced_token", StringComparison.OrdinalIgnoreCase))
                return Fail($"{other.Name} is one double-faced card already: it can't be another token's back.");
            var target = RowToPairWith(row, other.ScryfallId, count);
            if (target != null) return Pair(row, target, count);

            // No free single copies of the other side: the row's copies become two-sided cards with it.
            if (FreeCopies(row) < count)
                return Fail($"Only {FreeCopies(row)} of {SideText(row)} are free (copies in decks stay as they are).");
            var (nf, f, e) = PairFinishes(TokenPairing.FromRow(row), other);
            if (!FinishExists(CardFinish.Normalize(row.Finish), nf, f, e))
                return Fail($"{other.Name} doesn't come in {CardFinish.Display(row.Finish)}: both sides of a card share its finish.");
            var pool = TokenPairing.PoolCards(new[] { row.ScryfallId });
            var card = TokenPairing.Compose(pool.GetValueOrDefault(row.ScryfallId) ?? TokenPairing.FromRow(row), other);
            var r = RemoveFromRow(TokensTable, row.TokenCollectionEntryId, count, false);
            if (r.Changed < count) return Fail($"Could not take {count} × {row.Name} off its row: {r.Message}");
            var add = Add(TokensTable, card, row.Finish, count, row.Language, row.Condition);
            if (add.Changed <= 0) return add;
            return new EditResult
            {
                Changed = count,
                Message = $"{count} × {row.Name} became {card.Name}: one card, two sides (its own row now).",
                Touched = { add.Touched.FirstOrDefault() ?? TokenKey(row), TokenKey(row) },
            };
        }

        /// <summary>The printings a pairing touches (for the page's Undo step): the two sides and the card.</summary>
        public static string[] PairPrintings(string sidA, string sidB, string nameA, string numberA, string nameB, string numberB)
        {
            bool aFirst = TokenPairing.AFirst(nameA, numberA, sidA, nameB, numberB, sidB);
            return new[] { sidA, sidB, aFirst ? TokenPairing.SidOf(sidA, sidB) : TokenPairing.SidOf(sidB, sidA) };
        }

        /// <summary>The printings unpairing a two-sided card touches: the card and its two sides.</summary>
        public static string[] UnpairPrintings(string pairSid) =>
            TokenPairing.Sides(pairSid) is { } s ? new[] { pairSid, s.A, s.B } : new[] { pairSid };

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
            /// <summary>Cards that can be paired from your free single copies (0: not both yours, or none free).</summary>
            public int CanPair { get; set; }
            /// <summary>"Treasure (TFRA #15) / Angel (TFRA #3)".</summary>
            public string Text => $"{NameA} ({SetA.ToUpperInvariant()} #{NoA})  /  {NameB} ({SetB.ToUpperInvariant()} #{NoB})";
        }

        private const string PairsHeader = "Side A,Set A,No. A,Side B,Set B,No. B,Cards";

        /// <summary>Your two-sided cards as a CSV file's text: each side's name, set code and collector number, and how many.</summary>
        public static string ExportPairsCsv(out int count)
        {
            var rows = TokenRows().Where(IsPairRow).ToList();
            var pool = TokenPairing.PoolCards(rows.SelectMany(r => TokenPairing.Sides(r.ScryfallId) is { } s ? new[] { s.A, s.B } : Array.Empty<string>()));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(PairsHeader);
            count = 0;
            // One line per card (copies in different finishes or conditions add up).
            foreach (var g in rows.GroupBy(r => r.ScryfallId, StringComparer.OrdinalIgnoreCase))
            {
                var s = TokenPairing.Sides(g.Key)!.Value;
                var first = g.First();
                var (na, nb) = TokenPairing.SideNames(first.Name);
                var nos = CardFaces.Sides(first.CollectorNumber);
                pool.TryGetValue(s.A, out var ta);
                pool.TryGetValue(s.B, out var tb);
                sb.AppendLine(string.Join(",", new[]
                {
                    ta?.Name ?? na, ta?.SetCode ?? first.SetCode, ta?.CollectorNumber ?? (nos.Length > 0 ? nos[0] : ""),
                    tb?.Name ?? nb, tb?.SetCode ?? first.SetCode, tb?.CollectorNumber ?? (nos.Length > 1 ? nos[1] : ""),
                    g.Sum(r => r.Quantity).ToString(),
                }.Select(Csv)));
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
        /// Read a pairs file and match each pair to your single token rows by
        /// set and collector number (free copies; English first). Pairs you
        /// don't have both sides of come back with CanPair 0. Null: not a pairs file.
        /// </summary>
        public static List<ImportedPair>? ReadPairsCsv(string text)
        {
            var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0 || !lines[0].StartsWith("Side A", StringComparison.OrdinalIgnoreCase)) return null;
            var rows = TokenRows().Where(r => !IsRealDoubleFaced(r) && !IsPairRow(r)).ToList();
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
                if (ip.RowA != null && ip.RowB != null && TokenKey(ip.RowA) != TokenKey(ip.RowB) &&
                    CardFinish.Normalize(ip.RowA.Finish) == CardFinish.Normalize(ip.RowB.Finish))
                {
                    ip.CanPair = Math.Min(n, Math.Min(left[ip.RowA], left[ip.RowB]));
                    left[ip.RowA] -= ip.CanPair;
                    left[ip.RowB] -= ip.CanPair;
                }
                list.Add(ip);
            }
            return list;
        }

        /// <summary>The printings importing these pairs touches (for the page's Undo step).</summary>
        public static List<string> ImportPrintings(IEnumerable<ImportedPair> pairs) =>
            pairs.Where(p => p.CanPair > 0 && p.RowA != null && p.RowB != null)
                 .SelectMany(p => PairPrintings(p.RowA!.ScryfallId, p.RowB!.ScryfallId, p.RowA.Name, p.RowA.CollectorNumber, p.RowB.Name, p.RowB.CollectorNumber))
                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Pair what the file matched. One combined result.</summary>
        public static EditResult PairImported(IEnumerable<ImportedPair> pairs)
        {
            int done = 0;
            var notes = new List<string>();
            var touched = new List<RowKey>();
            foreach (var p in pairs.Where(p => p.CanPair > 0 && p.RowA != null && p.RowB != null))
            {
                // The rows as they are now (an earlier pair in the file may have taken copies).
                var a = TokenRow(TokenKey(p.RowA!));
                var b = TokenRow(TokenKey(p.RowB!));
                if (a == null || b == null) { notes.Add($"{p.Text}: not there any more."); continue; }
                var r = Pair(a, b, Math.Min(p.CanPair, Math.Min(FreeCopies(a), FreeCopies(b))));
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
