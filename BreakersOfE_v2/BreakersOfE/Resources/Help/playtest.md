# Playtest Table
> Play a deck by yourself to see how it draws and runs (goldfishing).

Open it with **▶ Playtest** on a deck: **Edit → Decks**, or a deck under **View → Decks**. The table opens in its own window, so BoE stays usable behind it. Make it as big as you like, or **⛶ Full Screen** ([F11]; [Esc] to leave).

## Starting
- Your deck is shuffled and you draw 7. Commanders wait in the **command zone**; the sideboard and the deck's Tokens part are set aside.
- The bar just over your hand sums up the opening hand: lands and spells, the colors your lands make and your spells need, and the spells' mana values.
- **Keep** [K] or **Mulligan** [M] (the London mulligan: draw 7 again, then put one card on the bottom for each mulligan). After keeping, click the cards to put on the bottom. In Commander-style decks the first mulligan is free (a ⚙ Table setting).
- **New Game ▾** starts over: **On Your Own**, or **Against Another Deck…** (pick any of your decks; type to narrow the list). After that, **Against … Again** plays the same deck again.
- **Sample Hands** deals practice opening hands, one after another (Space for a new one, D to see the next draws), with a running count of lands per hand. The game on the table isn't touched.
- **A game left unfinished** is kept when you close the table, and offered back the next time you playtest that deck: carry on, or start fresh. They're kept in **Tabletop\Games** in BoE's data folder; ⚙ **Table** → **Clear Saved Games** deletes them all.

## Player 2
With a deck against you, the table splits: their side across from yours, farther away, so smaller.
- **Their mat** faces them (upside down from your side), with their deck's playmat and sleeves (⚙ **Table** → **Look**, **This deck only**, while that deck is open, or your defaults). Their battlefield shows as they'd lay it out, seen from across the table: what's near them is far from you.
- **Their badge** (⚙ **Table** → **Player** → **Player 2**: a name and a picture) with their life and how many cards they hold. **Their hand** is held at the far edge: the backs of their cards. **Their piles** are on the right: command zone, exile, graveyard, library.
- **Their cards grow under the mouse** like yours, so you can read them.
- **Turns pass round the table:** the gold edge shows whose turn it is. For now their side is to look at; on their turn, **Next Turn** passes it back to you. Playing their side comes next. Drag the line between the two sides to share the table differently.

