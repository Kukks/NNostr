using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinqKit;
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

            var subscriptions = _stateManager.SubscriptionToFilter.Where(pair => pair.Value.Contains(e.FilterId)).Select(pair => pair.Key);

            var connections =
                _stateManager.ConnectionToSubscriptions.Where(pair => pair.Value.Any(s => subscriptions.Contains(s)));

            foreach (var connection in connections)
            {
                foreach (var subscription in subscriptions)
                {
                    if (connection.Value.Contains(subscription))
                    {
                        foreach (var nostrEvent in e.Events)
                        {
                            await _stateManager.PendingMessages.Writer.WriteAsync((connection.Key,
                                JsonSerializer.Serialize(new object[]
                                {
                                    "EVENT", subscription, nostrEvent
                                })));
                        }

                        if(_options.CurrentValue.EnableNip15 && e.Events.Any())
                        {
                            _stateManager.PendingMessages.Writer.TryWrite((connection.Key,
                                JsonSerializer.Serialize(new[]
                                {
                                    "EOSE",
                                    connection.Key
                                })));
                        }
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