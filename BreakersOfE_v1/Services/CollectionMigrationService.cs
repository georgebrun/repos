using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;

namespace BreakersOfE.Services
{
    /// <summary>
    /// Phase 3 — one-time data migration for existing collection databases.
    ///
    /// Detects un-migrated data (combined foil/non-foil rows, Finish not set)
    /// and transforms it into the Phase 2 per-finish model:
    ///   • CollectionEntries with FoilQuantity > 0 are split into a separate
    ///     foil row (new entry with Finish="foil", Quantity = old FoilQuantity,
    ///     Price = old PriceUsdFoil). The original row becomes Finish="nonfoil"
    ///     with Price = PriceUsd, and FoilQuantity is zeroed out.
    ///   • TradeBinderEntries / WantListEntries with IsFoil=true but
    ///     Finish="nonfoil" get Finish corrected from IsFoil.
    ///   • Special collections (Token, Planar, Scheme, Vanguard, ArtSeries,
    ///     Conspiracy) — same FoilQuantity split as CollectionEntries.
    ///
    /// Always backs up collection.db before modifying. Safe to call multiple
    /// times (idempotent: checks whether migration is needed before acting).
    /// </summary>
    public static class CollectionMigrationService
    {
        /// <summary>
        /// Checks whether the collection needs per-finish migration.
        /// Returns true if any un-migrated data is found.
        /// </summary>
        public static bool NeedsMigration()
        {
            try
            {
                string path = AppFolderService.CollectionDatabasePath;
                if (!File.Exists(path)) return false;

                using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                conn.Open();

                // Check 1: CollectionEntries with FoilQuantity > 0 (combined rows)
                if (HasRows(conn,
                    "SELECT 1 FROM CollectionEntries WHERE FoilQuantity > 0 LIMIT 1"))
                    return true;

                // Check 2: TradeBinderEntries with IsFoil=1 and Finish='nonfoil'
                if (HasRows(conn,
                    "SELECT 1 FROM TradeBinderEntries WHERE IsFoil = 1 AND Finish = 'nonfoil' LIMIT 1"))
                    return true;

                // Check 3: WantListEntries with IsFoil=1 and Finish='nonfoil'
                if (HasRows(conn,
                    "SELECT 1 FROM WantListEntries WHERE IsFoil = 1 AND Finish = 'nonfoil' LIMIT 1"))
                    return true;

                // Check 4: Special collections with FoilQuantity > 0
                string[] specialTables = {
                    "TokenCollectionEntries",
                    "PlanarCollectionEntries",
                    "SchemeCollectionEntries",
                    "VanguardCollectionEntries",
                    "ArtSeriesCollectionEntries",
                    "ConspiracyCollectionEntries"
                };
                foreach (var table in specialTables)
                {
                    if (TableExists(conn, table) &&
                        HasRows(conn, $"SELECT 1 FROM {table} WHERE FoilQuantity > 0 LIMIT 1"))
                        return true;
                }

                // Check 5: CollectionEntries with Price null but PriceUsd available
                if (HasRows(conn,
                    "SELECT 1 FROM CollectionEntries WHERE Price IS NULL AND (PriceUsd IS NOT NULL OR PriceUsdFoil IS NOT NULL) LIMIT 1"))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Migration] NeedsMigration check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns a summary of what needs migrating (for the prompt dialog).
        /// </summary>
        public static (int combinedRows, int binderFixups, int wantFixups, int specialRows, int nullPrices) GetMigrationStats()
        {
            string path = AppFolderService.CollectionDatabasePath;
            if (!File.Exists(path)) return (0, 0, 0, 0, 0);

            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            conn.Open();

            int combined = CountRows(conn,
                "SELECT count(*) FROM CollectionEntries WHERE FoilQuantity > 0");
            int binder = CountRows(conn,
                "SELECT count(*) FROM TradeBinderEntries WHERE IsFoil = 1 AND Finish = 'nonfoil'");
            int want = CountRows(conn,
                "SELECT count(*) FROM WantListEntries WHERE IsFoil = 1 AND Finish = 'nonfoil'");

            int special = 0;
            string[] specialTables = {
                "TokenCollectionEntries", "PlanarCollectionEntries",
                "SchemeCollectionEntries", "VanguardCollectionEntries",
                "ArtSeriesCollectionEntries", "ConspiracyCollectionEntries"
            };
            foreach (var table in specialTables)
            {
                if (TableExists(conn, table))
                    special += CountRows(conn,
                        $"SELECT count(*) FROM {table} WHERE FoilQuantity > 0");
            }

            int nullPrices = CountRows(conn,
                "SELECT count(*) FROM CollectionEntries WHERE Price IS NULL AND (PriceUsd IS NOT NULL OR PriceUsdFoil IS NOT NULL)");

            return (combined, binder, want, special, nullPrices);
        }

        /// <summary>
        /// Runs the full per-finish migration. Returns a summary string.
        /// Backs up collection.db first with a timestamped name.
        /// </summary>
        public static string RunMigration()
        {
            string path = AppFolderService.CollectionDatabasePath;
            if (!File.Exists(path))
                return "No collection database found.";

            // ── Step 1: Back up ──────────────────────────────────────────
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(
                AppFolderService.CollectionFolder,
                $"collection_pre_perfinish_{timestamp}.db");
            SqliteConnection.ClearAllPools();
            File.Copy(path, backupPath, overwrite: true);

            // ── Step 2: Run migrations ──────────────────────────────────
            int splitCount = 0, binderFixed = 0, wantFixed = 0, specialSplit = 0;

            int pricesFixed = 0;

            using (var db = new CollectionDbContext())
            {
                // 2a: Split combined CollectionEntries
                splitCount = SplitCollectionEntries(db);

                // 2b: Fix Trade Binder Finish from IsFoil
                binderFixed = FixBinderFinish(db);

                // 2c: Fix Want List Finish from IsFoil
                wantFixed = FixWantListFinish(db);

                // 2d: Split special collections
                specialSplit = SplitSpecialCollections(db);

                // 2e: Backfill Price on entries where it's null
                // (entries that existed before Phase 2 added the Price column)
                pricesFixed = BackfillPrices(db);
            }

            int total = splitCount + binderFixed + wantFixed + specialSplit + pricesFixed;
            return $"Migration complete.\n\n" +
                   $"Collection rows split: {splitCount}\n" +
                   $"Trade Binder entries fixed: {binderFixed}\n" +
                   $"Want List entries fixed: {wantFixed}\n" +
                   $"Special collection rows split: {specialSplit}\n" +
                   $"Prices backfilled: {pricesFixed}\n\n" +
                   $"Total changes: {total}\n" +
                   $"Backup saved to:\n{backupPath}";
        }

        // ── CollectionEntries: split combined foil+non-foil rows ─────────

        private static int SplitCollectionEntries(CollectionDbContext db)
        {
            // Find rows that still have FoilQuantity > 0 (combined model)
            var combined = db.CollectionEntries
                .Where(e => e.FoilQuantity > 0)
                .ToList();

            if (combined.Count == 0) return 0;

            int count = 0;
            foreach (var orig in combined)
            {
                // Create a NEW foil row cloned from the original
                var foilRow = new CollectionEntry
                {
                    PoolId = orig.PoolId,
                    ScryfallId = orig.ScryfallId,
                    OracleId = orig.OracleId,
                    Name = orig.Name,
                    ManaCost = orig.ManaCost,
                    ManaValue = orig.ManaValue,
                    TypeLine = orig.TypeLine,
                    OracleText = orig.OracleText,
                    FlavorText = orig.FlavorText,
                    Power = orig.Power,
                    Toughness = orig.Toughness,
                    LoyaltyOrDefense = orig.LoyaltyOrDefense,
                    Colors = orig.Colors,
                    ColorIdentity = orig.ColorIdentity,
                    SetCode = orig.SetCode,
                    SetName = orig.SetName,
                    SetType = orig.SetType,
                    CollectorNumber = orig.CollectorNumber,
                    Rarity = orig.Rarity,
                    Artist = orig.Artist,
                    ImageSmallUrl = orig.ImageSmallUrl,
                    ImageNormalUrl = orig.ImageNormalUrl,
                    ImageBackUrl = orig.ImageBackUrl,
                    LocalImagePath = orig.LocalImagePath,
                    LocalImageBackPath = orig.LocalImageBackPath,
                    Layout = orig.Layout,
                    IsFoilAvailable = orig.IsFoilAvailable,
                    IsNonFoilAvailable = orig.IsNonFoilAvailable,
                    IsToken = orig.IsToken,
                    IsMeld = orig.IsMeld,
                    ReleasedAt = orig.ReleasedAt,
                    LegalitiesJson = orig.LegalitiesJson,
                    IsFavorite = orig.IsFavorite,
                    Keywords = orig.Keywords,
                    PriceUsd = orig.PriceUsd,
                    PriceUsdFoil = orig.PriceUsdFoil,
                    PriceUsdEtched = orig.PriceUsdEtched,
                    PriceEur = orig.PriceEur,
                    PriceEurFoil = orig.PriceEurFoil,
                    PriceTix = orig.PriceTix,
                    PricesJson = orig.PricesJson,
                    // Per-finish: foil row
                    Quantity = orig.FoilQuantity,
                    FoilQuantity = 0,
                    Finish = CardFinish.Foil,
                    Price = orig.PriceUsdFoil,
                    UsedCount = 0,  // will be recomputed
                    Condition = orig.Condition,
                    Language = orig.Language,
                    StorageLocation = orig.StorageLocation,
                    Notes = orig.Notes,
                    BuyAt = orig.BuyAt,
                    SellAt = orig.SellAt,
                    SellAtValue = orig.SellAtValue,
                    PriceHigh = orig.PriceHigh,
                    MarketValue = orig.PriceUsdFoil ?? orig.MarketValue,
                    PriceLow = orig.PriceLow,
                    Needed = orig.Needed,
                    Excess = orig.Excess,
                    Target = orig.Target,
                    Desired = orig.Desired,
                    CardGroup = orig.CardGroup,
                    PrintType = orig.PrintType,
                    BuyStatus = orig.BuyStatus,
                    SellStatus = orig.SellStatus,
                    DateAdded = orig.DateAdded,
                    DateModified = DateTime.Now
                };
                db.CollectionEntries.Add(foilRow);

                // Fix the original row to be nonfoil-only
                orig.Finish = CardFinish.NonFoil;
                orig.Price = orig.PriceUsd;
                orig.FoilQuantity = 0;   // drained into the new foil row
                orig.DateModified = DateTime.Now;

                count++;
            }

            db.SaveChanges();
            return count;
        }

        // ── Trade Binder: fix Finish from IsFoil ─────────────────────────

        private static int FixBinderFinish(CollectionDbContext db)
        {
            var bad = db.TradeBinderEntries
                .Where(e => e.IsFoil && e.Finish == CardFinish.NonFoil)
                .ToList();

            foreach (var e in bad)
            {
                e.Finish = CardFinish.Foil;
                e.Price = e.PriceUsdFoil ?? e.Price;
            }

            // Also fix nonfoil entries that have no Price set
            var noPriceNF = db.TradeBinderEntries
                .Where(e => !e.IsFoil && e.Price == null && e.PriceUsd != null)
                .ToList();
            foreach (var e in noPriceNF)
                e.Price = e.PriceUsd;

            db.SaveChanges();
            return bad.Count;
        }

        // ── Want List: fix Finish from IsFoil ────────────────────────────

        private static int FixWantListFinish(CollectionDbContext db)
        {
            var bad = db.WantListEntries
                .Where(e => e.IsFoil && e.Finish == CardFinish.NonFoil)
                .ToList();

            foreach (var e in bad)
            {
                e.Finish = CardFinish.Foil;
                e.Price = e.PriceUsdFoil ?? e.Price;
            }

            var noPriceNF = db.WantListEntries
                .Where(e => !e.IsFoil && e.Price == null && e.PriceUsd != null)
                .ToList();
            foreach (var e in noPriceNF)
                e.Price = e.PriceUsd;

            db.SaveChanges();
            return bad.Count;
        }

        // ── Backfill Price from PriceUsd/PriceUsdFoil ─────────────────

        private static int BackfillPrices(CollectionDbContext db)
        {
            // CollectionEntries with Price null — set from PriceUsd or PriceUsdFoil
            // based on Finish
            var nullPrice = db.CollectionEntries
                .Where(e => e.Price == null)
                .ToList();

            int count = 0;
            foreach (var e in nullPrice)
            {
                decimal? price = e.Finish == CardFinish.Foil
                    ? (e.PriceUsdFoil ?? e.PriceUsd)
                    : (e.PriceUsd ?? e.PriceUsdFoil);
                if (price != null)
                {
                    e.Price = price;
                    count++;
                }
            }

            // Trade Binder entries
            var binderNull = db.TradeBinderEntries
                .Where(e => e.Price == null)
                .ToList();
            foreach (var e in binderNull)
            {
                decimal? price = e.Finish == CardFinish.Foil
                    ? (e.PriceUsdFoil ?? e.PriceUsd)
                    : (e.PriceUsd ?? e.PriceUsdFoil);
                if (price != null)
                {
                    e.Price = price;
                    count++;
                }
            }

            // Want List entries
            var wantNull = db.WantListEntries
                .Where(e => e.Price == null)
                .ToList();
            foreach (var e in wantNull)
            {
                decimal? price = e.Finish == CardFinish.Foil
                    ? (e.PriceUsdFoil ?? e.PriceUsd)
                    : (e.PriceUsd ?? e.PriceUsdFoil);
                if (price != null)
                {
                    e.Price = price;
                    count++;
                }
            }

            if (count > 0) db.SaveChanges();
            return count;
        }

        // ── Special collections: split FoilQuantity rows ─────────────────
        // These use raw SQL because EF Core's model already expects the new
        // schema, so reading old combined rows requires careful handling.

        private static int SplitSpecialCollections(CollectionDbContext db)
        {
            int total = 0;

            // Token
            var tokens = db.TokenCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in tokens)
            {
                var foil = CloneTokenEntry(e);
                db.TokenCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.Price = null; // tokens often have no price
                e.FoilQuantity = 0;
            }
            total += tokens.Count;

            // Planar (Planechase)
            var planes = db.PlanarCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in planes)
            {
                var foil = ClonePlanarEntry(e);
                db.PlanarCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.FoilQuantity = 0;
            }
            total += planes.Count;

