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
        bool IsFoil { get; }
        bool IsEtched { get; }
        int OwnedNonFoil { get; set; }
        int OwnedFoil { get; set; }
        int OwnedEtched { get; set; }
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
        /// <summary>"MtgoCards" → "mtgo", "ArenaCards" → "arena", else null (paper).</summary>
        public static string? OnlineGameOf(string poolTag) => poolTag switch
        {
            "MtgoCards" => OnlineGame.Mtgo,
            "ArenaCards" => OnlineGame.Arena,
            _ => null,
        };

        public static void Fill(string poolTag, IEnumerable<object> rows)
        {
            var owned = Load(poolTag);
            bool online = OnlineGameOf(poolTag) != null;
            foreach (var card in rows.OfType<IOwnedCard>())
            {
                if (owned.TryGetValue(card.ScryfallId ?? "", out var t))
                {
                    // v1 stored etched-only printings as foil rows: count them as
                    // etched. (Paper only — online rows are stored as they are.)
                    bool etchedOnly = !online && card.IsEtched && !card.IsFoil;
                    card.OwnedNonFoil = t.nonFoil;
                    card.OwnedFoil = etchedOnly ? 0 : t.foil;
                    card.OwnedEtched = t.etched + (etchedOnly ? t.foil : 0);
                }
                else
                {
                    card.OwnedNonFoil = 0;
                    card.OwnedFoil = 0;
                    card.OwnedEtched = 0;
                }
            }
        }

        private static Dictionary<string, (int nonFoil, int foil, int etched)> Load(string poolTag)
        {
            var map = new Dictionary<string, (int nonFoil, int foil, int etched)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new CollectionDbContext();
                string? game = OnlineGameOf(poolTag);
                IEnumerable<(string sid, string finish, int qty)> rows = poolTag switch
                {
                    // Online pools ← that game's online collection
                    _ when game != null => db.OnlineCollectionEntries.Where(e => e.Game == game)
                                  .Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
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
                    "Oversized" => db.OversizedCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    "FrontCards" => db.FrontCollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                    _ => db.CollectionEntries.Select(e => new { e.ScryfallId, e.Finish, e.Quantity })
                                  .ToList().Select(e => (e.ScryfallId, e.Finish, e.Quantity)),
                };

                foreach (var (sid, finish, qty) in rows)
                {
                    if (string.IsNullOrEmpty(sid) || qty <= 0) continue;
                    map.TryGetValue(sid, out var t);
                    switch (CardFinish.Normalize(finish))
                    {
                        case CardFinish.Foil: t.foil += qty; break;
                        case CardFinish.Etched: t.etched += qty; break;
                        default: t.nonFoil += qty; break;
                    }
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
