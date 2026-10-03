using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BreakersOfE.Models;
using BreakersOfE.Services;
using BreakersOfE.Views.Dialogs;

namespace BreakersOfE.Views.Pages
{
    // Deck → Collection → "Find Missing…" (part of EditDeckPage, same class).
    //
    // For every card copy the deck lists but hasn't claimed from the collection:
    //  1. Other printings of that card NAME you own with free copies — offered,
    //     never ticked for you (some cards are for the collection, not for play).
    //     Ticked: the deck's line switches to your printing and those copies
    //     are claimed.
    //  2. The rest → the Want List (the deck's printing and finish), topped up
    //     to what's missing, never doubled.
    // Prices are shown for both. One Undo step covers the deck, its claims and
    // the Want List.
    public partial class EditDeckPage
    {
        /// <summary>A ticked "use your printing instead": the need and the collection row to use.</summary>
        private sealed record SwapPick(ClaimLine Need, OwnedPrinting Owned);

        private void BtnFindMissing_Click(object sender, RoutedEventArgs e) => FindMissing();

        private static string Money(decimal? v) => v.HasValue ? $"${v.Value:F2}" : "no price";

        /// <summary>The deck's price per copy for a printing + finish (from its line).</summary>
        private decimal? ListedPrice(ClaimLine n)
        {
            var line = _deck?.Cards.FirstOrDefault(c => !c.IsTokenLine &&
                string.Equals(c.ScryfallId, n.ScryfallId, StringComparison.OrdinalIgnoreCase));
            if (line == null) return null;
            return n.Finish switch
            {
                CardFinish.Foil => line.FoilCopyPrice,
                CardFinish.Etched => line.EtchedCopyPrice,
                _ => line.PriceUsd,
            };
        }

        private void FindMissing()
        {
            if (_deck == null || _deckPath == null) { ShowStatus("Pick a deck first.", true); return; }
            var all = Needs().Where(n => n.Needed > 0).ToList();
            // The Want List holds cards: tokens are left out (and said so).
            var needs = all.Where(n => n.Table == CollectionEditService.CardsTable)
                           .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            int tokensMissing = all.Where(n => n.Table == CollectionEditService.TokensTable).Sum(n => n.Needed);
            string tokenNote = tokensMissing > 0
                ? $"{tokensMissing} token {(tokensMissing == 1 ? "copy is" : "copies are")} missing too — not listed, the Want List holds cards."
                : "";
            if (needs.Count == 0)
            {
                ShowStatus($"{_deck.Name}: every card copy is claimed from your collection — nothing missing." +
                           (tokenNote.Length > 0 ? "  " + tokenNote : ""), tokenNote.Length > 0);
                return;
            }

            var swaps = new List<FindMissingItem>();
            var wants = new List<FindMissingItem>();
            foreach (var n in needs)
            {
                decimal? listed = ListedPrice(n);
                string listedText = $"{n.SetCode.ToUpperInvariant()} #{n.CollectorNumber} {CardFinish.Display(n.Finish)}";
                foreach (var o in CollectionEditService.OtherPrintingsOwned(n.Name, n.ScryfallId))
                {
                    swaps.Add(new FindMissingItem
                    {
                        IsChecked = false,
                        Title = $"{n.Name}: use your {o.PrintingText} instead",
                        Detail = $"Yours: {o.Key.Text} — {o.Free} free — {Money(o.Price)} each.   " +
                                 $"The deck lists {listedText} — {Money(listed)} each, {n.Needed} missing.",
                        Tag = new SwapPick(n, o),
                    });
                }
                bool eo = EtchedOnly(n.ScryfallId, false);
                int wanted = CollectionEditService.WantedCopies(n.ScryfallId, eo).GetValueOrDefault(n.Finish);
                string cost = listed.HasValue ? $"{Money(listed)} each, {Money(listed * n.Needed)} for {n.Needed}" : "no price";
                wants.Add(new FindMissingItem
                {
                    IsChecked = wanted < n.Needed,
                    Title = $"{n.Needed} × {n.Text}",
                    Detail = $"{cost}  ·  on the Want List now: {wanted}" + (wanted >= n.Needed ? " — already covered" : ""),
                    Tag = n,
                });
            }

            decimal missingCost = needs.Sum(n => (ListedPrice(n) ?? 0m) * n.Needed);
            string header = $"{_deck.Name}: {needs.Sum(n => n.Needed)} card {(needs.Sum(n => n.Needed) == 1 ? "copy" : "copies")} missing " +
                            $"({needs.Count} {(needs.Count == 1 ? "line" : "lines")}, about {Money(missingCost)} at the deck's printings)";
            if (!FindMissingDialog.Ask(Window.GetWindow(this), header, tokenNote, swaps, wants)) return;

            var pickedSwaps = swaps.Where(i => i.IsChecked).Select(i => (SwapPick)i.Tag!).ToList();
            var pickedWants = wants.Where(i => i.IsChecked).Select(i => (ClaimLine)i.Tag!).ToList();
            var claimPrintings = pickedSwaps
                .SelectMany(p => new[] { p.Need.ScryfallId, p.Owned.Key.ScryfallId })
                .Select(s => (CollectionEditService.CardsTable, s)).Distinct().ToList();

            Edit("Find Missing", () =>
            {
                var results = new List<DeckEditResult>();
                // 1. Your own printings, up to what each card is missing.
                var covered = new Dictionary<(string, string), int>();
                // One collection row can be offered for several missing cards: its free copies are shared.
                var freeLeft = new Dictionary<RowKey, int>();
                foreach (var p in pickedSwaps) freeLeft[p.Owned.Key] = p.Owned.Free;
                foreach (var g in pickedSwaps.GroupBy(p => (p.Need.ScryfallId.ToLowerInvariant(), p.Need.Finish)))
                {
                    int left = g.First().Need.Needed;
                    foreach (var p in g)
                    {
                        if (left <= 0) break;
                        int t = Math.Min(left, freeLeft[p.Owned.Key]);
                        if (t <= 0) continue;
                        var r = SwapIn(p.Need, p.Owned, t);
                        results.Add(r);
                        if (r.Changed > 0)
                        {
                            left -= r.Changed;
                            freeLeft[p.Owned.Key] -= r.Changed;
                            covered[g.Key] = covered.GetValueOrDefault(g.Key) + r.Changed;
                        }
                    }
                }
                // 2. The rest of what's missing → the Want List.
                foreach (var w in pickedWants)
                {
                    int remaining = w.Needed - covered.GetValueOrDefault((w.ScryfallId.ToLowerInvariant(), w.Finish));
                    if (remaining <= 0)
                    {
                        results.Add(new DeckEditResult { Message = $"{w.Text}: covered by your own printing." });
                        continue;
                    }
                    var pool = PoolFor(w.ScryfallId);
                    results.Add(pool == null
                        ? DeckEditResult.Refused($"{w.Text}: not in the card pool.")
                        : FromEdit(CollectionEditService.TopUpWantList(pool, w.Finish, remaining, _deck!.Name)));
                }
                return results;
            }, claimPrintings, pickedWants.Select(w => w.ScryfallId).ToList());
        }

