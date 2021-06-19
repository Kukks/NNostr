using Microsoft.EntityFrameworkCore;

namespace Relay.Data
{
    public class RelayDbContextDesignTimeDbContextFactory :
        DesignTimeDbContextFactoryBase<RelayDbContext>
    {
        public override string DefaultConnectionStringName { get; } = RelayDbContext.DatabaseConnectionStringName;

        protected override RelayDbContext CreateNewInstance(DbContextOptions<RelayDbContext> options)
        {
            return new RelayDbContext(options);
        }
    }
}