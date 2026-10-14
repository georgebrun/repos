# Import / Export
> Edit → Import / Export: bring card lists in, and send them out.

## Import
1. **Import into**: **Collection (cards and tokens)**, **Want List**, **A new deck** (give it a **Deck name**), **MTGO collection** or **Arena collection**.
2. **Format**: leave **Auto-detect**, or pick one if a file is read wrongly: **BoE full collection file (.csv)**, **ManaBox (.csv)**, **Other app's CSV** (Moxfield, Archidekt, Deckbox, TCGplayer, Dragon Shield, MTGGoldfish, Deckstats, Delver Lens, MTGO …), **Text list** or **MTGO .dek file**.
3. For the collection, pick **Add to what I have** or **Replace (match the file)**, and the language and condition for lines that don't say.
4. **Open File…** (a .csv, .txt or .dek) or **Paste Text**, then **Read Pasted Text**.
5. Check the preview. Each line says **Ready**, **Check** (several printings fit: one is picked, change it if needed), or why it's left out. You can change a line's **Printing** and **Finish**. **Show only lines that need attention** hides the ready ones.
6. Click **Import** and confirm.

A text list has one card per line, like: 4 Lightning Bolt (M10) 146 *F*. The set, number and finish are optional; *F* means foil and *E* etched. Headers such as Commander, Sideboard and Tokens are understood; Maybeboard lines are skipped.

**Undo Last Import** takes the last import back.

Warning: **Replace** makes your collection match the file: in each collection table the file has cards for (Cards, Tokens, …), anything not in the file is removed. Copies claimed by decks or the Trade Binder stay. BoE asks first and saves a fresh backup of your collection (in the Backups folder), and **Undo Last Import** takes it back.

Note: BoE deck files open in Edit → Decks, not here. MTG Studio files can't be imported.

## MTGO and Arena
- **MTGO collection** takes Magic Online's own files: a **.dek** or its **CSV** (from MTGO's Export), or a text list. A .dek or CSV names each card by its MTGO id, so the printing and foil (Premium) are exact.
- **Arena collection** takes a text list, like Arena's own: 4 Lightning Bolt (STA) 42. Arena has no way to export a whole collection, so type it in or paste a list.
- Only cards that game has are found: a paper-only printing in an Arena list shows **Not found**.

Each game is matched against its own cards, so switching **Import into** between paper, MTGO and Arena reads the list again.

## Export
1. **Export**: **My collection** (your cards, including the oversized ones), **A deck**, **Want List**, **Trade Binder**, **MTGO collection** or **Arena collection**.
2. **as**: a **Text list** (to paste into Moxfield, Archidekt, MTG Deck Tools…), a **ManaBox CSV**, a **Spreadsheet CSV** with every detail, or a **BoE full backup** of the whole collection.
3. Options: **Only copies I can use** leaves out copies claimed by decks or the binder; **Include tokens**; **Mark foil and etched (*F* / *E*)**.
4. **Copy to Clipboard** to paste into a website, or **Save File…** (to the Exports folder).

A deck can also go out as **Arena** (copy, then in Arena: Decks → Import) or **MTGO .dek** (load it in Magic Online). Each card becomes a printing that game has: when it doesn't have the deck's printing, the newest one it has of the same card. The summary lists any card the game doesn't have, and tokens are left out. The **MTGO collection** goes out as a **.dek**, an **MTGO CSV** or a text list; the **Arena collection** as Arena text or a text list.

Tip: The **BoE full backup** is the one to keep: Import it with **Replace** to restore everything. See [[howto-export-backup]].
