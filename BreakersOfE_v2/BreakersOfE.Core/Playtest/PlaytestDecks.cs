using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Playtest
{
    /// <summary>What a deck needs from the card pool to be played: each printing's layout.</summary>
    public static class PlaytestDecks
    {
        /// <summary>
        /// Scryfall's layout of each printing in the deck ("transform",
        /// "modal_dfc", "flip" …), by ScryfallId: one look in the pool for the
        /// cards and one for the tokens. Missing ones are left out (played as
        /// plain cards).
        /// </summary>
        public static Dictionary<string, string> Layouts(Deck deck)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var cardIds = deck.Cards.Where(c => !c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId))
                                        .Select(c => c.ScryfallId).Distinct().ToList();
                var tokenIds = deck.Cards.Where(c => c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId))
                                         .Select(c => c.ScryfallId).Distinct().ToList();
                using var db = new AppDbContext();
                foreach (var r in db.PoolCards.AsNoTracking().Where(p => cardIds.Contains(p.ScryfallId))
                                    .Select(p => new { p.ScryfallId, p.Layout }).ToList())
                    result[r.ScryfallId] = r.Layout ?? "";
                foreach (var r in db.TokenCards.AsNoTracking().Where(p => tokenIds.Contains(p.ScryfallId))
                                    .Select(p => new { p.ScryfallId, p.Layout }).ToList())
                    result[r.ScryfallId] = r.Layout ?? "";
            }
            catch
            {
                // No card pool yet: everything plays as a plain card (turning over still works from the picture).
            }
            return result;
        }

        /// <summary>Printed loyalty / defense of each planeswalker and battle in the deck, by ScryfallId (from the card pool): what they enter with.</summary>
        public static Dictionary<string, string> Loyalties(Deck deck)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var ids = deck.Cards.Where(c => !c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId))
                                    .Select(c => c.ScryfallId).Distinct().ToList();
                using var db = new AppDbContext();
                foreach (var r in db.PoolCards.AsNoTracking().Where(p => ids.Contains(p.ScryfallId) && p.LoyaltyOrDefense != "")
                                    .Select(p => new { p.ScryfallId, p.LoyaltyOrDefense }).ToList())
                    result[r.ScryfallId] = r.LoyaltyOrDefense ?? "";
            }
            catch
            {
                // No card pool yet: planeswalkers enter with no counters (add them from Counters).
            }
            return result;
        }

        /// <summary>
        /// The mana each printing in the deck makes (Scryfall produced_mana), by
        /// ScryfallId, cards and tokens: the table checks its choices against it.
        /// Empty until a Full Database Update fills the column.
        /// </summary>
        public static Dictionary<string, string> Produced(Deck deck)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var ids = deck.Cards.Where(c => !string.IsNullOrEmpty(c.ScryfallId)).Select(c => c.ScryfallId).Distinct().ToList();
                using var db = new AppDbContext();
                foreach (var r in db.PoolCards.AsNoTracking().Where(p => ids.Contains(p.ScryfallId) && p.ProducedMana != "")
                                    .Select(p => new { p.ScryfallId, p.ProducedMana }).ToList())
                    result[r.ScryfallId] = r.ProducedMana;
                foreach (var r in db.TokenCards.AsNoTracking().Where(p => ids.Contains(p.ScryfallId) && p.ProducedMana != "")
                                    .Select(p => new { p.ScryfallId, p.ProducedMana }).ToList())
                    result[r.ScryfallId] = r.ProducedMana;
            }
            catch
            {
                // No card pool yet: the table reads mana from the rules text alone.
            }
            return result;
        }

        private static List<TokenCard>? _tokens;

        /// <summary>Every token in the card pool (read once): for Tokens ▾ → Any Token….</summary>
        public static List<TokenCard> AllTokens()
        {
            if (_tokens != null) return _tokens;
            try
            {
                using var db = new AppDbContext();
                _tokens = db.TokenCards.AsNoTracking().ToList();
                ManaSources.Register(_tokens.Select(t => new KeyValuePair<string, string>(t.ScryfallId, t.ProducedMana)));
            }
            catch
            {
                return new List<TokenCard>();                              // no card pool yet: try again next time
            }
            return _tokens;
        }

        /// <summary>Life to start with: 40 in Commander-style decks (Brawl 25, Oathbreaker 20, Pauper Commander 30), else 20.</summary>
        public static int DefaultLife(Deck deck) => deck.DeckType switch
        {
            DeckType.Commander => 40,
            DeckType.PauperCommander => 30,
            DeckType.Brawl => 25,
            DeckType.StandardBrawl => 25,
            DeckType.DuelCommander => 20,
            DeckType.Oathbreaker => 20,
            _ => 20,
        };
    }
}
