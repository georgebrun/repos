# Decks
> Edit → Decks: build decks and tie them to the cards you own.

A deck is a file in your data folder's Decks folder. Every change is saved at once. **View → Decks** shows your decks as tiles to look at; the Edit pages change them.

## The three deck pages
- **Pool → Deck**: build from any card that exists. Nothing is taken from your collection. Good for planning.
- **Collection → Deck**: build from the cards you own. Adding a card **claims** copies from that collection row, so another deck can't use them.
- **Deck → Collection**: match a finished deck to your collection: claim copies you own, or add new copies (a sealed precon).

## Opening and creating
- Pick a deck in the **Deck** list (type a name to jump to it). The page opens with no deck open.
- **New Deck…** asks for a **Name**, **Deck type** (Commander, Constructed, Brawl, Standard Brawl, Pauper Commander, Duel Commander, Oathbreaker, Limited), a **Format** for Constructed, and a description.
- **Deck Settings…** changes them later; renaming also renames the file.
- **Close Deck** closes it (it's already saved).

## Adding and removing (Pool → Deck, Collection → Deck)
- **Add Non-Foil / Add Foil / Add Etched** ([Enter], [Shift+Enter], [Ctrl+Enter]) add Qty copies. **Add to** picks Main deck or Sideboard where the format has one.
- Right-click → **Add as Commander** (or Oathbreaker / Signature Spell) puts a card in the command zone.
- **Remove Non-Foil / Foil / Etched** take Qty away; **Remove Card** ([Shift+Delete]) takes every copy. In the deck table, [Delete] removes Qty of that line's finish.
- Double-click a deck line's **Non-Foil**, **Foil** or **Etched** count to type the exact number.
- Right-click a deck line to **Set as Commander**, **Move to Sideboard** or **Move to Main Deck**.

If an add would break a deck rule (too many copies of a card, not legal in the format, outside the commander's colors, deck size), **Add Anyway?** explains why. **Cancel** is the default; **Add Anyway** adds it anyway. It's a warning, never a block.

## Claiming your copies (Deck → Collection)
- **Use Copies I Own** ([Enter]): claim free copies of the same printing and finish.
- **Add as New Copies**: add what's still needed to your collection as new copies, claimed for the deck, in the language and condition picked under **New copies as**.
- **Use This Row**: claim the free copies of the selected collection row.
- **Free Copies** ([Delete]): give claimed copies back to Available; the cards stay in the deck.
- **Whole Deck: Use Copies I Own…** and **Whole Deck: Add as New…** do the whole deck after a preview.
- **Find Missing…**: switch cards to a printing you own, or add what's missing to the Want List. See [[howto-find-missing]].

The line under the buttons shows how much of the deck is claimed, e.g. "Main 60 of 60 · Sideboard 15 of 15".

## Tokens
**Suggested Tokens…** lists the tokens the deck's cards make; tick them and **Add Ticked**. Tokens you own are claimed. Tokens sit in the deck's Tokens part and no deck rules apply to them.

## Tear Down
**Tear Down…** gives every claimed copy back to Available and moves the deck file to the **Deleted Decks** folder (you can copy it back by hand). It can't be undone on the page. See [[howto-tear-down]].

## Undo
**Undo** or [Ctrl+Z] takes back the last 30 changes **of the open deck**. Each deck keeps its own undo while BoE is open.

See also [[howto-build-deck]] and [[statistics]].
