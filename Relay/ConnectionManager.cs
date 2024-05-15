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
        private Task _processingSendMessages = Task.CompletedTask;
        private CancellationTokenSource _cts;
        public ConcurrentDictionary<string, WebSocket> Connections { get; set; } = new();


        public ConnectionManager(StateManager stateManager, ILogger<ConnectionManager> logger,
            NostrEventService nostrEventService)
        {
            _stateManager = stateManager;
            _logger = logger;
            _nostrEventService = nostrEventService;
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
            if (!Connections.ContainsKey(e.ConnectionId))
            {
                _stateManager.RemoveConnection(e.ConnectionId);
                return;
            }

            foreach (var nostrEvent in e.Events)
            {
                await _stateManager.PendingMessages.Writer.WriteAsync((e.ConnectionId,
                    JsonSerializer.Serialize(new object[]
                    {
                        "EVENT",
                        e.SubscriptionId, nostrEvent
                    })));
            }
            e.OnSent.SetResult();
            _logger.LogInformation($"Sent {e.Events.Length} events to {e.ConnectionId} for subscription {e.SubscriptionId}");
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
                            _logger.LogTrace($"sending message to connection {evt.connectionId}\n{evt.message}");
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