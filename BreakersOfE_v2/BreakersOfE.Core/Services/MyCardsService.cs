using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BreakersOfE.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>
    /// "My cards": every printing (Scryfall ID) in your collection tables,
    /// Trade Binder, Want List and decks. Decides which card pictures are
    /// kept on disk when Settings says "only my cards". Rebuilt in the
    /// background when a minute old; until then the last list is used.
    /// </summary>
    public static class MyCardsService
    {
        private static HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _built = DateTime.MinValue;
        private static int _building;

        /// <summary>
        /// True when this printing is one of yours. Never waits: the list is
        /// (re)built in the background — first use, or when a minute old —
        /// and until the first build is in, the answer is "no" (the background
        /// picture download catches anything missed).
        /// </summary>
        public static bool Contains(string? scryfallId)
        {
            if (string.IsNullOrEmpty(scryfallId)) return false;
            if (DateTime.UtcNow - _built > TimeSpan.FromMinutes(1) &&
                System.Threading.Interlocked.CompareExchange(ref _building, 1, 0) == 0)
                Task.Run(() => { try { Rebuild(); } finally { _building = 0; } });
            return _ids.Contains(scryfallId);
        }

        /// <summary>Every printing of yours, built fresh.</summary>
        public static IReadOnlyCollection<string> All()
        {
            Rebuild();
            return _ids;
        }

        private static void Rebuild()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var db = new CollectionDbContext();
                ids.UnionWith(db.CollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.TokenCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.PlanarCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.SchemeCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.VanguardCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.ArtSeriesCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.ConspiracyCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.OversizedCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.FrontCollectionEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.TradeBinderEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
                ids.UnionWith(db.WantListEntries.AsNoTracking().Select(e => e.ScryfallId).ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"My cards (collection): {ex.Message}");
            }
            try
            {
                foreach (var (_, deck) in DeckIndexService.AllDecks())
                    foreach (var c in deck.Cards)
                        if (!string.IsNullOrEmpty(c.ScryfallId)) ids.Add(c.ScryfallId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"My cards (decks): {ex.Message}");
            }
            ids.Remove("");
            _ids = ids;
            _built = DateTime.UtcNow;
        }
    }
}
