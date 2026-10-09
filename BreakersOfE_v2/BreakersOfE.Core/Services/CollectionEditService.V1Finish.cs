using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>A printing whose foil copies v1 couldn't tell from etched: the review list asks which they are.</summary>
    public sealed record FinishQuestion(string ScryfallId, string Name, string SetCode, string CollectorNumber,
                                        string ImageUrl, int Copies, int InDecks);

    /// <summary>
    /// After v1 → v2 (part 2, once the card data is in): v1 stored etched
    /// copies as foil. Printings that only come in etched are set to etched
    /// straight away; printings that come in BOTH are asked about (the review
    /// list) — never guessed.
    /// </summary>
    public static partial class CollectionEditService
    {
        /// <summary>
        /// Every printing with foil copies (collection, Trade Binder, Want List
        /// or a deck): etched-only ones set to etched now (returned: how many);
        /// foil-and-etched ones returned to ask about.
        /// </summary>
        public static (int EtchedOnlyFixed, List<FinishQuestion> ToAsk) PrepareV1FinishReview()
        {
            // Foil copies, per printing: collection rows, the two lists, and the decks.
            var copies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var inDecks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var db = new CollectionDbContext())
            {
                foreach (var e in db.CollectionEntries.AsNoTracking().Where(e => e.Finish == CardFinish.Foil)
                             .Select(e => new { e.ScryfallId, e.Quantity }).ToList())
                    copies[e.ScryfallId] = copies.GetValueOrDefault(e.ScryfallId) + e.Quantity;
                foreach (var sid in db.TradeBinderEntries.AsNoTracking().Where(e => e.Finish == CardFinish.Foil).Select(e => e.ScryfallId)
                             .Concat(db.WantListEntries.AsNoTracking().Where(e => e.Finish == CardFinish.Foil).Select(e => e.ScryfallId)).ToList())
                    copies.TryAdd(sid, 0);
            }
            foreach (var (_, deck) in DeckIndexService.AllDecks())
                foreach (var c in deck.Cards.Where(c => !c.IsTokenLine && c.FoilQuantity > 0 && !string.IsNullOrEmpty(c.ScryfallId)))
                    inDecks[c.ScryfallId] = inDecks.GetValueOrDefault(c.ScryfallId) + c.FoilQuantity;

            var sids = copies.Keys.Union(inDecks.Keys, StringComparer.OrdinalIgnoreCase).Where(s => s.Length > 0).ToList();
            if (sids.Count == 0) return (0, new List<FinishQuestion>());

            // What each printing really comes in (the new card data).
            List<PoolCard> pool;
            using (var pdb = new AppDbContext())
                pool = pdb.PoolCards.AsNoTracking().Where(p => sids.Contains(p.ScryfallId)).ToList();
            var byId = pool.GroupBy(p => p.ScryfallId, StringComparer.OrdinalIgnoreCase)
                           .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var etchedOnly = sids.Where(s => byId.TryGetValue(s, out var p) && p.IsEtched && !p.IsFoil).ToList();
            int fixedCount = etchedOnly.Count > 0 ? FoilToEtched(etchedOnly) : 0;

            var ask = sids.Where(s => byId.TryGetValue(s, out var p) && p.IsEtched && p.IsFoil)
                .Select(s => byId[s])
                .Select(p => new FinishQuestion(p.ScryfallId, p.Name, p.SetCode, p.CollectorNumber,
                    p.ImageSmallUrl ?? "", copies.GetValueOrDefault(p.ScryfallId), inDecks.GetValueOrDefault(p.ScryfallId)))
                .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ThenBy(q => q.SetCode).ToList();
            return (fixedCount, ask);
        }

        /// <summary>
        /// These printings' foil copies are really etched: collection rows,
        /// Trade Binder and Want List rows, deck and binder claims, and the
        /// decks' foil counts all move to etched (a row merges into the etched
        /// row of the same language and condition, if there is one). Returns
        /// how many printings changed.
        /// </summary>
        public static int FoilToEtched(IReadOnlyCollection<string> scryfallIds)
        {
            var set = new HashSet<string>(scryfallIds.Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0) return 0;
            var list = set.ToList();

            using (var db = new CollectionDbContext())
            {
                // Collection rows.
                var rows = db.CollectionEntries.Where(e => list.Contains(e.ScryfallId)).ToList();
                foreach (var foil in rows.Where(r => r.Finish == CardFinish.Foil).ToList())
                {
                    var etched = rows.FirstOrDefault(r => r != foil && r.Finish == CardFinish.Etched &&
                        string.Equals(r.ScryfallId, foil.ScryfallId, StringComparison.OrdinalIgnoreCase) &&
                        CardLanguage.Normalize(r.Language) == CardLanguage.Normalize(foil.Language) &&
                        CardCondition.Normalize(r.Condition) == CardCondition.Normalize(foil.Condition));
                    if (etched != null)
                    {
                        etched.Quantity += foil.Quantity;
                        etched.DateModified = DateTime.Now;
                        db.CollectionEntries.Remove(foil);
                        rows.Remove(foil);
                    }
                    else
                    {
                        foil.Finish = CardFinish.Etched;
                        foil.Price = CardFinish.PriceFor(CardFinish.Etched, foil.PriceUsd, foil.PriceUsdFoil, foil.PriceUsdEtched);
                        foil.DateModified = DateTime.Now;
                    }
                }

                // Trade Binder and Want List rows (the binder's copies are claims, moved below).
                foreach (var b in db.TradeBinderEntries.Where(e => list.Contains(e.ScryfallId) && e.Finish == CardFinish.Foil).ToList())
                    b.Finish = CardFinish.Etched;
                var wants = db.WantListEntries.Where(e => list.Contains(e.ScryfallId)).ToList();
                foreach (var w in wants.Where(w => w.Finish == CardFinish.Foil).ToList())
                {
                    var etched = wants.FirstOrDefault(x => x != w && x.Finish == CardFinish.Etched &&
                        string.Equals(x.ScryfallId, w.ScryfallId, StringComparison.OrdinalIgnoreCase));
                    if (etched != null)
                    {
                        etched.Quantity += w.Quantity;
                        db.WantListEntries.Remove(w);
                        wants.Remove(w);
                    }
                    else w.Finish = CardFinish.Etched;
                }

                // Deck and binder claims: their foil copies are etched copies.
                foreach (var u in db.DeckUsages.Where(u => list.Contains(u.ScryfallId) && u.EnteredFoil > 0).ToList())
                {
                    u.EnteredEtched += u.EnteredFoil;
                    u.EnteredFoil = 0;
                }
                db.SaveChanges();

                // Used counts from the moved claims.
                var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                Recompute(db, CardsTable, list, cache);
                db.SaveChanges();
            }

            // The decks' own counts.
            foreach (var (path, indexed) in DeckIndexService.AllDecks())
            {
                if (!indexed.Cards.Any(c => !c.IsTokenLine && set.Contains(c.ScryfallId ?? "") && c.FoilQuantity > 0)) continue;
                var deck = DeckService.Load(path);
                if (deck == null) continue;
                bool changed = false;
                foreach (var c in deck.Cards.Where(c => !c.IsTokenLine && set.Contains(c.ScryfallId ?? "") && c.FoilQuantity > 0))
                {
                    c.EtchedQuantity += c.FoilQuantity;
                    c.FoilQuantity = 0;
                    changed = true;
                }
                if (changed) DeckService.Save(deck);
            }
            return set.Count;
        }
    }
}
