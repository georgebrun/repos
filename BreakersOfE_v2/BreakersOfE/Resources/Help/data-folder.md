# Your Data Folder
> Where BoE keeps everything, and what's in it.

Everything BoE saves is in one folder, Documents\Breakers of E unless you picked another. **Settings → Data folder → Open Folder** opens it.

## What's inside
- **breakersofe.db**: the card pool (from Update Database).
- **rulings.db**: the rulings.
- **Collection**: collection.db (your collection, Trade Binder, Want List, tokens and online collections) and PriceHistory.db (the saved prices).
- **Decks**: one .deck file per deck. **Deleted Decks**: decks removed with Tear Down.
- **Backups**: copies of collection.db from before the first change each time BoE starts (the last 10).
- **Imports** and **Exports**: where Open File… and Save File… start.
- **CardImages**, **SetSymbols**, **ManaSymbols**: pictures.
- **Settings.json** and the table layouts.

## Backing up
Close BoE and copy the whole folder, or export a **BoE full backup** ([[howto-export-backup]]).

Note: v2 creates creates backup files of v1 files so you still have them if something goes wrong.
