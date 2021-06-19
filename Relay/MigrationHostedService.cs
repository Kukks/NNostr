using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Relay.Data;

namespace Relay
{
    public class MigrationHostedService : IHostedService
    {
        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        private readonly ILogger<MigrationHostedService> _logger;

        public MigrationHostedService(
            IDbContextFactory<RelayDbContext> dbContextFactory, ILogger<MigrationHostedService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation($"Migrating database to latest version");
                await using var context = _dbContextFactory.CreateDbContext();
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
                _logger.LogInformation(pendingMigrations.Any()
                    ? $"Running migrations: {string.Join(", ", pendingMigrations)}"
                    : $"Database already at latest version");
                await context.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error on the MigrationStartupTask");
                throw;
            }
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}