using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;

namespace Relay
{
    public class ConnectionManager : IHostedService
    {
        private readonly StateManager _stateManager;
        private readonly ILogger<ConnectionManager> _logger;
        private readonly NostrEventService _nostrEventService;
        private readonly IOptionsMonitor<RelayOptions> _options;
        private Task _processingSendMessages = Task.CompletedTask;
        private CancellationTokenSource _cts;
        public ConcurrentDictionary<string, WebSocket> Connections { get; set; } = new();


        public ConnectionManager(StateManager stateManager, ILogger<ConnectionManager> logger,
            NostrEventService nostrEventService, IOptionsMonitor<RelayOptions> options)
        {
            _stateManager = stateManager;
            _logger = logger;
            _nostrEventService = nostrEventService;
            _options = options;
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            _processingSendMessages = ProcessSendMessages(_cts.Token);
            _nostrEventService.EventsMatched += (sender, matched) => { _ = NostrEventServiceOnEventsMatched(matched); };
            return Task.CompletedTask;
        }

        private async Task NostrEventServiceOnEventsMatched(NostrEventsMatched e)
        {

            var subscriptions = _stateManager.SubscriptionToFilter.GetKeysContainingValue(e.FilterId);
            var connections = _stateManager.ConnectionToSubscriptions.GetKeysContainingValue(subscriptions);;

            foreach (var connectionId in connections)
            {
                
                if (!_stateManager.ConnectionToSubscriptions.TryGetValues(connectionId, out var connectionSubscriptions))
                {
                    continue;
                }
                if (!Connections.ContainsKey(connectionId))
                {
                    _stateManager.RemoveConnection(connectionId, out var orphanedFilters);
                    orphanedFilters.ForEach(x => _nostrEventService.RemoveFilter(x));
                    continue;
                }
                foreach (var subscription in subscriptions)
                {
                    if (connectionSubscriptions.Contains(subscription))
                    {
                        foreach (var nostrEvent in e.Events)
                        {
                            await _stateManager.PendingMessages.Writer.WriteAsync((connectionId,
                                JsonSerializer.Serialize(new object[]
                                {
                                    "EVENT", subscription, nostrEvent
                                })));
                        }
                        e.OnEventsSent?.Invoke((connectionId, e));
                    }
                }

            }
        }

        private async Task ProcessSendMessages(CancellationToken cancellationToken)
        {
            while (await _stateManager.PendingMessages.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_stateManager.PendingMessages.Reader.TryRead(out var evt))
                {
                    try
                    {
                        if (Connections.TryGetValue(evt.Item1, out var conn))
                        {
                            await conn.SendMessageAsync(evt.Item2, cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"Had to send a message to a connection that no longer exists {evt.Item1}");
                        }
                    }
                    catch when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Unhandled exception in {this.GetType().Name}");
                    }
                }
            }
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            try
            {
                await _processingSendMessages;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}