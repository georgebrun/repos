<p align="center">
  <img src="docs/images/icon.png" width="96" alt="Breakers of E icon">
</p>

<h1 align="center">Breakers of E</h1>

<p align="center">
  A Magic: The Gathering collection manager and deck builder for Windows.<br>
  <a href="https://github.com/georgebrun/repos/releases/latest"><b>Download the latest version</b></a>
</p>

---

## What it does

Breakers of E (BoE) keeps track of every Magic card that exists, the cards you own, your decks, the cards you're trading and the cards you're looking for, with card data, prices and rulings from [Scryfall](https://scryfall.com).

### Card pool
- Every Magic card from Scryfall (100,000+ printings), loaded for instant browsing
- Separate pools for Tokens, Planes, Schemes, Vanguards, Art Series and Conspiracies
- **MTGO and Arena** cards in their own tables (MTGO prices in tix)
- **Non-Foil, Foil and Etched** as real finishes, each with its own price
- Set browser with a set completion checklist
- **Update Database**: card data, prices, set and mana symbols and rulings in one click, or prices only

### Browsing
- **Grid and Gallery** views of every table
- Column filters, multi-level sort, saved column layouts and zoom
- A **Filters** panel: colors, rarity, type, legality, finish, artist, set, deck, rules text, and price, mana value, power and toughness ranges
- Card panel and card window: prices with a price chart, legality by format, rulings, and every deck using the card
- Two-sided cards show each side, with a flip

### Collection
- One row per printing × finish × language × condition, with notes, storage location, favorites and price
- Add and remove with a Qty box, keys, right-click menus or gallery **+ / −** tiles, with **Undo**
- Copies used by decks or the Trade Binder can't be removed by accident
- **Statistics**: counts, value, breakdowns, and **collection value over time**
- A backup of your collection before the first change each session

### Decks
- **Every format, with its real rules**: Standard, Pioneer, Modern, Legacy, Vintage, Pauper and more; Commander, Brawl, Standard Brawl, Pauper Commander, Duel Commander, Oathbreaker; Limited
- A live rules check: deck size, copy limits, sideboard, color identity, legality, and partner / Background / Oathbreaker pairing. It warns, it never blocks
- Build from the whole card pool or from the cards you own; the copies a deck uses are claimed so no other deck takes them
- Command zone, main deck, sideboard and tokens kept apart, with **Suggested Tokens**
- **Deck statistics**: mana curve, mana symbols vs. sources, value, Commander brackets, **draw odds** and **sample hands**
- Autosave, Undo, and **Tear Down** to free a deck's cards

### Trade Binder and Want List
- **Trade Binder**: set copies aside to trade or sell, with Trade Value; **Traded / Sold** takes them out of your collection
- **Want List**: cards you're looking for, with an offer price; **Got It** moves them into your collection

### Import and export
- Import from **ManaBox**, **Moxfield, Archidekt, Deckbox, TCGplayer, Dragon Shield, MTGGoldfish, Deckstats, Delver Lens** CSVs, or a plain **text list**, into your collection, Want List or a new deck, with a preview of every line and **Undo Last Import**
- Export a text list (for Moxfield, Archidekt, MTG Deck Tools…), a ManaBox CSV, a spreadsheet CSV, or a **BoE full backup**

### And more
- **Keyword Dictionary**: what every keyword means, the official rules, and the cards that have it
- **Card pictures offline**: keep the pictures of your own cards, or download them all
- **Themes**: Dark, Light, Follow Windows, or your own colors
- **Help** for every page (press F1) and a **Program Tour**
- Lets you know when a new version is out

---

## Download and install

1. Go to the [latest release](https://github.com/georgebrun/repos/releases/latest) and download **BreakersOfE_Setup_v2.x.x.exe**.
2. Run it. No administrator rights are needed.
3. Start Breakers of E. The first time, it downloads the card data by itself. That takes a few minutes, and an internet connection.

Your data is kept in **Documents\Breakers of E**. You can move it later in **Settings → Data folder**.

### System requirements
- Windows 10 or 11 (64-bit or 32-bit)
- An internet connection for card data, prices and pictures
- A few hundred MB for the card data, plus the card pictures you choose to keep (several GB for all of them)

---

## Coming from v1

**Version 2 replaces v1, and v1 is no longer supported.**

- Setup installs v2 over v1. Before anything is changed, it tells you v1 is being replaced; **Cancel** there leaves v1 as it was.
- The first time v2 starts, it converts your collection, Trade Binder, Want List and the cards your decks use. Every card is counted before and after converting; if anything doesn't match, your v1 files are put back.
- Your v1 files are kept, exactly as they were, in **Documents\Breakers of E\v1 Backups**.
- Your decks, filters and card pictures stay where they are; v2 reads them.
- v1 couldn't tell foil from etched. After the first card data update, printings that only come in etched are set to etched for you, and a **Foil or Etched?** list asks about the ones that come in both.
- v1's settings aren't carried over. v2 starts with its own.

---

## Your data

Everything BoE saves is in one folder, **Documents\Breakers of E** unless you moved it:

| | |
|---|---|
| `Collection\collection.db` | Your collection, Trade Binder, Want List, tokens and online collections |
| `Collection\PriceHistory.db` | The prices saved with each update |
| `Decks` | One `.deck` file per deck |
| `Backups` | Copies of `collection.db` from before the first change each session |
| `breakersofe.db`, `rulings.db` | The card data and rulings (downloaded again any time) |

To back up, close BoE and copy the whole folder, or use **Edit → Import / Export → BoE full backup**.

---

## Building from source

1. Open `BreakersOfE_v2/BreakersOfE_v2.sln` in **Visual Studio 2022** with the **.NET desktop development** workload (.NET 8 SDK).
2. Set **BreakersOfE** as the startup project, then build and run.

To make the installer:
1. Run `Installer\Publish_v2.bat` (publishes x64 and x86).
2. Open `Installer\BreakersOfE_Setup_v2.iss` in **Inno Setup 6** and compile. The setup file goes to `Installer\Output`.

### Project structure
```
BreakersOfE_v2.sln
├── BreakersOfE.Core     — shared library: models, data (EF Core + SQLite), services
├── BreakersOfE          — the WPF app (WPF-UI): views, view models, Help topics
└── BreakersOfE.Agent    — background helper, parked (not part of 2.0)
BreakersOfE_v1           — v1, kept for reference (no longer supported)
Installer                — publish script and Inno Setup scripts
```

---

## Credits

Card data, prices, rulings and images are provided by [Scryfall](https://scryfall.com). Breakers of E is not affiliated with Scryfall.

Breakers of E is unofficial Fan Content permitted under the [Fan Content Policy](https://company.wizards.com/en/legal/fancontentpolicy). Not approved/endorsed by Wizards. Portions of the materials used are property of Wizards of the Coast. ©Wizards of the Coast LLC.

---

© George Brun. All rights reserved.
