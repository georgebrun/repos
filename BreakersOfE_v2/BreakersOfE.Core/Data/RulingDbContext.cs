using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BreakersOfE.Data
{
    public class RulingsDbContext : DbContext
    {
        private static readonly string _dbPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "rulings.db");

        public DbSet<CardRuling> CardRulings { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={_dbPath}");

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<CardRuling>().HasKey(r => r.RulingId);
            model.Entity<CardRuling>().HasIndex(r => r.ScryfallId);
        }

        public void EnsureCreated() => Database.EnsureCreated();
    }

    [Table("CardRulings")]
    public class CardRuling
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int RulingId { get; set; }
        public string ScryfallId { get; set; } = string.Empty;
        public string PublishedAt { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }
}