        /// <summary>
        /// Put <paramref name="n"/> of your own copies (another printing) in the
        /// deck in place of copies it's missing: the deck's lines of the missing
        /// printing go down (main deck first), your printing's lines go up in the
        /// same parts, and your copies are claimed. Returns copies swapped.
        /// </summary>
        private DeckEditResult SwapIn(ClaimLine need, OwnedPrinting owned, int n)
        {
            if (PoolFor(owned.Key.ScryfallId) is not PoolCard pool)
                return DeckEditResult.Refused($"{need.Name} ({owned.PrintingText}): not in the card pool.");
            var template = DeckService.FromPoolCard(pool);

            // Claim your copies first: the deck only changes by what was actually claimed.
            var claimed = CollectionEditService.ClaimRow(CollectionEditService.CardsTable, _deck!, owned.Key, n, need.Name);
            if (claimed.Changed <= 0) return DeckEditResult.Refused(claimed.Message);
            n = claimed.Changed;

            static int Rank(DeckCardCategory s) => s switch
            {
                DeckCardCategory.Mainboard => 0,
                DeckCardCategory.Sideboard => 1,
                _ => 2,
            };
            var lines = _deck!.Cards
                .Where(c => !c.IsTokenLine && c.CountOf(need.Finish) > 0 &&
                            string.Equals(c.ScryfallId, need.ScryfallId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => Rank(DeckEditService.SectionOf(c)))
                .ToList();

            int left = n;
            var touched = new List<DeckLineKey>();
            foreach (var line in lines)
            {
                if (left <= 0) break;
                var key = DeckLineKey.Of(line);
                int has = line.CountOf(need.Finish);
                int take = Math.Min(left, has);
                // Your printing in first (refused if it doesn't come in that finish), then the old one down.
                var added = DeckEditService.Add(_deck, template, owned.Key.Finish, take, key.Section);
                if (added.Changed <= 0) break;            // claims above what the deck lists are freed after the save
                DeckEditService.SetCount(_deck, key, need.Finish, has - take);
                touched.AddRange(added.Touched);
                left -= take;
            }
            int swapped = n - left;
            if (swapped <= 0) return DeckEditResult.Refused($"{need.Text}: the deck's card couldn't be switched to {owned.PrintingText}.");
            string msg = $"{need.Name}: {swapped} × your {owned.PrintingText} ({owned.Key.Text}) in place of " +
                         $"{need.SetCode.ToUpperInvariant()} #{need.CollectorNumber}";
            return new DeckEditResult
            {
                Changed = swapped,
                Warning = claimed.Warning || swapped < n,
                Message = claimed.Warning ? $"{msg}. {claimed.Message}" : $"{msg}, claimed.",
                Touched = touched,
            };
        }
    }
}