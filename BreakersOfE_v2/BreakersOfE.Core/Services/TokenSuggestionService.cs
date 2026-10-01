using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Services
{
    /// <summary>One token a deck's cards make (Edit → Decks → Suggested Tokens).</summary>
    public sealed class TokenSuggestion
    {
        /// <summary>The printing offered (the one most of the deck's cards link to).</summary>
        public TokenCard Token { get; init; } = new();
        /// <summary>The deck's cards that make it (names, A→Z).</summary>
        public List<string> MadeBy { get; init; } = new();
        /// <summary>Copies of this token already in the deck's Tokens part (any printing).</summary>
        public int InDeck { get; init; }
        /// <summary>Copies of this token in your token collection (any printing).</summary>
        public int Owned { get; init; }
        /// <summary>Of those, copies of the printing offered.</summary>
        public int OwnedOffered { get; init; }
        /// <summary>Ticked in the list (two-way bound).</summary>
        public bool IsChecked { get; set; }

        /// <summary>"Treasure · Colorless · Artifact — Treasure · NEO #14".</summary>
        public string Title
        {
            get
            {
                var t = Token;
                string pt = !string.IsNullOrWhiteSpace(t.Power) || !string.IsNullOrWhiteSpace(t.Toughness)
                    ? $" {t.Power}/{t.Toughness}" : "";
                return $"{t.Name}{pt}  ·  {ColorText(t.Colors)}  ·  {t.TypeLine}  ·  {t.SetCode.ToUpperInvariant()} #{t.CollectorNumber}";
            }
        }

        /// <summary>"Made by Dockside Extortionist, Smothering Tithe · 2 already in the deck".</summary>
        public string Detail =>
            $"Made by {string.Join(", ", MadeBy)}" +
            (Owned == 0 ? "  ·  not in your collection"
             : Owned == OwnedOffered ? $"  ·  you own {Owned}"
             : $"  ·  you own {Owned} ({OwnedOffered} of this printing)") +
            (InDeck > 0 ? $"  ·  {InDeck} already in the deck" : "");

        private static string ColorText(string colors)
        {
            var map = new Dictionary<char, string> { ['W'] = "White", ['U'] = "Blue", ['B'] = "Black", ['R'] = "Red", ['G'] = "Green" };
            var names = "WUBRG".Where(colors.Contains).Select(c => map[c]).ToList();
            return names.Count == 0 ? "Colorless" : string.Join("/", names);
        }
    }

    /// <summary>
    /// Which tokens a deck's cards make, as Scryfall links them (each pool
    /// card's TokenIds, from "all_parts"). Only linked tokens: cards that copy
    /// something ("create a token that's a copy of…") or older sets often have
    /// none. One entry per token (different printings of the same token count
    /// as one). The printing offered: the one you own most copies of (any
    /// printing of that token in your token collection), else the one most of
    /// the cards link to.
    /// </summary>
    public static class TokenSuggestionService
    {
        /// <summary>
        /// Suggestions for <paramref name="makers"/> (the deck's cards, or a few
        /// of them). <paramref name="noLinkData"/>: the card pool has no token
        /// links at all yet (a Full Database Update brings them).
        /// </summary>
        public static List<TokenSuggestion> Suggest(Deck deck, IEnumerable<DeckCard> makers, out bool noLinkData)
        {
            noLinkData = false;
            var sources = makers.Where(c => !c.IsTokenLine && !string.IsNullOrEmpty(c.ScryfallId)).ToList();
            var sids = sources.Select(c => c.ScryfallId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sids.Count == 0) return new List<TokenSuggestion>();

            using var db = new AppDbContext();
            noLinkData = !db.PoolCards.Any(p => p.TokenIds != "");
            if (noLinkData) return new List<TokenSuggestion>();

            // Token printing → the cards that link to it.
            var makersByToken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in db.PoolCards.AsNoTracking()
                         .Where(p => sids.Contains(p.ScryfallId) && p.TokenIds != "")
                         .Select(p => new { p.Name, p.TokenIds }).ToList())
                foreach (var id in link.TokenIds.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!makersByToken.TryGetValue(id, out var set))
                        makersByToken[id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(link.Name);
                }
            if (makersByToken.Count == 0) return new List<TokenSuggestion>();

            var ids = makersByToken.Keys.ToList();
            var tokens = db.TokenCards.AsNoTracking().Where(t => ids.Contains(t.ScryfallId)).ToList();

            // Every printing of these tokens (you may own another printing than
            // the one the cards link to), and how many of each you own.
            var oracleIds = tokens.Select(t => t.OracleId).Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
            var allPrintings = oracleIds.Count == 0 ? new List<TokenCard>()
                : db.TokenCards.AsNoTracking().Where(t => oracleIds.Contains(t.OracleId)).ToList();
            var owned = OwnedTokens(allPrintings.Select(t => t.ScryfallId).Concat(ids).Distinct().ToList());

            var deckTokens = deck.Cards.Where(c => c.IsTokenLine).ToList();
            var list = new List<TokenSuggestion>();
            // One entry per token: same Oracle card (else same name, P/T and colors).
            foreach (var g in tokens.GroupBy(TokenKey))
            {
                var linked = g.ToList();
                var every = string.IsNullOrEmpty(g.First().OracleId)
                    ? linked
                    : allPrintings.Where(t => t.OracleId == g.First().OracleId)
                                  .Concat(linked).GroupBy(t => t.ScryfallId).Select(x => x.First()).ToList();
                int Links(TokenCard t) => makersByToken.TryGetValue(t.ScryfallId, out var m) ? m.Count : 0;
                var pick = every
                    .OrderByDescending(t => owned.GetValueOrDefault(t.ScryfallId))   // yours first
                    .ThenByDescending(Links)                                         // then the linked one
                    .ThenByDescending(t => t.ReleasedAt, StringComparer.Ordinal)
                    .First();
                var printings = linked;
                var madeBy = printings.SelectMany(t => makersByToken[t.ScryfallId])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                var printingIds = new HashSet<string>(every.Select(t => t.ScryfallId), StringComparer.OrdinalIgnoreCase);
                int inDeck = deckTokens
                    .Where(c => printingIds.Contains(c.ScryfallId) || SameToken(c, pick))
                    .Sum(c => c.TotalQuantity);
                list.Add(new TokenSuggestion
                {
                    Token = pick,
                    MadeBy = madeBy,
                    InDeck = inDeck,
                    Owned = every.Sum(t => owned.GetValueOrDefault(t.ScryfallId)),
                    OwnedOffered = owned.GetValueOrDefault(pick.ScryfallId),
                    IsChecked = inDeck == 0,          // ticked unless the deck already has it
                });
            }
            return list
                .OrderBy(s => s.Token.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Token.Power)
                .ToList();
        }

        /// <summary>
        /// Token printing → copies in your token collection. A read error is
        /// NOT hidden (it would show every token as "not in your collection"):
        /// it goes up to the page, which shows it.
        /// </summary>
        private static Dictionary<string, int> OwnedTokens(List<string> sids)
        {
            if (sids.Count == 0) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cdb = new CollectionDbContext();
                return cdb.TokenCollectionEntries
                    .Where(e => e.Quantity > 0 && sids.Contains(e.ScryfallId))
                    .Select(e => new { e.ScryfallId, e.Quantity })
                    .ToList()
                    .GroupBy(e => e.ScryfallId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"your token collection couldn't be read ({ex.Message})", ex);
            }
        }

        private static string TokenKey(TokenCard t) =>
            !string.IsNullOrEmpty(t.OracleId)
                ? t.OracleId
                : $"{t.Name}|{t.Power}|{t.Toughness}|{t.Colors}|{t.TypeLine}";

        /// <summary>A deck token line that's the same token as <paramref name="t"/> (another printing).</summary>
        private static bool SameToken(DeckCard c, TokenCard t) =>
            string.Equals(c.Name, t.Name, StringComparison.OrdinalIgnoreCase) &&
            c.Power == t.Power && c.Toughness == t.Toughness &&
            c.ColorIdentity == t.ColorIdentity &&
            string.Equals(c.TypeLine, t.TypeLine, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.OracleText, t.OracleText, StringComparison.Ordinal);
    }
}