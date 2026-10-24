using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Suggested Tokens → Add Ticked: the tokens go to the deck's Tokens part
    /// (never counted as part of the deck), and the token copies you own are
    /// claimed for it. Shared by Edit → Decks and the Build page (after Accept).
    /// The deck must be saved (claims are keyed on its id); the caller saves it again after.
    /// </summary>
    public static class DeckTokenService
    {
        /// <summary>The finish a new token line gets: non-foil when the printing comes in it.</summary>
        public static string DefaultFinish(DeckCard card) =>
            card.IsNonFoil ? CardFinish.NonFoil : card.IsFoil ? CardFinish.Foil
            : card.IsEtched ? CardFinish.Etched : CardFinish.NonFoil;

        public static List<DeckEditResult> AddTokens(Deck deck, IEnumerable<TokenSuggestion> picked, int copies) =>
            picked.Select(s =>
            {
                var t = DeckService.FromTokenCard(s.Token);
                string fin = DefaultFinish(t);
                var added = DeckEditService.Add(deck, t, fin, copies, DeckCardCategory.Tokens);
                if (added.Changed <= 0 || s.Owned == 0) return added;
                var claimed = CollectionEditService.ClaimOwned(CollectionEditService.TokensTable, deck,
                                                               t.ScryfallId, fin, copies, t.Name);
                return claimed.Changed > 0
                    ? new DeckEditResult
                    {
                        Changed = added.Changed,
                        Touched = added.Touched,
                        Warning = claimed.Warning,
                        Message = $"{added.Message} {claimed.Message}",
                    }
                    : added;
            }).ToList();
    }
}