            // Scheme (Archenemy)
            var schemes = db.SchemeCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in schemes)
            {
                var foil = CloneSchemeEntry(e);
                db.SchemeCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.FoilQuantity = 0;
            }
            total += schemes.Count;

            // Vanguard
            var vanguards = db.VanguardCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in vanguards)
            {
                var foil = CloneVanguardEntry(e);
                db.VanguardCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.FoilQuantity = 0;
            }
            total += vanguards.Count;

            // ArtSeries
            var arts = db.ArtSeriesCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in arts)
            {
                var foil = CloneArtSeriesEntry(e);
                db.ArtSeriesCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.FoilQuantity = 0;
            }
            total += arts.Count;

            // Conspiracy
            var cons = db.ConspiracyCollectionEntries
                .Where(e => e.FoilQuantity > 0).ToList();
            foreach (var e in cons)
            {
                var foil = CloneConspiracyEntry(e);
                db.ConspiracyCollectionEntries.Add(foil);
                e.Finish = CardFinish.NonFoil;
                e.FoilQuantity = 0;
            }
            total += cons.Count;

            if (total > 0) db.SaveChanges();
            return total;
        }

        // ── Clone helpers (one per special type — EF needs concrete types) ──

        private static TokenCollectionEntry CloneTokenEntry(TokenCollectionEntry e) => new()
        {
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            OracleText = e.OracleText,
            FlavorText = e.FlavorText,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        private static PlanarCollectionEntry ClonePlanarEntry(PlanarCollectionEntry e) => new()
        {
            PlanarId = e.PlanarId,
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            OracleText = e.OracleText,
            FlavorText = e.FlavorText,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        private static SchemeCollectionEntry CloneSchemeEntry(SchemeCollectionEntry e) => new()
        {
            SchemeId = e.SchemeId,
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            OracleText = e.OracleText,
            FlavorText = e.FlavorText,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        private static VanguardCollectionEntry CloneVanguardEntry(VanguardCollectionEntry e) => new()
        {
            VanguardId = e.VanguardId,
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            OracleText = e.OracleText,
            FlavorText = e.FlavorText,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        private static ArtSeriesCollectionEntry CloneArtSeriesEntry(ArtSeriesCollectionEntry e) => new()
        {
            ArtSeriesId = e.ArtSeriesId,
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            FlavorText = e.FlavorText,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        private static ConspiracyCollectionEntry CloneConspiracyEntry(ConspiracyCollectionEntry e) => new()
        {
            ConspiracyId = e.ConspiracyId,
            ScryfallId = e.ScryfallId,
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            TypeLine = e.TypeLine,
            OracleText = e.OracleText,
            FlavorText = e.FlavorText,
            ManaCost = e.ManaCost,
            ManaValue = e.ManaValue,
            ColorIdentity = e.ColorIdentity,
            Colors = e.Colors,
            Artist = e.Artist,
            Rarity = e.Rarity,
            IsFoilAvailable = e.IsFoilAvailable,
            IsNonFoilAvailable = e.IsNonFoilAvailable,
            ImageNormalUrl = e.ImageNormalUrl,
            LocalImagePath = e.LocalImagePath,
            Quantity = e.FoilQuantity,
            FoilQuantity = 0,
            Finish = CardFinish.Foil,
            Condition = e.Condition,
            Language = e.Language,
            StorageLocation = e.StorageLocation,
            DateAdded = e.DateAdded,
            DateModified = DateTime.Now
        };

        // ── SQL helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Phase 3b — Scan all .deck files, stamp DeckId + set CollectionLinked
        /// where cards overlap with the collection, then SyncDeck + populate
        /// entered counts for each CollectionLinked deck.
        /// Returns a summary string.
        /// </summary>
        public static string BackfillDeckUsage()
        {
            string deckFolder = AppFolderService.DecksFolder;
            if (!Directory.Exists(deckFolder))
                return "No Decks folder found.";

            var deckFiles = Directory.EnumerateFiles(
                deckFolder, "*.deck", System.IO.SearchOption.AllDirectories).ToList();

            if (deckFiles.Count == 0)
                return "No .deck files found.";

            // Load all collection ScryfallIds for quick overlap check
            System.Collections.Generic.HashSet<string> collectionIds;
            using (var cdb = new CollectionDbContext())
            {
                collectionIds = cdb.CollectionEntries
                    .Where(e => e.ScryfallId != null && e.ScryfallId != "")
                    .Select(e => e.ScryfallId)
                    .Distinct()
                    .ToHashSet();
            }

            int stamped = 0, linked = 0, synced = 0, saved = 0;

            foreach (var file in deckFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var deck = System.Text.Json.JsonSerializer.Deserialize<Deck>(json);
                    if (deck == null) continue;

                    deck.FilePath = file;
                    bool changed = false;

                    // Stamp DeckId if missing
                    if (string.IsNullOrWhiteSpace(deck.DeckId))
                    {
                        deck.EnsureDeckId();
                        changed = true;
                        stamped++;
                    }

                    // Check CollectionLinked: if any card's ScryfallId is in
                    // the collection, this deck is collection-linked.
                    if (!deck.CollectionLinked)
                    {
                        bool hasOverlap = deck.Cards
                            .Any(c => !string.IsNullOrEmpty(c.ScryfallId) &&
                                      collectionIds.Contains(c.ScryfallId));
                        if (hasOverlap)
                        {
                            deck.CollectionLinked = true;
                            changed = true;
                            linked++;
                        }
                    }

                    // SyncDeck + populate entered counts for linked decks
                    if (deck.CollectionLinked &&
                        !DeckUsageService.HasUsage(deck.EnsureDeckId()))
                    {
                        DeckUsageService.SyncDeck(deck);

                        // Set entered counts = deck demand (best guess for
                        // existing decks — we don't know the actual history,
                        // so assume the full deck qty was entered).
                        using var db = new CollectionDbContext();
                        var usages = db.DeckUsages
                            .Where(u => u.DeckId == deck.DeckId)
                            .ToList();
                        foreach (var u in usages)
                        {
                            u.EnteredNonFoil = u.Quantity;
                            u.EnteredFoil = u.FoilQuantity;
                        }
                        db.SaveChanges();

                        // Recompute UsedCount on affected collection entries
                        var affectedIds = usages
                            .Select(u => u.ScryfallId).Distinct();
                        DeckUsageService.RecomputeUsedForCards(affectedIds);

                        synced++;
                    }

                    // Save deck file if we changed it
                    if (changed)
                    {
                        deck.Modified = DateTime.Now;
                        string outJson = System.Text.Json.JsonSerializer.Serialize(
                            deck, new System.Text.Json.JsonSerializerOptions
                            {
                                WriteIndented = true,
                                DefaultIgnoreCondition = System.Text.Json.Serialization
                                    .JsonIgnoreCondition.WhenWritingDefault
                            });
                        File.WriteAllText(file, outJson);
                        saved++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Migration] Deck backfill error on {file}: {ex.Message}");
                }
            }

            return $"Deck backfill complete.\n\n" +
                   $"Deck files scanned: {deckFiles.Count}\n" +
                   $"DeckIds stamped: {stamped}\n" +
                   $"Newly collection-linked: {linked}\n" +
                   $"Usage synced: {synced}\n" +
                   $"Files saved: {saved}";
        }

        // ── SQL helpers ──────────────────────────────────────────────────

        private static bool HasRows(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var rdr = cmd.ExecuteReader();
            return rdr.HasRows;
        }

        private static int CountRows(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }
}