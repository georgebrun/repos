using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Data
{
    /// <summary>
    /// Separate database for the user's collection.
    /// Lives in My Documents\Breakers of E\Collection\collection.db
    /// Completely independent of the card pool database — pool updates
    /// never touch this database.
    ///
    /// Each collection entry is SELF-CONTAINED: it stores all card display
    /// data (name, set, type, image, etc.) directly.  The only shared key
    /// with the pool is ScryfallId; PoolId is kept for legacy compat only.
    /// </summary>
    public class CollectionDbContext : DbContext
    {
        public DbSet<CollectionEntry> CollectionEntries { get; set; }
        public DbSet<TokenCollectionEntry> TokenCollectionEntries { get; set; }
        public DbSet<PlanarCollectionEntry> PlanarCollectionEntries { get; set; }
        public DbSet<SchemeCollectionEntry> SchemeCollectionEntries { get; set; }
        public DbSet<VanguardCollectionEntry> VanguardCollectionEntries { get; set; }
        public DbSet<ConspiracyCollectionEntry> ConspiracyCollectionEntries { get; set; }
        public DbSet<ArtSeriesCollectionEntry> ArtSeriesCollectionEntries { get; set; }
        public DbSet<TradeBinderEntry> TradeBinderEntries { get; set; }
        public DbSet<WantListEntry> WantListEntries { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite(
                $"Data Source={AppFolderService.CollectionDatabasePath}");
        }

        public override int SaveChanges()
        {
            int result = base.SaveChanges();
            // Passive checkpoint after each save nudges the write-ahead log to
            // merge into the main collection.db without blocking writers. Keeps
            // the -wal file small and the main file close to current.
            try { Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(PASSIVE);"); }
            catch { /* best-effort */ }
            return result;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ScryfallId is the primary lookup key now
            modelBuilder.Entity<CollectionEntry>()
                .HasIndex(e => e.ScryfallId);
            modelBuilder.Entity<CollectionEntry>()
                .HasIndex(e => e.PoolId);  // legacy compat
        }

        public void EnsureCreated()
        {
            if (!System.IO.File.Exists(AppFolderService.CollectionDatabasePath))
                Database.EnsureCreated();
        }

        /// <summary>
        /// Forces SQLite to merge the write-ahead log (collection.db-wal) into
        /// the main collection.db file. Call after saving and on app shutdown so
        /// the main database file is always complete on disk and the -wal/-shm
        /// working files are flushed. TRUNCATE resets the WAL to zero length.
        /// </summary>
        public void Checkpoint()
        {
            try
            {
                Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch { /* checkpoint is best-effort; never block on it */ }
        }

        public void MigrateSchema()
        {
            if (!System.IO.File.Exists(AppFolderService.CollectionDatabasePath))
                Database.EnsureCreated();

            // ── Create tables that may not exist yet ────────────────────────
            CreateTableIfMissing(@"
                CREATE TABLE IF NOT EXISTS ConspiracyCollectionEntries (
                    ConspiracyCollectionEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConspiracyId                 INTEGER NOT NULL DEFAULT 0,
                    ScryfallId                   TEXT NOT NULL DEFAULT '',
                    OracleId                     TEXT NOT NULL DEFAULT '',
                    Name                         TEXT NOT NULL DEFAULT '',
                    TypeLine                     TEXT NOT NULL DEFAULT '',
                    OracleText                   TEXT NOT NULL DEFAULT '',
                    FlavorText                   TEXT NOT NULL DEFAULT '',
                    ManaCost                     TEXT NOT NULL DEFAULT '',
                    ManaValue                    REAL NOT NULL DEFAULT 0,
                    ColorIdentity                TEXT NOT NULL DEFAULT '',
                    Colors                       TEXT NOT NULL DEFAULT '',
                    SetCode                      TEXT NOT NULL DEFAULT '',
                    SetName                      TEXT NOT NULL DEFAULT '',
                    SetType                      TEXT NOT NULL DEFAULT '',
                    CollectorNumber              TEXT NOT NULL DEFAULT '',
                    Rarity                       TEXT NOT NULL DEFAULT '',
                    Artist                       TEXT NOT NULL DEFAULT '',
                    ImageSmallUrl                TEXT NOT NULL DEFAULT '',
                    ImageNormalUrl                TEXT NOT NULL DEFAULT '',
                    Layout                       TEXT NOT NULL DEFAULT '',
                    IsFoilAvailable              INTEGER NOT NULL DEFAULT 0,
                    IsNonFoilAvailable           INTEGER NOT NULL DEFAULT 0,
                    ReleasedAt                   TEXT NOT NULL DEFAULT '',
                    LocalImagePath               TEXT NOT NULL DEFAULT '',
                    IsFavorite                   INTEGER NOT NULL DEFAULT 0,
                    Quantity                     INTEGER NOT NULL DEFAULT 0,
                    FoilQuantity                 INTEGER NOT NULL DEFAULT 0,
                    Condition                    TEXT NOT NULL DEFAULT 'Unknown',
                    Language                     TEXT NOT NULL DEFAULT 'English',
                    Notes                        TEXT NOT NULL DEFAULT '',
                    StorageLocation              TEXT NOT NULL DEFAULT '',
                    DateAdded                    TEXT NOT NULL DEFAULT '',
                    DateModified                 TEXT NOT NULL DEFAULT ''
                )");

            CreateTableIfMissing(@"
                CREATE TABLE IF NOT EXISTS TradeBinderEntries (
                    TradeBinderEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    PoolId              INTEGER NOT NULL DEFAULT 0,
                    ScryfallId          TEXT NOT NULL DEFAULT '',
                    Name                TEXT NOT NULL DEFAULT '',
                    SetCode             TEXT NOT NULL DEFAULT '',
                    SetName             TEXT NOT NULL DEFAULT '',
                    CollectorNumber     TEXT NOT NULL DEFAULT '',
                    TypeLine            TEXT NOT NULL DEFAULT '',
                    OracleText          TEXT NOT NULL DEFAULT '',
                    FlavorText          TEXT NOT NULL DEFAULT '',
                    ManaCost            TEXT NOT NULL DEFAULT '',
                    ManaValue           REAL NOT NULL DEFAULT 0,
                    ColorIdentity       TEXT NOT NULL DEFAULT '',
                    Colors              TEXT NOT NULL DEFAULT '',
                    Rarity              TEXT NOT NULL DEFAULT '',
                    Artist              TEXT NOT NULL DEFAULT '',
                    Power               TEXT NOT NULL DEFAULT '',
                    Toughness           TEXT NOT NULL DEFAULT '',
                    IsFoilAvailable     INTEGER NOT NULL DEFAULT 0,
                    IsNonFoilAvailable  INTEGER NOT NULL DEFAULT 0,
                    PriceUsd            REAL,
                    PriceUsdFoil        REAL,
                    ImageNormalUrl       TEXT NOT NULL DEFAULT '',
                    LocalImagePath       TEXT NOT NULL DEFAULT '',
                    LegalitiesJson       TEXT NOT NULL DEFAULT '',
                    Quantity            INTEGER NOT NULL DEFAULT 1,
                    IsFoil              INTEGER NOT NULL DEFAULT 0,
                    Condition           TEXT NOT NULL DEFAULT 'Near Mint',
                    AskingPrice         REAL,
                    Notes               TEXT NOT NULL DEFAULT '',
                    DateAdded           TEXT NOT NULL DEFAULT ''
                )");

            CreateTableIfMissing(@"
                CREATE TABLE IF NOT EXISTS WantListEntries (
                    WantListEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    PoolId           INTEGER NOT NULL DEFAULT 0,
                    ScryfallId       TEXT NOT NULL DEFAULT '',
                    Name             TEXT NOT NULL DEFAULT '',
                    SetCode          TEXT NOT NULL DEFAULT '',
                    SetName          TEXT NOT NULL DEFAULT '',
                    CollectorNumber  TEXT NOT NULL DEFAULT '',
                    TypeLine         TEXT NOT NULL DEFAULT '',
                    OracleText       TEXT NOT NULL DEFAULT '',
                    FlavorText       TEXT NOT NULL DEFAULT '',
                    ManaCost         TEXT NOT NULL DEFAULT '',
                    ManaValue        REAL NOT NULL DEFAULT 0,
                    ColorIdentity    TEXT NOT NULL DEFAULT '',
                    Colors           TEXT NOT NULL DEFAULT '',
                    Rarity           TEXT NOT NULL DEFAULT '',
                    Artist           TEXT NOT NULL DEFAULT '',
                    Power            TEXT NOT NULL DEFAULT '',
                    Toughness        TEXT NOT NULL DEFAULT '',
                    IsFoilAvailable  INTEGER NOT NULL DEFAULT 0,
                    IsNonFoilAvailable INTEGER NOT NULL DEFAULT 0,
                    PriceUsd         REAL,
                    PriceUsdFoil     REAL,
                    ImageNormalUrl    TEXT NOT NULL DEFAULT '',
                    LocalImagePath    TEXT NOT NULL DEFAULT '',
                    LegalitiesJson    TEXT NOT NULL DEFAULT '',
                    Quantity         INTEGER NOT NULL DEFAULT 1,
                    IsFoil           INTEGER NOT NULL DEFAULT 0,
                    OfferPrice       REAL,
                    Notes            TEXT NOT NULL DEFAULT '',
                    DateAdded        TEXT NOT NULL DEFAULT ''
                )");

            // ── Indexes ─────────────────────────────────────────────────────
            SafeExec("CREATE INDEX IF NOT EXISTS IX_CollectionEntries_ScryfallId ON CollectionEntries (ScryfallId)");
            SafeExec("CREATE INDEX IF NOT EXISTS IX_TradeBinderEntries_ScryfallId ON TradeBinderEntries (ScryfallId)");
            SafeExec("CREATE INDEX IF NOT EXISTS IX_WantListEntries_ScryfallId ON WantListEntries (ScryfallId)");
            SafeExec("CREATE INDEX IF NOT EXISTS IX_TradeBinderEntries_PoolId ON TradeBinderEntries (PoolId)");
            SafeExec("CREATE INDEX IF NOT EXISTS IX_WantListEntries_PoolId ON WantListEntries (PoolId)");

            // ── ADD COLUMN for every new field on every table ───────────────
            // CollectionEntries — card data fields
            var ceCardCols = new (string col, string def)[]
            {
                ("OracleId",           "TEXT NOT NULL DEFAULT ''"),
                ("Name",               "TEXT NOT NULL DEFAULT ''"),
                ("ManaCost",           "TEXT NOT NULL DEFAULT ''"),
                ("ManaValue",          "REAL NOT NULL DEFAULT 0"),
                ("TypeLine",           "TEXT NOT NULL DEFAULT ''"),
                ("OracleText",         "TEXT NOT NULL DEFAULT ''"),
                ("FlavorText",         "TEXT NOT NULL DEFAULT ''"),
                ("Power",              "TEXT NOT NULL DEFAULT ''"),
                ("Toughness",          "TEXT NOT NULL DEFAULT ''"),
                ("LoyaltyOrDefense",   "TEXT NOT NULL DEFAULT ''"),
                ("Colors",             "TEXT NOT NULL DEFAULT ''"),
                ("ColorIdentity",      "TEXT NOT NULL DEFAULT ''"),
                ("SetCode",            "TEXT NOT NULL DEFAULT ''"),
                ("SetName",            "TEXT NOT NULL DEFAULT ''"),
                ("SetType",            "TEXT NOT NULL DEFAULT ''"),
                ("CollectorNumber",    "TEXT NOT NULL DEFAULT ''"),
                ("Rarity",             "TEXT NOT NULL DEFAULT ''"),
                ("Artist",             "TEXT NOT NULL DEFAULT ''"),
                ("ImageSmallUrl",      "TEXT NOT NULL DEFAULT ''"),
                ("ImageNormalUrl",     "TEXT NOT NULL DEFAULT ''"),
                ("ImageBackUrl",       "TEXT NOT NULL DEFAULT ''"),
                ("LocalImagePath",     "TEXT NOT NULL DEFAULT ''"),
                ("LocalImageBackPath", "TEXT NOT NULL DEFAULT ''"),
                ("Layout",             "TEXT NOT NULL DEFAULT ''"),
                ("IsFoilAvailable",    "INTEGER NOT NULL DEFAULT 0"),
                ("IsNonFoilAvailable", "INTEGER NOT NULL DEFAULT 0"),
                ("IsToken",            "INTEGER NOT NULL DEFAULT 0"),
                ("IsMeld",             "INTEGER NOT NULL DEFAULT 0"),
                ("ReleasedAt",         "TEXT NOT NULL DEFAULT ''"),
                ("LegalitiesJson",     "TEXT NOT NULL DEFAULT ''"),
                ("IsFavorite",         "INTEGER NOT NULL DEFAULT 0"),
                ("Keywords",           "TEXT NOT NULL DEFAULT ''"),
                ("PriceUsd",           "REAL"),
                ("PriceUsdFoil",       "REAL"),
                ("PriceUsdEtched",     "REAL"),
                ("PriceEur",           "REAL"),
                ("PriceEurFoil",       "REAL"),
                ("PriceTix",           "REAL"),
                ("PricesJson",         "TEXT NOT NULL DEFAULT ''"),
                // legacy compat columns that may already exist
                ("FoilQuantity",       "INTEGER NOT NULL DEFAULT 0"),
                ("UsedCount",          "INTEGER NOT NULL DEFAULT 0"),
                ("Condition",          "TEXT NOT NULL DEFAULT 'NM'"),
                ("ScryfallId",         "TEXT NOT NULL DEFAULT ''"),
            };
            foreach (var (col, def) in ceCardCols)
                AddColumn("CollectionEntries", col, def);

            // Special collection shared card-data columns
            var specialCardCols = new (string col, string def)[]
            {
                ("OracleId",           "TEXT NOT NULL DEFAULT ''"),
                ("Name",               "TEXT NOT NULL DEFAULT ''"),
                ("TypeLine",           "TEXT NOT NULL DEFAULT ''"),
                ("OracleText",         "TEXT NOT NULL DEFAULT ''"),
                ("FlavorText",         "TEXT NOT NULL DEFAULT ''"),
                ("SetCode",            "TEXT NOT NULL DEFAULT ''"),
                ("SetName",            "TEXT NOT NULL DEFAULT ''"),
                ("SetType",            "TEXT NOT NULL DEFAULT ''"),
                ("CollectorNumber",    "TEXT NOT NULL DEFAULT ''"),
                ("Rarity",             "TEXT NOT NULL DEFAULT ''"),
                ("Artist",             "TEXT NOT NULL DEFAULT ''"),
                ("ImageSmallUrl",      "TEXT NOT NULL DEFAULT ''"),
                ("ImageNormalUrl",     "TEXT NOT NULL DEFAULT ''"),
                ("Layout",             "TEXT NOT NULL DEFAULT ''"),
                ("IsFoilAvailable",    "INTEGER NOT NULL DEFAULT 0"),
                ("IsNonFoilAvailable", "INTEGER NOT NULL DEFAULT 0"),
                ("ReleasedAt",         "TEXT NOT NULL DEFAULT ''"),
                ("LocalImagePath",     "TEXT NOT NULL DEFAULT ''"),
                ("IsFavorite",         "INTEGER NOT NULL DEFAULT 0"),
                ("ScryfallId",         "TEXT NOT NULL DEFAULT ''"),
                ("UsedCount",          "INTEGER NOT NULL DEFAULT 0"),
            };

            string[] specialTables = {
                "TokenCollectionEntries",
                "PlanarCollectionEntries",
                "SchemeCollectionEntries",
                "VanguardCollectionEntries",
                "ArtSeriesCollectionEntries",
                "ConspiracyCollectionEntries"
            };
            foreach (var table in specialTables)
                foreach (var (col, def) in specialCardCols)
                    AddColumn(table, col, def);

            // Token-specific extra fields
            AddColumn("TokenCollectionEntries", "Power", "TEXT NOT NULL DEFAULT ''");
            AddColumn("TokenCollectionEntries", "Toughness", "TEXT NOT NULL DEFAULT ''");
            AddColumn("TokenCollectionEntries", "Colors", "TEXT NOT NULL DEFAULT ''");
            AddColumn("TokenCollectionEntries", "ColorIdentity", "TEXT NOT NULL DEFAULT ''");

            // Vanguard-specific extra fields
            AddColumn("VanguardCollectionEntries", "HandModifier", "TEXT NOT NULL DEFAULT ''");
            AddColumn("VanguardCollectionEntries", "LifeModifier", "TEXT NOT NULL DEFAULT ''");

            // Conspiracy-specific extra fields
            AddColumn("ConspiracyCollectionEntries", "ManaCost", "TEXT NOT NULL DEFAULT ''");
            AddColumn("ConspiracyCollectionEntries", "ManaValue", "REAL NOT NULL DEFAULT 0");
            AddColumn("ConspiracyCollectionEntries", "ColorIdentity", "TEXT NOT NULL DEFAULT ''");
            AddColumn("ConspiracyCollectionEntries", "Colors", "TEXT NOT NULL DEFAULT ''");

            // Trade Binder & Want List — card data columns
            var binderWantCols = new (string col, string def)[]
            {
                ("ScryfallId",         "TEXT NOT NULL DEFAULT ''"),
                ("Name",               "TEXT NOT NULL DEFAULT ''"),
                ("SetCode",            "TEXT NOT NULL DEFAULT ''"),
                ("SetName",            "TEXT NOT NULL DEFAULT ''"),
                ("CollectorNumber",    "TEXT NOT NULL DEFAULT ''"),
                ("TypeLine",           "TEXT NOT NULL DEFAULT ''"),
                ("OracleText",         "TEXT NOT NULL DEFAULT ''"),
                ("FlavorText",         "TEXT NOT NULL DEFAULT ''"),
                ("ManaCost",           "TEXT NOT NULL DEFAULT ''"),
                ("ManaValue",          "REAL NOT NULL DEFAULT 0"),
                ("ColorIdentity",      "TEXT NOT NULL DEFAULT ''"),
                ("Colors",             "TEXT NOT NULL DEFAULT ''"),
                ("Rarity",             "TEXT NOT NULL DEFAULT ''"),
                ("Artist",             "TEXT NOT NULL DEFAULT ''"),
                ("Power",              "TEXT NOT NULL DEFAULT ''"),
                ("Toughness",          "TEXT NOT NULL DEFAULT ''"),
                ("IsFoilAvailable",    "INTEGER NOT NULL DEFAULT 0"),
                ("IsNonFoilAvailable", "INTEGER NOT NULL DEFAULT 0"),
                ("PriceUsd",           "REAL"),
                ("PriceUsdFoil",       "REAL"),
                ("ImageNormalUrl",     "TEXT NOT NULL DEFAULT ''"),
                ("LocalImagePath",     "TEXT NOT NULL DEFAULT ''"),
                ("LegalitiesJson",     "TEXT NOT NULL DEFAULT ''"),
                ("Notes",              "TEXT NOT NULL DEFAULT ''"),
            };
            foreach (var (col, def) in binderWantCols)
            {
                AddColumn("TradeBinderEntries", col, def);
                AddColumn("WantListEntries", col, def);
            }
        }


        /// <summary>
        /// Populates empty card-data fields in existing collection entries by
        /// joining with the pool database via ScryfallId.  Called on first run
        /// after schema upgrade, or manually from Tools menu.  Safe to call
        /// multiple times — only touches entries with empty Name fields.
        /// Returns the number of entries updated.
        /// </summary>
        public int MigrateCardData(AppDbContext pdb)
        {
            int total = 0;

            // ── Main collection ─────────────────────────────────────────
            var emptyEntries = CollectionEntries
                .Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "")
                .ToList();

            if (emptyEntries.Count > 0)
            {
                var sids = emptyEntries.Select(e => e.ScryfallId).Distinct().ToHashSet();
                var pool = pdb.PoolCards.AsNoTracking()
                    .Where(p => sids.Contains(p.ScryfallId))
                    .ToList().ToDictionary(p => p.ScryfallId);

                foreach (var ce in emptyEntries)
                {
                    if (!pool.TryGetValue(ce.ScryfallId, out var pc)) continue;
                    ce.OracleId = pc.OracleId;
                    ce.Name = pc.Name;
                    ce.ManaCost = pc.ManaCost;
                    ce.ManaValue = pc.ManaValue;
                    ce.TypeLine = pc.TypeLine;
                    ce.OracleText = pc.OracleText;
                    ce.FlavorText = pc.FlavorText;
                    ce.Power = pc.Power;
                    ce.Toughness = pc.Toughness;
                    ce.LoyaltyOrDefense = pc.LoyaltyOrDefense;
                    ce.Colors = pc.Colors;
                    ce.ColorIdentity = pc.ColorIdentity;
                    ce.SetCode = pc.SetCode;
                    ce.SetName = pc.SetName;
                    ce.SetType = pc.SetType;
                    ce.CollectorNumber = pc.CollectorNumber;
                    ce.Rarity = pc.Rarity;
                    ce.Artist = pc.Artist;
                    ce.ImageSmallUrl = pc.ImageSmallUrl;
                    ce.ImageNormalUrl = pc.ImageNormalUrl;
                    ce.ImageBackUrl = pc.ImageBackUrl;
                    ce.LocalImagePath = pc.LocalImagePath;
                    ce.LocalImageBackPath = pc.LocalImageBackPath;
                    ce.Layout = pc.Layout;
                    ce.IsFoilAvailable = pc.IsFoil;
                    ce.IsNonFoilAvailable = pc.IsNonFoil;
                    ce.IsToken = pc.IsToken;
                    ce.IsMeld = pc.IsMeld;
                    ce.ReleasedAt = pc.ReleasedAt;
                    ce.LegalitiesJson = pc.LegalitiesJson;
                    ce.IsFavorite = pc.IsFavorite;
                    ce.Keywords = pc.Keywords;
                    ce.PriceUsd = pc.PriceUsd;
                    ce.PriceUsdFoil = pc.PriceUsdFoil;
                    ce.PriceUsdEtched = pc.PriceUsdEtched;
                    ce.PriceEur = pc.PriceEur;
                    ce.PriceEurFoil = pc.PriceEurFoil;
                    ce.PriceTix = pc.PriceTix;
                    ce.PricesJson = pc.PricesJson;
                    total++;
                }
                SaveChanges();
            }

            // ── Token collection ────────────────────────────────────────
            total += MigrateSpecial(
                TokenCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.TokenCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, tc) => {
                    ce.OracleId = tc.OracleId; ce.Name = tc.Name; ce.TypeLine = tc.TypeLine;
                    ce.OracleText = tc.OracleText; ce.FlavorText = tc.FlavorText;
                    ce.Power = tc.Power; ce.Toughness = tc.Toughness;
                    ce.Colors = tc.Colors; ce.ColorIdentity = tc.ColorIdentity;
                    ce.SetCode = tc.SetCode; ce.SetName = tc.SetName; ce.SetType = tc.SetType;
                    ce.CollectorNumber = tc.CollectorNumber; ce.Rarity = tc.Rarity; ce.Artist = tc.Artist;
                    ce.ImageSmallUrl = tc.ImageSmallUrl; ce.ImageNormalUrl = tc.ImageNormalUrl;
                    ce.Layout = tc.Layout; ce.IsFoilAvailable = tc.IsFoil; ce.IsNonFoilAvailable = tc.IsNonFoil;
                    ce.ReleasedAt = tc.ReleasedAt; ce.LocalImagePath = tc.LocalImagePath; ce.IsFavorite = tc.IsFavorite;
                });

            // ── Planar collection ───────────────────────────────────────
            total += MigrateSpecial(
                PlanarCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.PlanarCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, pc) => {
                    ce.OracleId = pc.OracleId; ce.Name = pc.Name; ce.TypeLine = pc.TypeLine;
                    ce.OracleText = pc.OracleText; ce.FlavorText = pc.FlavorText;
                    ce.SetCode = pc.SetCode; ce.SetName = pc.SetName; ce.SetType = pc.SetType;
                    ce.CollectorNumber = pc.CollectorNumber; ce.Rarity = pc.Rarity; ce.Artist = pc.Artist;
                    ce.ImageSmallUrl = pc.ImageSmallUrl; ce.ImageNormalUrl = pc.ImageNormalUrl;
                    ce.Layout = pc.Layout; ce.IsFoilAvailable = pc.IsFoil; ce.IsNonFoilAvailable = pc.IsNonFoil;
                    ce.ReleasedAt = pc.ReleasedAt; ce.LocalImagePath = pc.LocalImagePath; ce.IsFavorite = pc.IsFavorite;
                });

            // ── Scheme collection ───────────────────────────────────────
            total += MigrateSpecial(
                SchemeCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.SchemeCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, sc) => {
                    ce.OracleId = sc.OracleId; ce.Name = sc.Name; ce.TypeLine = sc.TypeLine;
                    ce.OracleText = sc.OracleText; ce.FlavorText = sc.FlavorText;
                    ce.SetCode = sc.SetCode; ce.SetName = sc.SetName; ce.SetType = sc.SetType;
                    ce.CollectorNumber = sc.CollectorNumber; ce.Rarity = sc.Rarity; ce.Artist = sc.Artist;
                    ce.ImageSmallUrl = sc.ImageSmallUrl; ce.ImageNormalUrl = sc.ImageNormalUrl;
                    ce.Layout = sc.Layout; ce.IsFoilAvailable = sc.IsFoil; ce.IsNonFoilAvailable = sc.IsNonFoil;
                    ce.ReleasedAt = sc.ReleasedAt; ce.LocalImagePath = sc.LocalImagePath; ce.IsFavorite = sc.IsFavorite;
                });

            // ── Vanguard collection ─────────────────────────────────────
            total += MigrateSpecial(
                VanguardCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.VanguardCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, vc) => {
                    ce.OracleId = vc.OracleId; ce.Name = vc.Name; ce.TypeLine = vc.TypeLine;
                    ce.OracleText = vc.OracleText; ce.FlavorText = vc.FlavorText;
                    ce.SetCode = vc.SetCode; ce.SetName = vc.SetName; ce.SetType = vc.SetType;
                    ce.CollectorNumber = vc.CollectorNumber; ce.Rarity = vc.Rarity; ce.Artist = vc.Artist;
                    ce.ImageSmallUrl = vc.ImageSmallUrl; ce.ImageNormalUrl = vc.ImageNormalUrl;
                    ce.Layout = vc.Layout; ce.IsFoilAvailable = vc.IsFoil; ce.IsNonFoilAvailable = vc.IsNonFoil;
                    ce.ReleasedAt = vc.ReleasedAt; ce.LocalImagePath = vc.LocalImagePath; ce.IsFavorite = vc.IsFavorite;
                    ce.HandModifier = vc.HandModifier; ce.LifeModifier = vc.LifeModifier;
                });

            // ── Conspiracy collection ───────────────────────────────────
            total += MigrateSpecial(
                ConspiracyCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.ConspiracyCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, cc) => {
                    ce.OracleId = cc.OracleId; ce.Name = cc.Name; ce.TypeLine = cc.TypeLine;
                    ce.OracleText = cc.OracleText; ce.FlavorText = cc.FlavorText;
                    ce.ManaCost = cc.ManaCost; ce.ManaValue = cc.ManaValue;
                    ce.ColorIdentity = cc.ColorIdentity; ce.Colors = cc.Colors;
                    ce.SetCode = cc.SetCode; ce.SetName = cc.SetName; ce.SetType = cc.SetType;
                    ce.CollectorNumber = cc.CollectorNumber; ce.Rarity = cc.Rarity; ce.Artist = cc.Artist;
                    ce.ImageSmallUrl = cc.ImageSmallUrl; ce.ImageNormalUrl = cc.ImageNormalUrl;
                    ce.Layout = cc.Layout; ce.IsFoilAvailable = cc.IsFoil; ce.IsNonFoilAvailable = cc.IsNonFoil;
                    ce.ReleasedAt = cc.ReleasedAt; ce.LocalImagePath = cc.LocalImagePath; ce.IsFavorite = cc.IsFavorite;
                });

            // ── ArtSeries collection ────────────────────────────────────
            total += MigrateSpecial(
                ArtSeriesCollectionEntries.Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "").ToList(),
                sids => pdb.ArtSeriesCards.AsNoTracking().Where(c => sids.Contains(c.ScryfallId)).ToList().ToDictionary(c => c.ScryfallId),
                (ce, ac) => {
                    ce.OracleId = ac.OracleId; ce.Name = ac.Name; ce.TypeLine = ac.TypeLine;
                    ce.FlavorText = ac.FlavorText;
                    ce.SetCode = ac.SetCode; ce.SetName = ac.SetName; ce.SetType = ac.SetType;
                    ce.CollectorNumber = ac.CollectorNumber; ce.Rarity = ac.Rarity; ce.Artist = ac.Artist;
                    ce.ImageSmallUrl = ac.ImageSmallUrl; ce.ImageNormalUrl = ac.ImageNormalUrl;
                    ce.Layout = ac.Layout; ce.IsFoilAvailable = ac.IsFoil; ce.IsNonFoilAvailable = ac.IsNonFoil;
                    ce.ReleasedAt = ac.ReleasedAt; ce.LocalImagePath = ac.LocalImagePath; ce.IsFavorite = ac.IsFavorite;
                });

            // ── Trade Binder ────────────────────────────────────────────
            var emptyBinder = TradeBinderEntries
                .Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "")
                .ToList();
            if (emptyBinder.Count > 0)
            {
                // TradeBinder ScryfallIds come from PoolCards
                var bSids = emptyBinder.Select(e => e.ScryfallId).Distinct().ToHashSet();
                var bPool = pdb.PoolCards.AsNoTracking()
                    .Where(p => bSids.Contains(p.ScryfallId))
                    .ToList().ToDictionary(p => p.ScryfallId);
                foreach (var e in emptyBinder)
                {
                    if (!bPool.TryGetValue(e.ScryfallId, out var pc)) continue;
                    e.Name = pc.Name; e.SetCode = pc.SetCode; e.SetName = pc.SetName;
                    e.CollectorNumber = pc.CollectorNumber; e.TypeLine = pc.TypeLine;
                    e.OracleText = pc.OracleText; e.FlavorText = pc.FlavorText;
                    e.ManaCost = pc.ManaCost; e.ManaValue = pc.ManaValue;
                    e.ColorIdentity = pc.ColorIdentity; e.Colors = pc.Colors;
                    e.Rarity = pc.Rarity; e.Artist = pc.Artist;
                    e.Power = pc.Power; e.Toughness = pc.Toughness;
                    e.IsFoilAvailable = pc.IsFoil; e.IsNonFoilAvailable = pc.IsNonFoil;
                    e.PriceUsd = pc.PriceUsd; e.PriceUsdFoil = pc.PriceUsdFoil;
                    e.ImageNormalUrl = pc.ImageNormalUrl; e.LocalImagePath = pc.LocalImagePath;
                    e.LegalitiesJson = pc.LegalitiesJson;
                    total++;
                }
                SaveChanges();
            }

            // ── Want List (same pool lookup) ────────────────────────────
            var emptyWant = WantListEntries
                .Where(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "")
                .ToList();
            if (emptyWant.Count > 0)
            {
                var wSids = emptyWant.Select(e => e.ScryfallId).Distinct().ToHashSet();
                var wPool = pdb.PoolCards.AsNoTracking()
                    .Where(p => wSids.Contains(p.ScryfallId))
                    .ToList().ToDictionary(p => p.ScryfallId);
                foreach (var e in emptyWant)
                {
                    if (!wPool.TryGetValue(e.ScryfallId, out var pc)) continue;
                    e.Name = pc.Name; e.SetCode = pc.SetCode; e.SetName = pc.SetName;
                    e.CollectorNumber = pc.CollectorNumber; e.TypeLine = pc.TypeLine;
                    e.OracleText = pc.OracleText; e.FlavorText = pc.FlavorText;
                    e.ManaCost = pc.ManaCost; e.ManaValue = pc.ManaValue;
                    e.ColorIdentity = pc.ColorIdentity; e.Colors = pc.Colors;
                    e.Rarity = pc.Rarity; e.Artist = pc.Artist;
                    e.Power = pc.Power; e.Toughness = pc.Toughness;
                    e.IsFoilAvailable = pc.IsFoil; e.IsNonFoilAvailable = pc.IsNonFoil;
                    e.PriceUsd = pc.PriceUsd; e.PriceUsdFoil = pc.PriceUsdFoil;
                    e.ImageNormalUrl = pc.ImageNormalUrl; e.LocalImagePath = pc.LocalImagePath;
                    e.LegalitiesJson = pc.LegalitiesJson;
                    total++;
                }
                SaveChanges();
            }

            return total;
        }

        /// <summary>Returns true if any collection entries need card data migration.</summary>
        public bool NeedsMigration()
        {
            try
            {
                return CollectionEntries.Any(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "")
                    || TradeBinderEntries.Any(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "")
                    || WantListEntries.Any(e => (e.Name == null || e.Name == "") && e.ScryfallId != null && e.ScryfallId != "");
            }
            catch { return false; }
        }

        private int MigrateSpecial<TEntry, TCard>(
            List<TEntry> entries,
            Func<HashSet<string>, Dictionary<string, TCard>> loadCards,
            Action<TEntry, TCard> copyFields)
            where TEntry : class
        {
            if (entries.Count == 0) return 0;
            // Get ScryfallId via reflection (all special entries have it)
            var sidProp = typeof(TEntry).GetProperty("ScryfallId");
            if (sidProp == null) return 0;
            var sids = entries.Select(e => (string)sidProp.GetValue(e)!).Distinct().ToHashSet();
            var cards = loadCards(sids);
            int count = 0;
            foreach (var e in entries)
            {
                var sid = (string)sidProp.GetValue(e)!;
                if (cards.TryGetValue(sid, out var card))
                {
                    copyFields(e, card);
                    count++;
                }
            }
            if (count > 0) SaveChanges();
            return count;
        }

        // ── Helpers ────────────────────────────────────────────────────────
        private void AddColumn(string table, string col, string def)
        {
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(
                    $"ALTER TABLE {table} ADD COLUMN `{col}` {def}");
#pragma warning restore EF1002
            }
            catch { /* Column already exists — ignore */ }
        }

        private void CreateTableIfMissing(string sql)
        {
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
            }
            catch { }
        }

        private void SafeExec(string sql)
        {
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
            }
            catch { }
        }
    }
}