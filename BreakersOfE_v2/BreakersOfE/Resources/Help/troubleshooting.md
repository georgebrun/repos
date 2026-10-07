# Troubleshooting
> When something isn't right.

## The card pool is empty
Run [[update-database|Update Database]]: the pool is downloaded from Scryfall.

## A card shows "Scryfall Fail"
There's no picture for it: you're offline and the picture isn't kept, or Scryfall has none. Go online, or keep pictures for offline use in Settings → **Card pictures**.

## "Your data folder wasn't found" at startup
The folder is on a drive that isn't connected, or BoE can't save there. BoE uses the default folder until you connect the drive and restart BoE.

Warning: Changes made while BoE uses the default folder are saved there, not in your usual folder.

## An update failed or was cancelled
Nothing was changed; run it again. If BoE warns that Scryfall's data format changed, check scryfall.com/blog.

## A button is greyed out, or copies won't go
**Add Foil** or **Add Etched** is greyed out when the printing doesn't come in that finish. **Remove** leaves copies used by decks or the Trade Binder: free them in the deck or the binder first.

## Undo says it can't
Undo is refused when the same cards (or the deck file) were changed since, for example on another page. The change itself is fine; it just can't be taken back.

## Getting your collection back
- **Backups** in your data folder holds copies of collection.db from before the first change of each session. To use one: close BoE, rename the Collection folder's collection.db (and delete collection.db-wal and collection.db-shm if they are there), then copy the backup in and name it collection.db.
- A **BoE full backup** export can be imported with **Replace**.
