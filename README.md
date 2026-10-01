# Breakers of E v2

A Magic: The Gathering collection manager and deck builder for Windows.

**Complete overhaul** — rebuilt from the ground up with a modern architecture and UI.

---

## Status

> 🚧 **v2 is not released yet — there is no installer.** It's in active development; see
> [Development Status](#development-status) below for what works today.
>
> The current, stable version is **v1**, in the `BreakersOfE_v1` folder of this repository.

---

## What works today

### Card pool (Scryfall)

- All ~100,000+ Magic cards from Scryfall, loaded into memory for instant browsing
- Separate pools for Tokens, Planes, Schemes, Vanguards, Art Series and Conspiracies
- **Online pools** — MTGO and MTG Arena cards in their own tables (MTGO prices in tix)
- Full Database Update and price-only updates, with a summary of what changed
- Finishes from Scryfall's `finishes` data: **Non-Foil, Foil and Etched** as real finishes, each with its own price
- Token links — which tokens each card makes
- Card images cached locally by Scryfall ID

### Browsing and filtering

- **Grid and Gallery views** of every table, with a remembered choice per table
- Column filters on every column (funnel headers), multi-level sort, saved column layouts and zoom
- **Filters panel** — colors, rarity, type, legality, finish, artist, set, deck, list, rules text, and price / mana value / power / toughness / owned ranges
- Search that jumps to matches (it never hides rows)
- Set browser and set completion checklist
- Card detail panel and pop-up window with prices, legality by format, rulings and the decks that use the card

### Collection

- One collection (`collection.db`), with a row for each printing × finish × language × condition
- Tracks quantity, **condition** (Near Mint … Damaged), **language**, notes, storage location, favorites and price
- Separate collections for tokens, planes, schemes, vanguards, art series and conspiracies, plus **MTGO** and **Arena** collections
- Owned counts shown live in the card pools
- **Edit → Pool → Collection** — add or remove by finish with a Qty box, buttons, keys, right-click menus or gallery **+ / −** tiles; change finish, language or condition; edit notes and storage; multi-select; **Undo**
- Copies used by decks can't be removed by accident
- Automatic backup of the collection before the first change each session

### Decks

- Deck browser grouped by type, with statistics (mana curve, colors, value, Commander brackets)
- **Every format, with its real rules:**
  - Constructed (Standard, Pioneer, Modern, Legacy, Vintage, Pauper, …)
  - Commander, Brawl, Standard Brawl, Pauper Commander, Duel Commander, Oathbreaker
  - Limited
- Live rules check: deck size, copy limits by card name across all printings, sideboard, color identity, legality, and partner / Background / Oathbreaker pairing; plus legality by format
- **Edit → Decks:**
  - **Pool → Deck** — build from the full card pool
  - **Collection → Deck** — build from cards you own; the copies are claimed for the deck
  - **Deck → Collection** — claim copies you already own for an existing deck, or add a new precon's cards to your collection, card by card or the whole deck at once
- "Add anyway?" warnings for rule problems (never a block)
- Command zone, main deck, sideboard and **tokens**, kept apart; tokens never count toward the deck
- **Suggested Tokens** — the tokens a deck's cards make, offering your own printing when you own one
- Autosave on every change, Undo, and Tear Down (frees the deck's copies and moves the file aside)
- **"Used in"** — every collection row shows which decks hold its copies

---

## Coming next

In order:

1. **Trade Binder & Want List editing** — including Traded/Sold, Got It, and Missing → Want List from a deck
2. **Import / Export** — ManaBox, Archidekt, Moxfield, TCGPlayer, Deckbox, Dragon Shield, CSV; MTGO and Arena deck formats
3. **Keyword Dictionary** — MTG keyword glossary
4. **Themes**
5. **Settings** — default language and condition, and more
6. **Help** — deck types and their rules, and how-tos
7. **v1 → v2** — v2 replaces the v1 install and takes over the BreakersOfE documents folder

Later: the background Agent (scheduled price updates and backups), synergy search and deck-building help, and the tabletop playtest.

---

## Building from source

1. Open `BreakersOfE_v2/BreakersOfE_v2.sln` in **Visual Studio 2022**
2. Set **BreakersOfE** as the startup project, then build and run
3. **Download the card database** — click **Update Database** at the bottom of the sidebar
4. **Add your collection** — **Edit → Pool → Collection**
5. **Build a deck** — **Edit → Decks** (from the pool, or from your collection)

---

## System Requirements

- Windows 10 or later (x64)
- Internet connection for the card database download and updates
- ~500 MB disk space for card images (downloaded on demand, cached locally)
- To build: Visual Studio 2022 with the .NET 8 SDK (.NET desktop development workload)

---

## Project Structure

```
BreakersOfE_v2.sln
├── BreakersOfE.Core     — Shared class library (Models, Data, Services)
├── BreakersOfE          — WPF application (Views, ViewModels, Themes)
└── BreakersOfE.Agent    — Background system tray app (planned)
```

---

## Development Status

🚧 **In active development.**

| Area                                                     | Status         |
| -------------------------------------------------------- | -------------- |
| UI shell and navigation (View / Edit sections)           | ✅ Done        |
| Card database and Scryfall import (incl. online, etched) | ✅ Done        |
| Browsing: grid, gallery, filters, sort, sets             | ✅ Done        |
| Collection viewing, statistics and editing               | ✅ Done        |
| Deck viewing, formats, rules check and statistics        | ✅ Done        |
| Deck editing (pool, collection, claims, tokens)          | ✅ Done        |
| Trade Binder & Want List editing                         | 🔨 Next        |
| Import / Export                                          | ⬜ Planned     |
| Keyword Dictionary                                       | ⬜ Planned     |
| Themes                                                   | ⬜ Planned     |
| Settings                                                 | ⬜ Planned     |
| Help                                                     | ⬜ Planned     |
| v1 → v2 move                                             | ⬜ Planned     |
| Background Agent, synergy tools, tabletop                | ⬜ Later       |

---

## Card Data

Card data and images are provided by [Scryfall](https://scryfall.com).
Breakers of E is not affiliated with Wizards of the Coast or Scryfall.
Magic: The Gathering is © Wizards of the Coast.
