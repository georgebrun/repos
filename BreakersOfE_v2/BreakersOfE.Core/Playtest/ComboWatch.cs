using System;
using System.Collections.Generic;
using System.Linq;
using BreakersOfE.Models;
using BreakersOfE.Services;

namespace BreakersOfE.Playtest
{
    /// <summary>
    /// The deck's Commander Spellbook combos, watched during play: a combo is
    /// "ready" when every piece is on the battlefield, or (for an instant or
    /// sorcery) in the hand or on the stack. Each one is announced once when
    /// it comes together, and again only after it falls apart and comes back.
    /// </summary>
    public sealed class ComboWatch
    {
        private readonly List<Combo> _combos;
        private readonly HashSet<string> _ready = new();

        public ComboWatch(Deck deck)
        {
            try
            {
                _combos = DeckDataService.HasCombos
                    ? DeckDataService.CombosWithin(deck.PlayCards.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                    : new List<Combo>();
            }
            catch
            {
                _combos = new List<Combo>();                     // no combos file: nothing to watch
            }
        }

        public ComboWatch(IEnumerable<Combo> combos) => _combos = combos.ToList();

        /// <summary>How many combos the deck holds.</summary>
        public int Count => _combos.Count;

        /// <summary>The combos that just came together (not ready before this check).</summary>
        public List<Combo> Check(Seat seat)
        {
            var onBoard = new HashSet<string>(seat.Battlefield.Where(c => !c.FaceDown).Select(c => DeckDataService.FrontName(c.Card.Name)),
                                              StringComparer.OrdinalIgnoreCase);
            var spells = new HashSet<string>(seat.Hand.Concat(seat.Stack)
                                                 .Where(c => c.Card.TypeLine.Contains("Instant", StringComparison.OrdinalIgnoreCase) ||
                                                             c.Card.TypeLine.Contains("Sorcery", StringComparison.OrdinalIgnoreCase))
                                                 .Select(c => DeckDataService.FrontName(c.Card.Name)),
                                             StringComparer.OrdinalIgnoreCase);
            var fresh = new List<Combo>();
            foreach (var combo in _combos)
            {
                bool ready = combo.Cards.Count > 0 && combo.Cards.All(n =>
                {
                    string f = DeckDataService.FrontName(n);
                    return onBoard.Contains(f) || spells.Contains(f);
                });
                if (ready && _ready.Add(combo.Id)) fresh.Add(combo);
                else if (!ready) _ready.Remove(combo.Id);
            }
            return fresh;
        }

        /// <summary>Forget what was announced (a new game).</summary>
        public void Reset() => _ready.Clear();
    }
}
