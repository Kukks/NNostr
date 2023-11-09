using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Data;

namespace Relay
{
    public class Purger : IHostedService
    {
        private readonly IOptionsMonitor<RelayOptions> _monitor;
        private readonly ILogger<Purger> _logger;
        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        private int? _lastPurgeValue = null;
        private IDisposable _monitorWatch;

        public Purger(IOptionsMonitor<RelayOptions> monitor, ILogger<Purger> logger,
            IDbContextFactory<RelayDbContext> dbContextFactory)
        {
            _monitor = monitor;
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            
            var oct = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Loop(oct.Token);
            _monitorWatch = _monitor.OnChange(options =>
            {
                if (_lastPurgeValue != options.PurgeAfterDays)
                {
                    _lastPurgeValue = options.PurgeAfterDays;
                    if (!oct.IsCancellationRequested)
                    {
                        oct.Dispose();
                    }
                    var ct = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    
                    _ = Loop(ct.Token);
                }
            })!;

                
                
        }


        private async Task Loop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Purge();
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }
        private async Task Purge()
        {
            if (_monitor.CurrentValue.PurgeAfterDays is null or <= 0)
            {
                return;
            }
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var paging = 1000;
            var counter = 0;
            var potentiallyMoreResults = true;
            while (potentiallyMoreResults)
            {
                var evts = context.Events.Where(e =>
                    e.CreatedAt == null ||
                    e.CreatedAt < DateTime.UtcNow.AddDays(-_monitor.CurrentValue.PurgeAfterDays.Value)).Take(paging).ToList();
                context.Events.RemoveRange(evts);
                await context.SaveChangesAsync();
                potentiallyMoreResults = evts.Count == paging;
                counter += evts.Count;
            }
            _logger.LogInformation($"Purged {counter} old events");
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _monitorWatch.Dispose();
        }
    }

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