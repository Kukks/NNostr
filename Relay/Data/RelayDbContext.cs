using Microsoft.EntityFrameworkCore;
using NNostr.Client;

namespace Relay.Data
{
    public class RelayDbContext : DbContext
    {
        public DbSet<NostrEvent> Events { get; set; }

        public DbSet<NostrEventTag> EventTags { get; set; }
        public const string DatabaseConnectionStringName = "RelayDatabase";

        public RelayDbContext(DbContextOptions<RelayDbContext> options):base(options)
        {
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<NostrEventTag>()
                .HasOne(p => p.Event)
                .WithMany(b => b.Tags);
        }
    }
}