## Playing
BoE helps with the bookkeeping; you play the game. Nothing happens unless you meant it, and everything can be undone.
- **Click** a card to select it (a gold edge). A small **action bar** appears beside it with what makes sense for that card: Play, Tap, Turn Over, Discard, Zoom… and **⋯** for everything else. Click the empty table to deselect.
- **Enter** (or **Space**) does the selected card's main action: play it from your hand, tap or untap it on the battlefield, cast your commander. With nothing selected, it acts on the card under the mouse.
- **Drag** a card anywhere: from your hand onto the battlefield, from the battlefield to the graveyard, exile, your hand or the library (top; hold [Shift] for the bottom), and around the battlefield. Cards played with Enter or the action bar line up by themselves: creatures (and planeswalkers, battles) along the top, non-creature artifacts and enchantments in the row just above the lands, lands along the bottom.
- **Basic lands pile up** by name: "Forest ×6 · 2 tapped". **Enter** (or **Tap 1**) taps the next untapped one; the action bar also has **Untap 1**, **Tap All** and **Untap All**. Dragging the pile around the battlefield moves the whole pile; dragging it elsewhere (the graveyard, your hand) takes one land. Nonbasic lands stay single, and so does a basic with counters or damage on it.
- **Double-click** a card, or press **Z**, to see it big (both faces of a two-faced card). **L** shows the last card played. A click or any key closes it. A double-click never plays anything.
- **The table:** your mat sits in the middle at real playmat proportions (24 × 14), the table around it. Your badge is on its left: you, the player (your own picture from ⚙ **Table** → **Badge**, the same whichever deck you play; none shows a plain person icon), with your life, your mana pool and the counters on you. Your library, graveyard, exile and command zone are on its right. Your hand is held in a fan along the bottom edge; the card under the mouse lifts out of it, big enough to read (the mouse wheel scrolls a big hand).
- **Hover** over any card on the table and it grows where it sits, big enough to read (about half the window's height); move on and it goes, come back and it grows again. Turn it off in ⚙ **Table**. **Zoom** (Z, or double-click) shows just the card, big; a two-faced card has a **Turn Over** button for its other face.
- **The message bar** along the bottom is the game log: one line shows the latest; **▲ ▼** step through older and newer messages a line at a time (so does the mouse wheel); **Expand** opens it up to read back (or drag its top edge), **Collapse** puts it back to one line. **Copy** copies the log, **Clear** clears what's shown.
- **Right-click** a card for every action: turn it over, flip it, face down, counters, a token copy, move it anywhere.
- **Auras and Equipment:** playing an Aura asks you to click the card it enchants (any permanent: a creature, a land, an artifact), and it goes on the stack with that target. Right-click any card → **Attach To…** (or drag it onto the card) to attach it; Equipment and Fortifications work the same. Attached cards cascade down under their card, each showing its rules text, and move with it; they stop short of the artifact and enchantment row ("+2 MORE UNDER" when there's no room). **Hover** the card to fan them all out, readable, over everything: move the mouse down the fan and each card comes forward in turn. **Unattach** (or drag it off) takes one off. When the card leaves the battlefield, its Auras go to the graveyard and its Equipment stays, unattached; the log says so, and Undo puts it all back.
- **Mutate:** playing a mutate card asks whether to mutate; pick the creature, then **on top** or **underneath**. The merged creature shows its top card with "MUTATED ×2"; it taps, gets counters and moves as one, and when it leaves the battlefield every card in it goes together. Zoom or right-click shows what's in it.
- **Commanders:** cast from the command zone, and the tax is counted (+2 each time). Drop a commander on the command zone to send it back.
- **Tokens ▾** makes a token: one from the deck's Tokens part (Suggested Tokens… on the deck page fills it), or **Any Token…** to find any token in the card pool by name, type, power/toughness or color, and make as many as you need. Right-click a card on the battlefield → **Create a Token Copy** for a copy. A token that leaves the battlefield is gone.
- **Library:** click it to draw. Right-click for Draw X, Look at the Top X, Search the Library, Shuffle, Mill X and Exile the Top X.
- **Play With the Top Card Revealed** (right-click the library) for Courser of Kruphix, Future Sight and the like: the top card shows on the library, and you can drag it onto the battlefield or into your hand. It stays on until you turn it off.
- **Graveyard and exile:** click to look through them; double-click a card there to put it in your hand, or right-click it.
- **Life:** − and + ([Shift] for 5), or double-click the number to type it.
- **The steps of the turn** show under the toolbar: Upkeep, Draw, Main 1, Combat, Main 2, End. **Next Step** [P] moves on (the draw step draws your card); click a step to go straight to it. **Next Turn** [N] goes to your next turn's first main phase: untap, upkeep, draw.
- **Reminders:** as a step comes, cards that do something "at the beginning of" it show in a bar over your hand (Phyrexian Arena in your upkeep, Ichorid from your graveyard …), read from the cards' own text, so new sets work by themselves. They're reminders only: what you do, and whether, is your call. **Done** puts them away.
- **The stack:** every spell goes on the stack first (top right of the battlefield); lands don't, since playing a land isn't casting a spell, and neither does a card put down face down. Your commander goes there too when cast. **Resolve** [R] finishes the top one: an instant or sorcery goes to the graveyard, anything else onto the battlefield. The last one cast resolves first.
- **Damage:** right-click a creature → **Damage** to mark it. It shows on the card (marked "lethal" at its toughness, counters included) and wears off at the end of the turn.
- **Until end of turn:** right-click a creature → **Until End of Turn** for +1/+1, +3/+3, -2/-2 and the like, or **Other…** for any change (+4/+0). It wears off at the end of the turn, or when the creature leaves the battlefield. Undo takes it back.
- **Power and toughness:** when counters or until-end-of-turn changes make a creature differ from what's printed, its power/toughness shows in its corner: green when it's bigger, red when it's smaller. Damage counts against it, so "lethal" shows at the right time.
- **Planeswalkers and battles** enter with their printed loyalty or defense as counters, whether they resolve from the stack or are put straight onto the battlefield. Right-click → **Counters** to add or remove loyalty for an ability. A card put down face down enters with none.
- **Keywords** show down the left of a creature: its own (flying, trample, deathtouch, ward …), what its Auras and Equipment give it, what the battlefield gives (Fervor's haste, an anthem's "creatures you control have flying"), and keyword counters. Hover them for the full list.
- **Zz** marks a creature that's **summoning sick**: it came in this turn, so it can't attack or use its {T} abilities until your next turn (haste takes the mark away). It's a reminder only: tapping still works. A mutated creature keeps the sickness of the one it mutated onto.
- Turn both off in ⚙ **Table** (**Show keywords on creatures …**).


## Abilities

- **A card's activated abilities are buttons** on its action bar (and under **Abilities** in its right-click menu): "{1}, {T}, Sacrifice ~: Draw a card", a planeswalker's "+1:", **Equip**, and **Cycling** from your hand. Hover one for its full text and how much of it the table can do.
- **Each one is a "you may":** pressing it pays what the table can pay (mana from your pool, {T}, life, loyalty, counters, sacrificing it) and does what it can read: draw, gain or lose life, counters, +N/+N and keywords until end of turn, mill, tokens (from the deck's Tokens part or the card pool), damage, tap, untap, destroy, exile, return to hand, transform, attach (Equip), proliferate. It's all one move: one Undo takes it back.
- **What it can't do for you** goes in the log ("For you to do: …"); costs it can't pay ("Sacrifice a creature") are logged too. **Scry**, **surveil**, **look at the top** and **search** open the library for you; **discard** asks you to click the cards.
- **Targets:** an ability with a target asks you to click it on the battlefield (Esc cancels). "Any target" asks first: a creature or planeswalker, you, or an opponent. **X** is asked for when there is one.
- **Costs you choose:** "Sacrifice a creature", "Tap two untapped creatures you control", "Exile two cards from your graveyard", "Discard a card" ask you to pick each card from a menu at the mouse (hover a name to see the card); "at random" and "Discard your hand" the table does. Energy ({E}) comes off your energy.
- **From the hand and graveyard:** Cycling and Channel ("Discard ~:") are on a hand card's action bar and menu; "Exile ~ from your graveyard:" and "Return ~ from your graveyard…" are in the graveyard's right-click menu (click the graveyard pile, then right-click the card).
- **Modes:** "Choose one —" gives one button per mode. **Coin flips** and **dice** are rolled for you, and "If you win the flip…" does the half that came up. "Sacrifice it at the beginning of the next end step" is logged again when the end step comes.
- **Instants and sorceries do what they say** as they resolve (Resolve, R, or Enter on the stack): a "Choose one —" spell asks which mode, then its extra cost ("As an additional cost … sacrifice a creature"), X, and its target, and it goes to the graveyard with its effects done, in one move (Undo puts it back on the stack). Lightning Bolt, Giant Growth, Wrath of God ("Destroy all creatures"), Divination, Raise the Alarm, Unsummon, Opt and the like are done in full; anything the table can't read is logged for you. **Resolve All** stops at a spell that asks, so you can answer it. **Just resolve it** on the mode menu resolves it without doing anything.
- **Also read:** "All creatures get -2/-0", damage to each creature (with or without flying), "Untap all creatures you control", blinking ("Exile target creature you control, then return it…"), "Put target creature on top of its owner's library", token copies of a creature, "Draw a card at the beginning of the next turn's upkeep" (logged when the upkeep comes); costs such as paying half your life, six energy, exert, a -1/-1 counter on itself, milling a card, or removing a counter from a creature you control (picked from a menu).
- **"Choose two", "Choose three", "Choose one or more"** (Farewell, the charms that pick several): tick the modes in the menu (it stays open), then **Resolve with the ticked modes**. Also read now: Bolster, Manifest, Populate (click the token to copy), base power and toughness (Diminish), "You gain 2 life for each creature you control", damage to each creature and each player (Earthquake, Hurricane), "Up to two target creatures each get…" (you click one; the others are yours), "You may tap or untap target creature", and "Each player draws a card, then discards a card".
- **Reminders, not rules:** a summoning-sick creature's {T} ability, a second loyalty ability in a turn, or a pool that's short still go ahead, with a note in the log.
- ⚙ **Table** → **Card abilities**: **Do what they say**, **Just remind me** (logs the text; you do it), or **Off**. **Coverage…** reads every ability in your card pool and shows how many the table understands, and the wordings it can't read yet; **Copy** puts that on the clipboard.
## Mana

- **Tapping a land or a mana stone adds its mana** to your pool, beside your life. A card with a choice (a dual land, a Talisman, Command Tower, a Treasure) asks which; **Just Tap** taps it without mana. A pile's **Tap All** taps every land in it for the same mana.
- **What comes with it:** a Talisman or painland's damage comes off your life, a Signet's {1} comes out of your pool, City of Brass deals its 1 damage, and a Treasure or Lotus Petal is sacrificed. It's all one move: one Undo takes it back.
- **Creatures and other cards** (Llanowar Elves, Birds of Paradise) tap for mana from their right-click menu (**Tap for Mana**) or the **Mana…** button: tapping a creature is often an attack, so plain Tap doesn't ask.
- **The battlefield counts:** Blood Moon and Magus of the Moon (nonbasic lands make {R}, and lose their own abilities, Urborg's too), Celestial Dawn, Urborg, Yavimaya, Leyline of the Guildpact, Prismatic Omen, Chromatic Lantern, Spreading Seas and the like, Contamination; and extra mana from Wild Growth, Utopia Sprawl (asks its color), Overgrowth, Mana Reflection, Nyxbloom Ancient, Mana Flare, Heartbeat of Spring, Zendikar Resurgent.
- **When the pool empties** is yours to pick in ⚙ **Table** → **Unspent mana empties**: **at the end of the turn** (the default), **every step** (the rules: mana empties as each step and phase ends), or **never** (until you empty it). Whichever you pick, Omnath, Locus of Mana (green), Leyline Tyrant (red) and Upwelling (everything) keep theirs, across turns too, and Kruphix or Horizon Stone turn it colorless instead. With every step, **MANA** → **Keep This Turn** holds mana that lasts until end of turn.
- **By hand:** click a color in the pool to add one, right-click to spend one; **MANA** → **Empty Pool** spends it all. Paying for spells is yours to do: the pool just keeps count.
- **Scryfall's produced_mana** (which colors each card can make) checks what the table reads from the rules text. It fills in at your next **Full Database Update**; until then the rules text does it alone.
- Turn it off in ⚙ **Table** (**Tapping a land or mana stone adds its mana…**) to tap without mana.
- **Counters on you:** ☠ poison, ⚡ energy and ✦ experience beside your life: click for +1, right-click for −1.
- **Combos:** when every piece of one of the deck's Commander Spellbook combos is on the battlefield (or an instant or sorcery piece is in your hand), a banner says so; **How It Goes…** shows the steps. With more cards in hand than the maximum (⚙ Table: 7, or no maximum), it first asks you to click the ones to discard, or **Skip** (Reliquary Tower and the like).
- **A big hand** overlaps down to the cards' names; past that it scrolls sideways with the mouse wheel. Click **HAND** to see it all laid out, and double-click a card there to play it.
- **Undo** [Ctrl+Z] takes back any move, as far back as you like. Everything is written in the **game log**.

## Two-faced cards
- In your library they show your sleeve, like real sleeved cards. In your hand they show their front; the card panel's Show Back Face shows the other side.
- **Modal double-faced cards** (a spell on one side, a land on the other) ask which face to play when they go onto the battlefield.
- **Transforming cards** and two-sided tokens enter front up; **Turn Over** [T] turns them, keeping their counters and tapped state.
- **Kamigawa flip cards:** **Flip** [F] turns the card upside down.
- In the graveyard and exile, and back in your hand or library, a card is always front up again.

## Keys
- [Enter] or [Space] the main action · [Z] zoom · [L] the last card played · [Esc] closes a zoom, then deselects, then leaves full screen.
- [D] draw, [S] shuffle, [U] untap all, [P] next step, [N] next turn, [R] resolve the top of the stack, [K] keep, [M] mulligan, [Ctrl+Z] undo, [F11] full screen.
- On the selected card (or the one under the mouse): [T] turn over, [F] flip, [+] / [−] a +1/+1 counter, [G] to the graveyard, [E] exile, [H] to your hand.

## ⚙ Table
The table has its own settings, apart from BoE's Settings page, on six tabs (it opens on the tab you last used):
- **Look:** sleeves, playmat and table color. **Player:** your badge. **Table:** card size, the card under the mouse, keywords and the message bar. **Rules:** starting life, mulligans, hand size, mana emptying and the stack. **Assists:** tapping for mana, reminders, combo alerts and card abilities. **Saved Games:** keeping unfinished games, and clearing them.
- **Sleeves:** the back of your cards. Pick a picture from the Tabletop\Sleeves folder of your data folder, **Add a Picture…** (copied there), or the Magic card back.
- **Playmat:** a picture under your battlefield, from Tabletop\Playmats, never stretched (**Fit…** moves it and makes it bigger or smaller). **Darken it** keeps busy art from fighting the cards. Or no playmat, just the table color.
- **Fit…** places a sleeve or playmat picture: the gold frame is your battlefield (or a card, for sleeves) and stays put, while you drag the picture under it and scroll to make it bigger or smaller. Smaller than the frame leaves the table color around it. **Fill** and **Whole Picture** are quick starts. Each picture remembers its fit.
- **Every deck, or this deck only:** your sleeves and playmat are the defaults for every deck. Choose **This deck only** to give the deck you're playing its own; "Use my default" goes back to the defaults.
- **Table color:** behind the playmat, the zones and your hand. Pick one, or **Custom…** for any color (#RRGGBB).
- **Card size**, growing the card under the mouse, keywords and the message bar.
- **Starting life:** by the deck's format (40 Commander, 30 Pauper Commander, 25 Brawl, 20 the rest) or a number of your own.
- **Maximum hand size:** 7, 8, 10 or no maximum.
- **The stack, reminders, combo alerts, keeping unfinished games:** each can be switched off. With the stack off, permanents go straight onto the battlefield and instants and sorceries to the graveyard.
- **Next Turn draws a card**, and **the first mulligan is free** in Commander-style decks.

Tip: sleeves and playmats from BoE v1 are already in those folders, ready to pick.

Note: playing never changes your deck or your collection.

More: [[decks]] and [[build]].
