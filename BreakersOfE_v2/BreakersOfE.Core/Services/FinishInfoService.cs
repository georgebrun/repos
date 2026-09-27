using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Finish facts from the card pool for collection-type rows (read-only):
    /// does the printing exist as etched, and ONLY as etched? A v1 foil row
    /// of an etched-only printing is then shown and priced as etched.
    /// </summary>
    public static class FinishInfoService
    {
        /// <summary>Etched facts per ScryfallId for one pool table.</summary>
        public static Dictionary<string, (bool etched, bool etchedOnly)> Load(string tableTag, IEnumerable<string> scryfallIds)
        {
            var map = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
            var ids = scryfallIds.Where(s => !string.IsNullOrEmpty(s))
                                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0) return map;

            try
            {
                using var db = new AppDbContext();
                for (int i = 0; i < ids.Count; i += 500)
                {
                    var chunk = ids.Skip(i).Take(500).ToList();
                    IEnumerable<(string id, bool foil, bool etched)> rows = tableTag switch
                    {
                        "CollTokens" => db.TokenCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        "CollPlanes" => db.PlanarCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        "CollSchemes" => db.SchemeCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        "CollVanguards" => db.VanguardCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        "CollArtSeries" => db.ArtSeriesCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        "CollConspiracies" => db.ConspiracyCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                        // Collection, Trade Binder, Want List, decks: the main Cards pool
                        _ => db.PoolCards.Where(c => chunk.Contains(c.ScryfallId))
                            .Select(c => new { c.ScryfallId, c.IsFoil, c.IsEtched }).ToList()
                            .Select(c => (c.ScryfallId, c.IsFoil, c.IsEtched)),
                    };
                    foreach (var (id, foil, etched) in rows)
                        map[id] = (etched, etched && !foil);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Finish info ({tableTag}): {ex.Message}");
            }
            return map;
        }

        /// <summary>Fill IsEtched / PrintingEtchedOnly on collection-type rows.</summary>
        public static void Fill(string tableTag, IEnumerable<object> rows)
        {
            var list = rows.OfType<IFinishRow>().ToList();
            if (list.Count == 0) return;
            var map = Load(tableTag, list.Select(r => r.ScryfallId));
            foreach (var r in list)
            {
                var (etched, etchedOnly) = map.GetValueOrDefault(r.ScryfallId ?? "");
                r.IsEtched = etched;
                r.PrintingEtchedOnly = etchedOnly;
            }
        }
    }
}
