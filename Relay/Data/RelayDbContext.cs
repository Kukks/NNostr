using Microsoft.EntityFrameworkCore;

namespace Relay.Data
{
    public class RelayDbContext : DbContext
    {
        public DbSet<NostrEvent> Events { get; set; }

        public DbSet<NostrEventTag> EventTags { get; set; }
    }
}