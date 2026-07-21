using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BreakersOfE.Data
{
    public class AppDbContext : DbContext
    {
        // ── Card Pool Tables ────────────────────────────────────────────────
        public DbSet<PoolCard> PoolCards { get; set; }
        public DbSet<TokenCard> TokenCards { get; set; }
        public DbSet<PlanarCard> PlanarCards { get; set; }
        public DbSet<SchemeCard> SchemeCards { get; set; }
        public DbSet<VanguardCard> VanguardCards { get; set; }
        public DbSet<ArtSeriesCard> ArtSeriesCards { get; set; }
        public DbSet<ConspiracyCard> ConspiracyCards { get; set; }

        // ── Collection Tables ───────────────────────────────────────────────

        // ── Other Tables ────────────────────────────────────────────────────
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Always store the database next to the executable
            string dbPath = Services.AppFolderService.DatabasePath;

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        // ── Schema migration for new columns ─────────────────────────────────────
        public void MigrateSchema()
        {
            // Create ConspiracyCards table if it doesn't exist yet
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ConspiracyCards (
                        ConspiracyId        INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScryfallId          TEXT NOT NULL DEFAULT '',
                        OracleId            TEXT NOT NULL DEFAULT '',
                        Name                TEXT NOT NULL DEFAULT '',
                        TypeLine            TEXT NOT NULL DEFAULT '',
                        OracleText          TEXT NOT NULL DEFAULT '',
                        FlavorText          TEXT NOT NULL DEFAULT '',
                        SetCode             TEXT NOT NULL DEFAULT '',
                        SetName             TEXT NOT NULL DEFAULT '',
                        SetType             TEXT NOT NULL DEFAULT '',
                        CollectorNumber     TEXT NOT NULL DEFAULT '',
                        Rarity              TEXT NOT NULL DEFAULT '',
                        Artist              TEXT NOT NULL DEFAULT '',
                        ManaCost            TEXT NOT NULL DEFAULT '',
                        ManaValue           REAL NOT NULL DEFAULT 0,
                        ColorIdentity       TEXT NOT NULL DEFAULT '',
                        Colors              TEXT NOT NULL DEFAULT '',
                        ImageSmallUrl       TEXT NOT NULL DEFAULT '',
                        ImageNormalUrl      TEXT NOT NULL DEFAULT '',
                        Layout              TEXT NOT NULL DEFAULT '',
                        IsFoil              INTEGER NOT NULL DEFAULT 0,
                        IsNonFoil           INTEGER NOT NULL DEFAULT 1,
                        ReleasedAt          TEXT NOT NULL DEFAULT '',
                        LocalImagePath      TEXT NOT NULL DEFAULT '',
                        IsFavorite          INTEGER NOT NULL DEFAULT 0
                    )");
                Database.ExecuteSqlRaw(
                    "CREATE UNIQUE INDEX IF NOT EXISTS IX_ConspiracyCards_ScryfallId ON ConspiracyCards (ScryfallId)");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_ConspiracyCards_Name ON ConspiracyCards (Name)");
#pragma warning restore EF1002
            }
            catch { }

            // DFC back face image columns for PoolCards
            var dfcColumns = new[]
            {
                ("PoolCards", "ImageBackUrl",       "TEXT NOT NULL DEFAULT ''"),
                ("PoolCards", "LocalImageBackPath", "TEXT NOT NULL DEFAULT ''"),
                ("PoolCards", "Keywords",           "TEXT NOT NULL DEFAULT ''"),
            };
            foreach (var (table, col, def) in dfcColumns)
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
        }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Indexes for fast searching ───────────────────────────────────

            // PoolCards
            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.Name);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.SetCode);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.ColorIdentity);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.Rarity);

            // TokenCards
            modelBuilder.Entity<TokenCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            modelBuilder.Entity<TokenCard>()
                .HasIndex(c => c.Name);

            // PlanarCards
            modelBuilder.Entity<PlanarCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // SchemeCards
            modelBuilder.Entity<SchemeCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // VanguardCards
            modelBuilder.Entity<VanguardCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // ArtSeriesCards
            modelBuilder.Entity<ArtSeriesCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // ConspiracyCards
            modelBuilder.Entity<ConspiracyCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            modelBuilder.Entity<ConspiracyCard>()
                .HasIndex(c => c.Name);

            // ── AppSettings key is already the primary key ───────────────────
            modelBuilder.Entity<AppSetting>()
                .HasKey(s => s.Key);
        }
    }
}