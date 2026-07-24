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

        // ── Other Tables ────────────────────────────────────────────────────
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = Services.AppFolderService.DatabasePath;
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        /// <summary>
        /// Ensures the pool database exists with the current schema.
        /// The pool is wiped and rebuilt on every full update, so we don't
        /// need incremental migrations — just ensure the tables exist.
        /// 
        /// Replaces the old MigrateSchema() which used ALTER TABLE ADD COLUMN
        /// wrapped in try/catch blocks, causing hundreds of "duplicate column"
        /// exceptions on every startup.
        /// </summary>
        public void EnsureSchema()
        {
            Database.EnsureCreated();
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