using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Exports (Edit → Import / Export → Export): the rows of the collection,
    /// a deck, the Want List or the Trade Binder, written by
    /// <see cref="ExportWriter"/> as a text list, a ManaBox CSV or a
    /// spreadsheet CSV. Read-only: nothing is changed.
    /// </summary>
    public static partial class CollectionEditService
    {
        public static ExportResult Export(ExportOptions o)
        {
            try
            {
                if (o.Format == ExportFormat.BoeFull)
                {
                    var (text, n, copies) = BoeFullText();
                    return new ExportResult
                    {
                        Text = text, Lines = n, Copies = copies,
                        FileName = $"BoE Collection {DateTime.Now:yyyy-MM-dd}.csv",
                    };
                }
                string deckName = "";
                string note = "";
                List<ExportRow> rows;
                switch (o.Source)
                {
                    case ExportSource.Deck:
                        var deck = DeckService.Load(o.DeckPath);
                        if (deck == null) return new ExportResult { Error = "Could not open that deck." };
                        deckName = deck.Name;
                        rows = DeckExportRows(deck, o.IncludeTokens);
                        // For MTGO or Arena: each card as a printing that game has.
                        if (o.Format == ExportFormat.MtgoDek) rows = ToGame(rows, OnlineGame.Mtgo, out note);
                        else if (o.Format == ExportFormat.ArenaText) rows = ToGame(rows, OnlineGame.Arena, out note);
                        break;
                    case ExportSource.MtgoCollection:
                        rows = OnlineExportRows(OnlineGame.Mtgo);
                        break;
                    case ExportSource.ArenaCollection:
                        rows = OnlineExportRows(OnlineGame.Arena);
                        break;
                    case ExportSource.WantList:
                        rows = WantExportRows();
                        break;
                    case ExportSource.TradeBinder:
                        rows = BinderExportRows();
                        break;
                    default:
                        rows = CollectionExportRows(o.OnlyFree, o.IncludeTokens, o.IncludeOversized);
                        break;
                }
                var result = ExportWriter.Write(rows, o, deckName);
                if (note.Length == 0) return result;
                return new ExportResult
                {
                    Text = result.Text, Lines = result.Lines, Copies = result.Copies, FileName = result.FileName, Error = result.Error,
                    Note = result.Note.Length > 0 ? $"{note} {result.Note}" : note,
                };
            }
            catch (Exception ex)
            {
                return new ExportResult { Error = $"Could not export: {ex.Message}" };
            }
        }

        /// <summary>Printings that come ONLY as etched (v1 stored those copies as foil).</summary>
        private static HashSet<string> EtchedOnlySids()
        {
            using var db = new AppDbContext();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in db.PoolCards.AsNoTracking().Where(c => c.IsEtched && !c.IsFoil).Select(c => c.ScryfallId)) set.Add(s);
            foreach (var s in db.TokenCards.AsNoTracking().Where(c => c.IsEtched && !c.IsFoil).Select(c => c.ScryfallId)) set.Add(s);
            foreach (var s in db.OversizedCards.AsNoTracking().Where(c => c.IsEtched && !c.IsFoil).Select(c => c.ScryfallId)) set.Add(s);
            return set;
        }

        private static List<ExportRow> CollectionExportRows(bool onlyFree, bool tokens, bool oversized)
        {
            var eo = EtchedOnlySids();
            var list = new List<ExportRow>();
            using var db = new CollectionDbContext();
            // Cards always; Oversized and Tokens only when asked for. Oversized are display
            // cards, not playable, so a deck builder would treat them as real copies.
            var tables = new List<string> { CardsTable };
            if (oversized) tables.Add(OversizedTable);
            if (tokens) tables.Add(TokensTable);
            foreach (string table in tables)
            {
                foreach (var r in RowsMany(db, table, AllSids(db, table)))
                {
                    int used = Math.Max(0, r.UsedCount);
                    int qty = onlyFree ? r.Quantity - used : r.Quantity;
                    if (qty <= 0) continue;
                    var e = r.Entity;
                    string Str(string p) => e.GetType().GetProperty(p)?.GetValue(e) as string ?? "";
                    list.Add(new ExportRow
                    {
                        ScryfallId = r.ScryfallId,
                        Name = r.Name,
                        SetCode = Str("SetCode"),
                        SetName = Str("SetName"),
                        CollectorNumber = Str("CollectorNumber"),
                        Rarity = Str("Rarity"),
                        Finish = CardFinish.Shown(r.Finish, eo.Contains(r.ScryfallId)),
                        Language = CardLanguage.Normalize(r.Language),
                        Condition = CardCondition.Normalize(r.Condition),
                        Quantity = qty,
                        InUse = Math.Min(used, r.Quantity),
                        Price = r.Price,
                        Notes = r.Notes,
                        Storage = r.Storage,
                        IsToken = table == TokensTable,
                    });
                }
            }
            return list;
        }

        private static List<ExportRow> DeckExportRows(Deck deck, bool tokens)
        {
            var list = new List<ExportRow>();
            foreach (var c in deck.Cards)
            {
                if (c.IsFooter) continue;
                bool tokenLine = c.Category == DeckCardCategory.Tokens;
                if (tokenLine && !tokens) continue;
                // The commander may be flagged on its line rather than in the Commander part.
                string section = c.IsCommander && !tokenLine ? "Commander" : c.Category switch
                {
                    DeckCardCategory.Commander => "Commander",
                    DeckCardCategory.Sideboard => "Sideboard",
                    DeckCardCategory.Tokens => "Tokens",
                    _ => "Deck",
                };
                foreach (string finish in new[] { CardFinish.NonFoil, CardFinish.Foil, CardFinish.Etched })
                {
                    int n = c.CountOf(finish);
                    if (n <= 0) continue;
                    // An older deck file's "foil" copies of an etched-only printing are etched.
                    string f = finish == CardFinish.Foil && c.IsEtchedOnly ? CardFinish.Etched : finish;
                    list.Add(new ExportRow
                    {
                        Section = section,
                        ScryfallId = c.ScryfallId ?? "",
                        Name = c.Name ?? "",
                        SetCode = c.SetCode ?? "",
                        SetName = c.SetName ?? "",
                        CollectorNumber = c.CollectorNumber ?? "",
                        Rarity = c.Rarity ?? "",
                        Finish = f,
                        Quantity = n,
                        Price = f == CardFinish.NonFoil ? c.PriceUsd : f == CardFinish.Etched ? c.EtchedCopyPrice : c.FoilCopyPrice,
                        IsToken = tokenLine || c.IsToken,
                    });
                }
            }
            return list;
        }

        private static List<ExportRow> WantExportRows()
        {
            var eo = EtchedOnlySids();
            using var db = new CollectionDbContext();
            return db.WantListEntries.AsNoTracking().ToList()
                .Where(w => w.Quantity > 0)
                .Select(w => new ExportRow
                {
                    ScryfallId = w.ScryfallId ?? "",
                    Name = w.Name ?? "",
                    SetCode = w.SetCode ?? "",
                    SetName = w.SetName ?? "",
                    CollectorNumber = w.CollectorNumber ?? "",
                    Rarity = w.Rarity ?? "",
                    Finish = CardFinish.Shown(w.Finish, eo.Contains(w.ScryfallId ?? "")),
                    Quantity = w.Quantity,
                    Price = w.Price,
                    MyPrice = w.OfferPrice,
                    Notes = w.Notes ?? "",
                }).ToList();
        }

        private static List<ExportRow> BinderExportRows()
        {
            var eo = EtchedOnlySids();
            using var db = new CollectionDbContext();
            return db.TradeBinderEntries.AsNoTracking().ToList()
                .Where(b => b.Quantity > 0)
                .Select(b => new ExportRow
                {
                    ScryfallId = b.ScryfallId ?? "",
                    Name = b.Name ?? "",
                    SetCode = b.SetCode ?? "",
                    SetName = b.SetName ?? "",
                    CollectorNumber = b.CollectorNumber ?? "",
                    Rarity = b.Rarity ?? "",
                    Finish = CardFinish.Shown(b.Finish, eo.Contains(b.ScryfallId ?? "")),
                    Language = CardLanguage.Normalize(b.Language),
                    Condition = CardCondition.Normalize(b.Condition),
                    Quantity = b.Quantity,
                    Price = b.Price,
                    MyPrice = b.AskingPrice,
                    Notes = b.Notes ?? "",
                }).ToList();
        }
    }
}
