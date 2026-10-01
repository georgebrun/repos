using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Data
{
    /// <summary>
    /// The ONLINE card pool: every printing available on Magic Online (MTGO)
    /// or MTG Arena, including digital-only cards (Alchemy, MTGO-only sets).
    ///
    /// Same file as the paper pool (breakersofe.db, so one Database Update
    /// builds both and swaps them in together) but its own table, OnlineCards.
    /// The paper pool (AppDbContext → PoolCards) is never touched by it, and
    /// nothing that reads the paper pool — collection, decks, Owned, prices,
    /// Set Completion — ever sees an online card.
    ///
    /// Rows are PoolCard objects, so the shared grid, gallery and detail panel
    /// show them like any pool card; IsOnMtgo / IsOnArena pick the table shown.
    /// </summary>
    public class OnlineDbContext : DbContext
    {
        public DbSet<PoolCard> OnlineCards { get; set; }

        /// <summary>Null = the real pool database; otherwise this file (an update's temporary copy).</summary>
        private readonly string? _path;

        public OnlineDbContext() { }

        /// <summary>Open a specific file (Database Update works on a temporary copy).</summary>
        public OnlineDbContext(string path) { _path = path; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = _path ?? Services.AppFolderService.DatabasePath;
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var e = modelBuilder.Entity<PoolCard>();
            e.ToTable("OnlineCards");
            e.Ignore(c => c.TokenIds);        // token links: paper pool only (for now)
            e.HasIndex(c => c.ScryfallId).IsUnique();
            e.HasIndex(c => c.Name);
            e.HasIndex(c => c.IsOnMtgo);
            e.HasIndex(c => c.IsOnArena);
        }

        /// <summary>
        /// Create the OnlineCards table (and its indexes) if this file doesn't
        /// have it yet. The file already holds the paper tables, so EnsureCreated
        /// would do nothing; the model's own script is run with IF NOT EXISTS.
        /// </summary>
        public void EnsureSchema()
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
    }
}
