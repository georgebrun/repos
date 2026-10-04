using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>One line ready to import: the printing it matched and how it goes in.</summary>
    public sealed class ImportItem
    {
        public PoolPrinting Printing { get; init; } = new();
        public string Finish { get; init; } = CardFinish.NonFoil;
        public string Language { get; init; } = CardLanguage.Default;
        public string Condition { get; init; } = CardCondition.Default;
        public int Quantity { get; init; }
        public string Notes { get; init; } = "";
        public string Storage { get; init; } = "";
        public bool? Favorite { get; init; }
        public DateTime? DateAdded { get; init; }
        public ImportSection Section { get; init; }
    }

    /// <summary>What Undo needs to take an import back.</summary>
    public sealed class ImportUndo
    {
        internal List<EditSnapshot> Snaps { get; } = new();
        internal string? DeckPath { get; set; }
        /// <summary>The new deck file as the import wrote it (Undo only removes it if unchanged).</summary>
        internal DateTime DeckWritten { get; set; }
        public string Text { get; init; } = "";
    }

    /// <summary>
    /// Imports (Edit → Import / Export): many lines into a collection table,
    /// the Want List or a new deck — one save per table, one Undo step.
    /// Same rules as every other edit: a row is printing + finish + language +
    /// condition, a finish the printing doesn't come in is never stored, and
    /// copies claimed by decks or the Trade Binder are never removed.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>A printing of any pool table (planes, schemes … too) by Scryfall id — for BoE full files.</summary>
        public static PoolPrinting? PrintingFor(string collTag, string sid)
        {
            var c = FindPoolCard(collTag, sid);
            if (c == null) return null;
            var (nf, f, e) = FinishesOf(collTag, c);
            return new PoolPrinting
            {
                Table = collTag,
                ScryfallId = sid,
                Name = GetString(c, "Name"),
                SetCode = GetString(c, "SetCode"),
                SetName = GetString(c, "SetName"),
                CollectorNumber = GetString(c, "CollectorNumber"),
                NonFoil = nf, Foil = f, Etched = e,
            };
        }

        /// <summary>The full pool cards of many printings (to copy card data into new rows).</summary>
        private static Dictionary<string, object> PoolCardsMany(string collTag, IEnumerable<string> sids)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var all = sids.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            using var db = new AppDbContext();
            for (int i = 0; i < all.Count; i += 400)
            {
                var chunk = all.Skip(i).Take(400).ToList();
                IEnumerable<object> cards = collTag switch
                {
                    "CollTokens" => db.TokenCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    "CollPlanes" => db.PlanarCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    "CollSchemes" => db.SchemeCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    "CollVanguards" => db.VanguardCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    "CollArtSeries" => db.ArtSeriesCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    "CollConspiracies" => db.ConspiracyCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                    _ => db.PoolCards.AsNoTracking().Where(c => chunk.Contains(c.ScryfallId)).ToList(),
                };
                foreach (var c in cards) map.TryAdd(GetString(c, "ScryfallId"), c);
            }
            return map;
        }

        // ══════════════════════════════════════════════════════════════════
        // COLLECTION
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Import into the collection tables (each item says which: cards,
        /// tokens, planes …). <see cref="ImportMode.Add"/> adds the copies;
        /// <see cref="ImportMode.Replace"/> makes every table in the import
        /// match it exactly — rows not in the file go, but never below the
        /// copies decks or the Trade Binder claim.
        /// </summary>
        public static (EditResult Result, ImportUndo? Undo) ImportIntoCollection(
            IReadOnlyList<ImportItem> items, ImportMode mode, string description)
        {
            var undo = new ImportUndo { Text = description };
            int added = 0, newRows = 0, removedCopies = 0, skipped = 0;
            var notes = new List<string>();
            try
            {
                EnsureBackup();
                foreach (var tableGroup in items.GroupBy(i => i.Printing.Table))
                {
                    string table = tableGroup.Key;
                    var fileSids = tableGroup.Select(i => i.Printing.ScryfallId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    List<string> snapSids;
                    using (var probe = new CollectionDbContext())
                        snapSids = mode == ImportMode.Replace
                            ? AllSids(probe, table).Union(fileSids, StringComparer.OrdinalIgnoreCase).ToList()
                            : fileSids;
                    var snap = Snapshot(table, snapSids);
                    if (snap == null) throw new InvalidOperationException($"could not read the {table} table for Undo");

                    // One entry per row key (copies of the same row in several file lines add up).
                    var wanted = new Dictionary<RowKey, (ImportItem First, int Qty)>();
                    foreach (var it in tableGroup)
                    {
                        // Last line of defence: never a finish the printing doesn't come in.
                        if (!it.Printing.Has(it.Finish)) { skipped++; continue; }
                        var key = KeyFor(table, it.Printing.ScryfallId, it.Finish, it.Language, it.Condition);
                        wanted[key] = wanted.TryGetValue(key, out var w) ? (w.First, w.Qty + it.Quantity) : (it, it.Quantity);
                    }

                    var pools = PoolCardsMany(table, wanted.Keys.Select(k => k.ScryfallId));
                    using var db = new CollectionDbContext();
                    var rows = RowsMany(db, table, mode == ImportMode.Replace ? snapSids : fileSids);
                    var etchedOnly = items.Where(i => i.Printing.Table == table)
                        .GroupBy(i => i.Printing.ScryfallId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Printing.Etched && !g.First().Printing.Foil, StringComparer.OrdinalIgnoreCase);
                    bool EO(string sid) => etchedOnly.TryGetValue(sid, out var e) && e;
                    var touched = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    // Existing rows by key, worked out once (a whole table in Replace mode).
                    var byKey = rows.GroupBy(r => KeyOf(r, EO(r.ScryfallId)))
                                    .ToDictionary(g => g.Key, g => new Queue<Row>(g));

                    foreach (var (key, (first, qty)) in wanted)
                    {
                        Row? row = byKey.TryGetValue(key, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
                        pools.TryGetValue(key.ScryfallId, out var pool);
                        if (row == null)
                        {
                            if (qty <= 0) continue;
                            if (pool == null) { skipped++; notes.Add($"{first.Printing.Name}: not in the card pool"); continue; }
                            row = NewRow(table, pool);
                            row.WriteKey(key);
                            row.Quantity = qty;
                            row.Price = PriceOf(table, pool, key.Finish);
                            row.DateAdded = first.DateAdded ?? DateTime.Now;
                            row.DateModified = DateTime.Now;
                            if (first.Notes.Length > 0) row.Notes = first.Notes;
                            if (first.Storage.Length > 0) row.Storage = first.Storage;
                            if (first.Favorite == true) row.IsFavorite = true;
                            db.Add(row.Entity);
                            newRows++;
                            added += qty;
                        }
                        else
                        {
                            int used = Math.Max(0, row.UsedCount);
                            int before = row.Quantity;
                            row.WriteKey(key);
                            row.Quantity = mode == ImportMode.Replace ? Math.Max(qty, used) : before + qty;
                            if (mode == ImportMode.Replace && qty < used)
                                notes.Add($"{first.Printing.Name} ({key.Text}): kept {used}, claimed by decks or the Trade Binder");
                            if (pool != null) row.Price = PriceOf(table, pool, key.Finish);
                            if (first.Notes.Length > 0 && (mode == ImportMode.Replace || row.Notes.Length == 0)) row.Notes = first.Notes;
                            if (first.Storage.Length > 0 && (mode == ImportMode.Replace || row.Storage.Length == 0)) row.Storage = first.Storage;
                            if (first.Favorite.HasValue && mode == ImportMode.Replace) row.IsFavorite = first.Favorite.Value;
                            row.DateModified = DateTime.Now;
                            if (row.Quantity > before) added += row.Quantity - before;
                            else removedCopies += before - row.Quantity;
                            if (row.Quantity <= 0) db.Remove(row.Entity);
                        }
                        touched.Add(row.Entity);
                    }

                    if (mode == ImportMode.Replace)
                    {
                        // Rows the file doesn't list: down to the copies claimed (gone when none).
                        foreach (var r in rows.Where(r => !touched.Contains(r.Entity)))
                        {
                            int used = Math.Max(0, r.UsedCount);
                            if (r.Quantity <= used) continue;
                            removedCopies += r.Quantity - used;
                            if (used == 0) db.Remove(r.Entity);
                            else
                            {
                                r.Quantity = used;
                                r.DateModified = DateTime.Now;
                                notes.Add($"{r.Name}: kept {used}, claimed by decks or the Trade Binder");
                            }
                        }
                    }
                    db.SaveChanges();                         // one save for the table
                    MarkAfter(snap);
                    undo.Snaps.Add(snap);
                }

                string msg = mode == ImportMode.Replace
                    ? $"Imported: {added:N0} copies added ({newRows:N0} new rows), {removedCopies:N0} removed to match the file."
                    : $"Imported: {added:N0} copies added ({newRows:N0} new rows).";
                if (skipped > 0) msg += $" {skipped} line(s) skipped.";
                if (notes.Count > 0) msg += $" {notes.Count} note(s): {string.Join("; ", notes.Take(3))}" + (notes.Count > 3 ? " …" : "");
                return (new EditResult { Changed = Math.Max(1, added + removedCopies), Message = msg, Warning = skipped > 0 || notes.Count > 0 },
                        undo.Snaps.Count > 0 ? undo : null);
            }
            catch (Exception ex)
            {
                // Tables already imported stay imported — and can be undone.
                return (Fail($"The import stopped: {ex.Message}" +
                             (undo.Snaps.Count > 0 ? " The part already imported can be undone." : "")),
                        undo.Snaps.Count > 0 ? undo : null);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // WANT LIST
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// Import into the Want List (cards; a want row is printing + finish).
        /// Replace makes the whole Want List match the file.
        /// </summary>
        public static (EditResult Result, ImportUndo? Undo) ImportIntoWantList(
            IReadOnlyList<ImportItem> items, ImportMode mode, string description)
        {
            var undo = new ImportUndo { Text = description };
            try
            {
                EnsureBackup();
                var cards = items.Where(i => i.Printing.Table == CardsTable && i.Printing.Has(i.Finish)).ToList();
                int skipped = items.Count - cards.Count;
                var fileSids = cards.Select(i => i.Printing.ScryfallId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fileSids.Count == 0 && mode == ImportMode.Add)
                    return (Fail("Nothing to put on the Want List (it holds cards, in finishes they come in)."), null);
                List<string> snapSids;
                using (var probe = new CollectionDbContext())
                    snapSids = mode == ImportMode.Replace
                        ? AllSids(probe, WantTable).Union(fileSids, StringComparer.OrdinalIgnoreCase).ToList()
                        : fileSids;
                var snap = Snapshot(WantTable, snapSids) ?? throw new InvalidOperationException("could not read the Want List for Undo");

                var wanted = cards.GroupBy(i => (Sid: i.Printing.ScryfallId.ToLowerInvariant(), i.Finish))
                                  .ToDictionary(g => g.Key, g => (First: g.First(), Qty: g.Sum(i => i.Quantity)));
                var pools = PoolCardsMany(CardsTable, fileSids);
                int added = 0, removed = 0;
                using (var db = new CollectionDbContext())
                {
                    var rows = db.WantListEntries.ToList()
                        .Where(e => mode == ImportMode.Replace || fileSids.Contains(e.ScryfallId, StringComparer.OrdinalIgnoreCase)).ToList();
                    var touched = new HashSet<WantListEntry>();
                    foreach (var ((sid, finish), (first, qty)) in wanted)
                    {
                        bool eo = first.Printing.Etched && !first.Printing.Foil;
                        var row = rows.FirstOrDefault(e => string.Equals(e.ScryfallId, sid, StringComparison.OrdinalIgnoreCase) &&
                                                           CardFinish.Shown(e.Finish, eo) == finish && !touched.Contains(e));
                        if (row == null)
                        {
                            if (qty <= 0 || !pools.TryGetValue(sid, out var pool)) continue;
                            row = NewWantEntry(pool);
                            db.WantListEntries.Add(row);
                        }
                        int before = row.Quantity;
                        row.Finish = finish;
                        row.IsFoil = finish != CardFinish.NonFoil;
                        row.Quantity = mode == ImportMode.Replace ? qty : before + qty;
                        if (pools.TryGetValue(sid, out var p)) row.Price = PaperPriceOf(p, finish);
                        if (first.Notes.Length > 0 && (mode == ImportMode.Replace || string.IsNullOrEmpty(row.Notes))) row.Notes = first.Notes;
                        if (row.Quantity > before) added += row.Quantity - before; else removed += before - row.Quantity;
                        if (row.Quantity <= 0) db.WantListEntries.Remove(row);
                        touched.Add(row);
                    }
                    if (mode == ImportMode.Replace)
                        foreach (var r in rows.Where(r => !touched.Contains(r)))
                        {
                            removed += r.Quantity;
                            db.WantListEntries.Remove(r);
                        }
                    db.SaveChanges();
                }
                MarkAfter(snap);
                undo.Snaps.Add(snap);
                string msg = $"Want List: {added:N0} copies added" + (removed > 0 ? $", {removed:N0} removed to match the file" : "") + ".";
                if (skipped > 0) msg += $" {skipped} line(s) skipped (tokens, or a finish the printing doesn't come in).";
                return (new EditResult { Changed = Math.Max(1, added + removed), Message = msg, Warning = skipped > 0 }, undo);
            }
            catch (Exception ex)
            {
                return (Fail($"The import stopped: {ex.Message}"), null);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // NEW DECK
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// A new deck file from the lines: Commander lines lead (and make it a
        /// Commander deck), Sideboard and Tokens parts kept apart. Nothing is
        /// claimed from the collection (that's Deck → Collection, afterwards).
        /// </summary>
        public static (EditResult Result, ImportUndo? Undo, string Path) ImportAsDeck(
            IReadOnlyList<ImportItem> items, string deckName, string description)
        {
            try
            {
                deckName = string.IsNullOrWhiteSpace(deckName) ? "Imported Deck" : deckName.Trim();
                bool commander = items.Any(i => i.Section == ImportSection.Commander);
                var deck = new Deck
                {
                    Name = deckName,
                    DeckType = commander ? DeckType.Commander : DeckType.Standard,
                    Created = DateTime.Now,
                    Modified = DateTime.Now,
                };
                var cards = PoolCardsMany(CardsTable, items.Where(i => !i.Printing.IsToken).Select(i => i.Printing.ScryfallId));
                var tokens = PoolCardsMany(TokensTable, items.Where(i => i.Printing.IsToken).Select(i => i.Printing.ScryfallId));
                int copies = 0, skipped = 0;
                foreach (var it in items)
                {
                    if (!it.Printing.Has(it.Finish) || it.Quantity <= 0) { skipped++; continue; }
                    DeckCard? template = it.Printing.IsToken
                        ? tokens.TryGetValue(it.Printing.ScryfallId, out var t) ? DeckService.FromTokenCard((TokenCard)t) : null
                        : cards.TryGetValue(it.Printing.ScryfallId, out var c) ? DeckService.FromPoolCard((PoolCard)c) : null;
                    if (template == null) { skipped++; continue; }
                    var section = it.Printing.IsToken ? DeckCardCategory.Tokens : it.Section switch
                    {
                        ImportSection.Commander => DeckCardCategory.Commander,
                        ImportSection.Sideboard => DeckCardCategory.Sideboard,
                        ImportSection.Tokens => DeckCardCategory.Tokens,
                        _ => DeckCardCategory.Mainboard,
                    };
                    var r = DeckEditService.Add(deck, template, it.Finish, it.Quantity, section);
                    if (r.Changed > 0) copies += r.Changed; else skipped++;
                }
                if (copies == 0) return (Fail("Nothing could go in the deck."), null, "");
                deck.FilePath = DeckService.NewDeckPath(deckName);
                DeckService.Save(deck);
                var undo = new ImportUndo { Text = description, DeckPath = deck.FilePath,
                                            DeckWritten = System.IO.File.GetLastWriteTimeUtc(deck.FilePath) };
                string msg = $"New deck \"{deckName}\" ({(commander ? "Commander" : "Constructed")}): {copies} cards. " +
                             "Change its type in Edit → Decks → Deck Settings if needed; claim copies you own in Deck → Collection." +
                             (skipped > 0 ? $" {skipped} line(s) skipped." : "");
                return (new EditResult { Changed = copies, Message = msg, Warning = skipped > 0 }, undo, deck.FilePath);
            }
            catch (Exception ex)
            {
                return (Fail($"Could not make the deck: {ex.Message}"), null, "");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // FINISH OF AN IMPORTED LINE
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// The finish a line goes in as, for this printing. Not stated → the
        /// printing's usual one (non-foil, else foil, else etched). "Foil" on
        /// an etched-only printing → etched (most apps have no etched value).
        /// Null with <paramref name="problem"/> when the printing doesn't come
        /// in the finish asked for — never stored that way.
        /// </summary>
        public static string? ResolveFinish(string? stated, PoolPrinting p, out string problem)
        {
            problem = "";
            if (stated == null)
                return p.NonFoil ? CardFinish.NonFoil : p.Foil ? CardFinish.Foil : p.Etched ? CardFinish.Etched : CardFinish.NonFoil;
            string f = CardFinish.Normalize(stated);
            if (p.Has(f)) return f;
            if (f == CardFinish.Foil && p.Etched) return CardFinish.Etched;
            problem = $"doesn't come in {CardFinish.Display(f)}" +
                      (p.Finishes().Any() ? $" (only {string.Join(", ", p.Finishes().Select(CardFinish.Display))})" : "");
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // EXPORT: BoE full collection file
        // ══════════════════════════════════════════════════════════════════
        /// <summary>The paper collection tables, in the order the full file lists them.</summary>
        public static readonly string[] PaperTables =
            { CardsTable, TokensTable, "CollPlanes", "CollSchemes", "CollVanguards", "CollArtSeries", "CollConspiracies" };

        /// <summary>
        /// Every row of every paper collection table, every field — the file a
        /// full restore (Import → BoE full file → Replace) brings back.
        /// Returns the file's text, its row count and the copies in it.
        /// </summary>
        public static (string Text, int Rows, int Copies) BoeFullText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(CsvText.Line(BoeFormat.Headers));
            int n = 0, copies = 0;
            using var db = new CollectionDbContext();
            foreach (string table in PaperTables)
            {
                var rows = RowsMany(db, table, AllSids(db, table))
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var r in rows)
                {
                    var e = r.Entity;
                    string Str(string p) => e.GetType().GetProperty(p)?.GetValue(e) as string ?? "";
                    var added = e.GetType().GetProperty("DateAdded")?.GetValue(e) as DateTime?;
                    sb.AppendLine(CsvText.Line(new[]
                    {
                        table, r.ScryfallId, r.Name, Str("SetCode").ToUpperInvariant(), Str("SetName"), Str("CollectorNumber"),
                        CardFinish.Normalize(r.Finish), CardLanguage.Normalize(r.Language), CardCondition.Normalize(r.Condition),
                        r.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), CsvText.Num(r.Price),
                        r.Notes, r.Storage, r.IsFavorite ? "Yes" : "",
                        added.HasValue && added.Value > DateTime.MinValue ? added.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "",
                    }));
                    n++;
                    copies += r.Quantity;
                }
            }
            return (sb.ToString(), n, copies);
        }

        // ══════════════════════════════════════════════════════════════════
        // UNDO
        // ══════════════════════════════════════════════════════════════════
        /// <summary>Take an import back: the tables as they were (refused if they changed since), or the new deck file removed.</summary>
        public static EditResult UndoImport(ImportUndo undo)
        {
            if (undo.DeckPath != null)
            {
                try
                {
                    if (System.IO.File.Exists(undo.DeckPath) &&
                        System.IO.File.GetLastWriteTimeUtc(undo.DeckPath) != undo.DeckWritten)
                        return Fail($"Can't undo: the new deck has been changed since. If you don't want it, use Tear Down in Edit → Decks.");
                    if (System.IO.File.Exists(undo.DeckPath)) System.IO.File.Delete(undo.DeckPath);
                    return new EditResult { Changed = 1, Message = $"Undone: {undo.Text} (the new deck file was removed)." };
                }
                catch (Exception ex)
                {
                    return Fail($"Could not remove the new deck file: {ex.Message}");
                }
            }
            return RestoreAll(undo.Snaps, undo.Text);
        }
    }
}
