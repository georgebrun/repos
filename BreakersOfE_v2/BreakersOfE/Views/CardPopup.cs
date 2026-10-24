using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BreakersOfE.Services;

namespace BreakersOfE.Views
{
    /// <summary>
    /// The larger card window (the one Card Pool and the Edit pages open) for
    /// lists of card names — the Build page's draft and Guided Build's options.
    /// Prev / Next walk the list; <paramref name="onCard"/> gets the name shown,
    /// so the list can follow. One window per list at a time.
    /// </summary>
    internal sealed class CardPopup
    {
        private CardDetailWindow? _window;

        public void Open(Window? owner, IReadOnlyList<string> names, int index, Action<string>? onCard = null)
        {
            if (index < 0 || index >= names.Count) return;
            _window?.Close();
            // The pool card for each name (the printing the card panel shows).
            var cards = new List<object>();
            var shown = new List<string>();
            int at = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (DeckBuildService.PoolCardByName(names[i]) is not { } card) continue;
                if (i == index) at = cards.Count;
                cards.Add(card);
                shown.Add(names[i]);
            }
            if (at < 0) return;
            var w = new CardDetailWindow(cards[at], owner, cards, at);
            w.CardChanged += c => { int i = cards.IndexOf(c); if (i >= 0) onCard?.Invoke(shown[i]); };
            w.Closed += (_, _) => { if (_window == w) _window = null; };
            _window = w;
            w.Show();
        }
    }
}
