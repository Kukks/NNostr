using Microsoft.EntityFrameworkCore;

namespace Relay.Data
{
    public class RelayDbContext : DbContext
    {
        public DbSet<Balance> Balances { get; set; }
        public DbSet<BalanceTopup> BalanceTopups { get; set; }
        public DbSet<BalanceTransaction> BalanceTransactions { get; set; }
        public DbSet<RelayNostrEventTag> EventTags { get; set; }
        public DbSet<RelayNostrEvent> Events { get; set; }
        public const string DatabaseConnectionStringName = "RelayDatabase";

        public RelayDbContext(DbContextOptions<RelayDbContext> options):base(options)
        {
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RelayNostrEventTag>()
                .HasOne(p => p.Event)
                .WithMany(b => b.Tags);
                     
        }
    }
}