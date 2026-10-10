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
        /// <summary>Oversized cards (Scryfall "oversized"): display commanders, league prizes …</summary>
        public DbSet<OversizedCard> OversizedCards { get; set; }
        /// <summary>The theme cards at the front of Jumpstart and similar packs.</summary>
        public DbSet<FrontCard> FrontCards { get; set; }

        // ── Other Tables ────────────────────────────────────────────────────
        public DbSet<AppSetting> AppSettings { get; set; }

        /// <summary>Null = the real pool database; otherwise this file (an update's temporary copy).</summary>
        private readonly string? _path;

        public AppDbContext() { }

        /// <summary>Open a specific file (Database Update works on a temporary copy).</summary>
        public AppDbContext(string path) { _path = path; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = _path ?? Services.AppFolderService.DatabasePath;
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

            // Tables added in a later version (Oversized, Front Cards): EnsureCreated
            // does nothing once a file has tables, so the model's own script runs
            // with IF NOT EXISTS (existing tables are left as they are).
            CreateMissingTables();

            // Conversion for pools built before a column existed: add it (no
            // data lost). Checked first, so there are no "duplicate column"
            // exceptions. The next Full Database Update fills it in.
            AddColumnIfMissing("PoolCards", "IsGameChanger", "INTEGER NOT NULL DEFAULT 0");
            // Tokens a card makes (Scryfall all_parts): suggestions in Edit → Decks.
            AddColumnIfMissing("PoolCards", "TokenIds", "TEXT NOT NULL DEFAULT ''");

            // Etched finish (Scryfall "finishes"), on every pool table.
            foreach (var table in new[] { "PoolCards", "TokenCards", "PlanarCards", "SchemeCards",
                                          "VanguardCards", "ArtSeriesCards", "ConspiracyCards" })
                AddColumnIfMissing(table, "IsEtched", "INTEGER NOT NULL DEFAULT 0");
        }

        private void CreateMissingTables()
        {
            string script = Database.GenerateCreateScript()
                .Replace("CREATE TABLE \"", "CREATE TABLE IF NOT EXISTS \"")
                .Replace("CREATE UNIQUE INDEX \"", "CREATE UNIQUE INDEX IF NOT EXISTS \"")
                .Replace("CREATE INDEX \"", "CREATE INDEX IF NOT EXISTS \"");
            Database.OpenConnection();
            try
            {
                using var cmd = Database.GetDbConnection().CreateCommand();
                cmd.CommandText = script;
                cmd.ExecuteNonQuery();
            }
            finally
            {
                Database.CloseConnection();
            }
        }

        private void AddColumnIfMissing(string table, string column, string definition)
        {
            var conn = Database.GetDbConnection();
            bool opened = false;
            if (conn.State != System.Data.ConnectionState.Open) { conn.Open(); opened = true; }
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
                    if (System.Convert.ToInt32(cmd.ExecuteScalar()) > 0) return;
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                    cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                if (opened) conn.Close();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Indexes for fast searching ───────────────────────────────────

            // PoolCards (paper). The online fields live only in the online
            // pool (OnlineDbContext → OnlineCards); this table never has them.
            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();
            modelBuilder.Entity<PoolCard>().Ignore(c => c.IsOnMtgo);
            modelBuilder.Entity<PoolCard>().Ignore(c => c.IsOnArena);
            modelBuilder.Entity<PoolCard>().Ignore(c => c.IsDigital);
            modelBuilder.Entity<PoolCard>().Ignore(c => c.MtgoId);
            modelBuilder.Entity<PoolCard>().Ignore(c => c.MtgoFoilId);
            modelBuilder.Entity<PoolCard>().Ignore(c => c.ArenaId);

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

            // OversizedCards: full cards, like PoolCards (paper only: no online fields).
            var over = modelBuilder.Entity<OversizedCard>();
            over.HasIndex(c => c.ScryfallId).IsUnique();
            over.HasIndex(c => c.Name);
            over.Ignore(c => c.IsOnMtgo);
            over.Ignore(c => c.IsOnArena);
            over.Ignore(c => c.IsDigital);
            over.Ignore(c => c.MtgoId);
            over.Ignore(c => c.MtgoFoilId);
            over.Ignore(c => c.ArenaId);

            // FrontCards
            modelBuilder.Entity<FrontCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // ── AppSettings key is already the primary key ───────────────────
            modelBuilder.Entity<AppSetting>()
                .HasKey(s => s.Key);
        }
    }
}