using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Relay
{
    public class ConnectionManager : IHostedService
    {
        private readonly StateManager _stateManager;
        private readonly ILogger<ConnectionManager> _logger;
        private readonly NostrEventService _nostrEventService;
        


        public ConnectionManager(StateManager stateManager, ILogger<ConnectionManager> logger,
            NostrEventService nostrEventService)
        {
            _stateManager = stateManager;
            _logger = logger;
            _nostrEventService = nostrEventService;
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _nostrEventService.EventsMatched += (sender, matched) => { _ = NostrEventServiceOnEventsMatched(matched); };
            return Task.CompletedTask;
        }

        private async Task NostrEventServiceOnEventsMatched(NostrEventsMatched e)
        {
            if (!_stateManager.Connections.ContainsKey(e.ConnectionId))
            {
                _stateManager.RemoveConnection(e.ConnectionId);
                return;
            }

            foreach (var nostrEvent in e.Events)
            {
                await _stateManager.SendEvent(e.ConnectionId,e.SubscriptionId, nostrEvent);
            }
            _logger.LogInformation($"Sent {e.Events.Length} events to {e.ConnectionId} for subscription {e.SubscriptionId}");
        }

       

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
        }
    }
}