# Breakers of E v2

A Magic: The Gathering collection manager, deck builder, and tabletop simulator for Windows.

**Complete overhaul** — rebuilt from the ground up with modern architecture and UI.

---

## Download

👉 **[Download the latest installer](../../releases/latest)**

> Windows will show a security warning on first launch — click **More info → Run anyway**. This is normal for unsigned software.

> Looking for v1? See the [BreakersOfE](https://github.com/YOURUSERNAME/BreakersOfE) repository.

---

## What's New in v2

- **Modern UI** — Windows 11 Fluent design with Wpf.Ui controls
- **10+ Themes** — Dark, Light, Windows XP Classic, 7 MTG color themes, and unlimited custom themes
- **Three-Project Architecture** — shared Core library, WPF app, and background Agent (zero duplicate code)
- **Live Deck Pricing** — prices always current from pool database, no manual update needed
- **Reactive Filtering** — inline filter panel with instant results, no popup windows
- **Background Agent** — system tray app for automatic price updates, backups, and notifications
- **Synergy Search** — find cards that work together by keyword and oracle text
- **Deck Builder Assistant** — category slot filling, staple suggestions, precon upgrade paths
- **Deck Comparison** — side-by-side analysis of two decks
- **Multi-Deck Tabs** — edit multiple decks simultaneously
- **Drag-and-Drop** — drag cards from collection to deck
- **Playtest Overhaul** — semi-automatic tabletop with Jitsi webcam integration
- **Card Condition Tracking** — NM, LP, MP, HP, DMG with adjusted pricing
- **Price History & Alerts** — track value trends, get notified on spikes
- **Automatic Backups** — scheduled database and deck file backups
- **Scryfall Schema Validation** — detects API changes before they break imports
- **Local Image Caching** — tiered caching for faster display and offline browsing

---

## Features

### Collection Management
- Browse all ~100,000+ Magic cards from Scryfall
- Track your collection with quantities, foil status, condition, language, storage location, and personal prices
- Import from ManaBox, Archidekt, Moxfield, TCGPlayer, Deckbox, Dragon Shield, CSV, and JSON
- Export to all major formats
- Collection insights dashboard with value tracking and statistics

### Deck Builder
- Build Standard and Commander decks with multi-tab support
- AI-assisted deckbuilding with synergy suggestions
- Pool → Deck or Collection → Deck mode
- Full deck statistics: mana curve, color pie, legality, card value, budget tracking
- Foil card support, commander designation, sideboard
- Deck comparison tool
- Drag-and-drop card management
- Auto-save drafts

### Trade Binder & Want List
- Visual card binder — just like a real physical binder
- Track cards you want to trade away and cards you're looking for
- Trade helper with value differential calculator

### Tabletop Simulator
- Play Magic against yourself or friends via Jitsi webcam
- Full Commander support: command zone, commander tax, commander damage tracking
- Life totals, poison counters, phases, the stack, tokens, and more
- Modern mechanics: Monarch, Initiative, Energy, The Ring
- Playtest stats tracking (hand quality, mana curve hit rate)
- Save and restore game state

### Background Agent
- System tray app runs quietly in the background
- Automatic daily price updates from Scryfall
- Automatic weekly database and deck file backups
- Price spike/drop notifications
- Card image pre-caching for your collection
- Scryfall API health monitoring

### Tools
- Synergy Search — find cards that complement each other
- Keyword Search — find cards by ability keywords with color and legality filters
- Keyword Dictionary — full MTG rules glossary (~200 keywords) with hover tooltips
- Advanced Filter — reactive inline filtering with instant results
- Card images cached locally for fast display and offline use

---

## Getting Started

1. **Install** — run the installer and follow the wizard
2. **Download the card database** — go to **Update Database** in the sidebar after launch
3. **Add your collection** — navigate to **Collection** and start adding cards, or import via the menu
4. **Build a deck** — navigate to **Decks** or **Deck Builder** and create a new deck
5. **Customize** — go to **Settings** and pick your theme

---

## System Requirements

- Windows 10 or later (x64)
- Internet connection required for first-time card database download
- ~500 MB disk space for card images (downloaded on demand, cached locally)
- .NET 8 Runtime (bundled with installer)

---

## Project Structure

```
BreakersOfE_v2.sln
├── BreakersOfE.Core     — Shared class library (Models, Data, Services)
├── BreakersOfE          — WPF application (Views, ViewModels, Themes)
└── BreakersOfE.Agent    — Background system tray app (Workers)
```

---

## Development Status

🚧 **In active development** — building phase by phase.

| Phase | Status | Description |
|-------|--------|-------------|
| 1 | ✅ Complete | UI shell, navigation, theme switching |
| 2 | ⬜ Planned | Database services, Scryfall import |
| 3 | ⬜ Planned | Collection browsing with reactive filtering |
| 4 | ⬜ Planned | Dashboard and insights |
| 5 | ⬜ Planned | Deck editor with live pricing |
| 6 | ⬜ Planned | Import/export, trade binder, want list |
| 7 | ⬜ Planned | Background agent |
| 8 | ⬜ Planned | Synergy search, deck builder assistant |
| 9 | ⬜ Planned | Tabletop playtest + multiplayer |
| 10 | ⬜ Planned | Polish and ship |

---

## Card Data

Card data and images are provided by [Scryfall](https://scryfall.com).
Breakers of E is not affiliated with Wizards of the Coast or Scryfall.
Magic: The Gathering is © Wizards of the Coast.
