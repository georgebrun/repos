# Update Database
> Card data, prices, symbols and rulings from Scryfall; card tags, combos and salt scores for deck building.

Click **Update Database** at the bottom of the side menu. (The very first time, BoE opens here and starts the update by itself.) **Last Updates** shows when the card data and prices were last updated.

- **Update Database**: everything. Card data, prices, set and mana symbols, card tags, combos and salt scores, then rulings.
- **Card Data Only**: card data, prices, symbols, card tags, combos and salt scores; no rulings.
- **Prices Only**: today's prices for the cards already in your database. Quicker.
- **Rulings Only**: the official rulings.
- **Cancel** stops safely.

## What an update does
- Your collection, Trade Binder and Want List rows get the new price of their own finish and the latest card text. Quantities and your notes are never touched.
- Each update saves the day's prices, so the price charts and **Collection value over time** grow.
- New cards from Scryfall arrive with **Update Database** or **Card Data Only**. **Prices Only** updates the cards you already have.
- **Card tags** (Scryfall's Tagger: what each card does, like ramp, removal or card draw) and **combos** (Commander Spellbook) are saved for deck building, in `tags.db` and `combos.db`, with **salt scores** (EDHREC's vote on the cards players most dislike facing) in `EDHREC\salt.json`. They come last; if one can't be downloaded, the summary says so and your card update still stands.
- After an update, the keyword dictionary finishes in the background. Start another update right away and it waits for that first ("Finishing the keyword dictionary…"), then starts by itself.
- If an update says a file is in use, something else has the database open: close Breakers of E v1, another Breakers of E window or a database viewer, then run it again. Your old database is kept as it was.

Note: An update works on a copy and swaps it in only when it's finished. If it fails, is cancelled or the PC shuts down, your card database is not changed.

## Reminders
When the data gets old, an amber line above Update Database says how many days old it is; click it to update. Choose how often in Settings → **Remind me to update after**.
