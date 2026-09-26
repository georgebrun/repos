using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;

namespace BreakersOfE.Models
{
    /// <summary>A pool card that can show how many copies you own.</summary>
    public interface IOwnedCard
    {
        string ScryfallId { get; }
        int OwnedNonFoil { get; set; }
        int OwnedFoil { get; set; }
    }
}

namespace BreakersOfE.Services
{
    /// <summary>
    /// Owned counts for pool pages (read-only): each pool page reads its
    /// matching collection table — Cards ← main collection, Tokens ← token
    /// collection, and so on — and fills OwnedNonFoil / OwnedFoil per printing.
    /// </summary>
    public static class OwnedCountService
    {
        public static void Fill(string poolTag, IEnumerable<object> rows)
        {
            var owned = Load(poolTag);
            foreach (var card in rows.OfType<IOwnedCard>())
            {
                if (owned.TryGetValue(card.ScryfallId ?? "", out var t))
                {
                    card.OwnedNonFoil = t.nonFoil;
                    card.OwnedFoil = t.foil;
                }
                else
                {
                    card.OwnedNonFoil = 0;
                    card.OwnedFoil = 0;
                }
            }
        }

        private static Dictionary<string, (int nonFoil, int foil)> Load(string poolTag)
        {
            var map = new Dictionary<string, (int nonFoil, int foil)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new CollectionDbContext();
                IEnumerable<(string sid, string finish, int qty)> rows = poolTag switch
                {
                    "Tokens" => db.TokenCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "Planes" => db.PlanarCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "Schemes" => db.SchemeCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "Vanguards" => db.VanguardCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "ArtSeries" => db.ArtSeriesCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "Conspiracies" => db.ConspiracyCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    _ => db.CollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                };

                foreach (var (sid, finish, qty) in rows)
                {
                    if (string.IsNullOrEmpty(sid) || qty <= 0) continue;
                    map.TryGetValue(sid, out var t);
                    if (CardFinish.Normalize(finish) == CardFinish.NonFoil) t.nonFoil += qty;
                    else t.foil += qty;
                    map[sid] = t;
                }
            }
            catch (Exception ex)
            {
                // No collection yet (or unreadable): nothing shows as owned.
                System.Diagnostics.Debug.WriteLine($"Owned counts ({poolTag}): {ex.Message}");
            }
            return map;
        }
    }
}