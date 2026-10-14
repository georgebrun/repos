using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Exports for Magic Online and MTG Arena: the online collections as they
    /// are, and a deck turned into printings that game has (a paper-only
    /// printing becomes the newest printing of the same card in that game).
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>The MTGO or Arena collection's rows.</summary>
        private static List<ExportRow> OnlineExportRows(string game)
        {
            using var db = new CollectionDbContext();
            return db.OnlineCollectionEntries.AsNoTracking().Where(e => e.Game == game && e.Quantity > 0).ToList()
                .Select(e => new ExportRow
                {
                    ScryfallId = e.ScryfallId,
                    Name = e.Name,
                    SetCode = e.SetCode,
                    SetName = e.SetName,
                    CollectorNumber = e.CollectorNumber,
                    Rarity = e.Rarity,
                    Finish = CardFinish.Normalize(e.Finish),
                    Quantity = e.Quantity,
                    Price = e.Price,
                    Notes = e.Notes,
                    Layout = e.Layout,
                    MtgoId = CardFinish.Normalize(e.Finish) == CardFinish.NonFoil ? e.MtgoId ?? e.MtgoFoilId : e.MtgoFoilId ?? e.MtgoId,
                }).ToList();
        }

        private sealed record GamePrinting(string ScryfallId, string Name, string SetCode, string SetName, string CollectorNumber,
                                           string Rarity, string Layout, string ReleasedAt, int? MtgoId, int? MtgoFoilId);

        /// <summary>
        /// A deck's rows as printings of <paramref name="game"/>: the same
        /// printing when that game has it, else the newest printing of the same
        /// card there. MTGO foils keep their foil id when there is one. Cards
        /// the game doesn't have at all stay (Arena: by name) and are listed in
        /// <paramref name="note"/>; tokens are left out (neither game imports them).
        /// </summary>
        private static List<ExportRow> ToGame(List<ExportRow> rows, string game, out string note)
        {
            bool arena = game == OnlineGame.Arena;
            List<GamePrinting> pool;
            using (var db = new OnlineDbContext())
            {
                var q = arena ? db.OnlineCards.AsNoTracking().Where(c => c.IsOnArena)
                              : db.OnlineCards.AsNoTracking().Where(c => c.IsOnMtgo);
                pool = q.Select(c => new GamePrinting(c.ScryfallId, c.Name, c.SetCode, c.SetName, c.CollectorNumber,
                                                      c.Rarity, c.Layout, c.ReleasedAt, c.MtgoId, c.MtgoFoilId))
                        .ToList();
            }
            var bySid = new Dictionary<string, GamePrinting>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, GamePrinting>();
            foreach (var p in pool)
            {
                if (!arena && p.MtgoId == null && p.MtgoFoilId == null) continue;      // no id: MTGO can't load it
                bySid.TryAdd(p.ScryfallId, p);
                string key = CardMatcher.NameKey(p.Name);
                // The newest printing stands for the card (ISO dates compare as text).
                if (!byName.TryGetValue(key, out var have) || string.CompareOrdinal(p.ReleasedAt ?? "", have.ReleasedAt ?? "") > 0)
                    byName[key] = p;
            }

            var result = new List<ExportRow>();
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int swapped = 0, tokens = 0;
            foreach (var r in rows)
            {
                if (r.IsToken || r.Section == "Tokens") { tokens++; continue; }
                bool same = bySid.TryGetValue(r.ScryfallId, out var p);
                if (!same) byName.TryGetValue(CardMatcher.NameKey(r.Name), out p);
                if (p == null)
                {
                    missing.Add(r.Name);
                    if (arena) result.Add(r);              // Arena: the name alone (Arena says what it can't find)
                    continue;
                }
                if (!same) swapped++;
                bool foil = r.Finish != CardFinish.NonFoil && !arena;
                result.Add(new ExportRow
                {
                    Section = r.Section,
                    ScryfallId = p.ScryfallId,
                    Name = p.Name,
                    SetCode = p.SetCode,
                    SetName = p.SetName,
                    CollectorNumber = p.CollectorNumber,
                    Rarity = p.Rarity,
                    Finish = foil && p.MtgoFoilId != null ? CardFinish.Foil : CardFinish.NonFoil,
                    Quantity = r.Quantity,
                    Layout = p.Layout,
                    MtgoId = foil && p.MtgoFoilId != null ? p.MtgoFoilId : p.MtgoId ?? p.MtgoFoilId,
                });
            }

            var notes = new List<string>();
            string where = arena ? "Arena" : "MTGO";
            if (swapped > 0)
                notes.Add($"{swapped} card{(swapped == 1 ? "" : "s")} use{(swapped == 1 ? "s" : "")} another printing ({where} doesn't have the deck's one).");
            if (missing.Count > 0)
                notes.Add($"Not on {where}: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" and {missing.Count - 5} more" : "")}" +
                          (arena ? " (listed by name; Arena will say it can't find them)." : " (left out)."));
            if (tokens > 0) notes.Add($"Tokens are left out ({where} makes its own).");
            note = string.Join(" ", notes);
            return result;
        }
    }
}
