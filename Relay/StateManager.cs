using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NNostr.Client;

namespace Relay
{
    public class StateManager
    {
        private readonly ILogger<StateManager> _logger;


        public ConcurrentDictionary<string, WebSocket> Connections { get; set; } = new();
        public readonly ConcurrentMultiDictionary<string, string> ConnectionToSubscriptions =
            new();

        public readonly ConcurrentMultiDictionary<string, NostrSubscriptionFilter[]> ConnectionSubscriptionsToFilters =
            new();

        public readonly ConcurrentDictionary<string, Channel<string>> ConnectionPendingMessages = new();
        public readonly ConcurrentDictionary<string, CancellationTokenSource> ConnectionChannel = new();


        public StateManager(ILogger<StateManager> logger)
        {
            _logger = logger;
        }
        
        public void RemoveConnection(string connectionId)
        {
            if (ConnectionToSubscriptions.Remove(connectionId, out var subscriptions))
            {
                foreach (var subscription in subscriptions)
                {
                    ConnectionSubscriptionsToFilters.Remove($"{connectionId}-{subscription}");
                }
            }

            if (ConnectionPendingMessages.Remove(connectionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
            if (ConnectionChannel.Remove(connectionId, out var cts))
            {
                cts.Cancel();
            }
        }

        public void AddSubscription(string connectionId, string id, NostrSubscriptionFilter[] filters)
        {
            ConnectionToSubscriptions.Add(connectionId, id);
            ConnectionSubscriptionsToFilters.AddOrReplace($"{connectionId}-{id}", filters);
        }


        public void RemoveSubscription(string connectionId, string id)
        {
            ConnectionToSubscriptions.Remove(connectionId, id);
            ConnectionSubscriptionsToFilters.Remove($"{connectionId}-{id}");
        }

        public async Task Send(string connection, string message)
        {
            if(ConnectionPendingMessages.TryGetValue(connection, out var channel))
            {
                await channel.Writer.WriteAsync(message);
            }
        }

        public async Task SendEvent(string connectionId, string subscriptionId, RelayNostrEvent e)
        {
            await Send(connectionId,
                JsonSerializer.Serialize(new object[]
                {
                    "EVENT",
                    subscriptionId, e
                }));
        }

        public async Task SendEOSE(string connectionId, string subscriptionId)
        {
            await Send(connectionId,
                JsonSerializer.Serialize(new[]
                {
                    "EOSE",
                    subscriptionId
                }));
        }
        
        public  async Task SendOk(string connection, string eventId, bool success, string reason)
        {
            await Send(connection, JsonSerializer.Serialize(new object[]
            {
                "OK",
                eventId,
                success,
                reason
            }));
        }

        public void AddConnection(string newConnection)
        {
            var channel = ConnectionPendingMessages.GetOrAdd(newConnection, Channel.CreateUnbounded<string>());
            var cts = new CancellationTokenSource();
            if(ConnectionChannel.TryAdd(newConnection, cts))
            {
                _ = ProcessSendMessages(newConnection, channel, cts.Token);
                
            }
        }
        
        private async Task ProcessSendMessages(string connectionId, Channel<string> channel, CancellationToken cancellationToken)
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (channel.Reader.TryRead(out var message))
                {
                    try
                    {
                        if (Connections.TryGetValue(connectionId, out var conn))
                        {
                            _logger.LogTrace($"sending message to connection {connectionId}\n{message}");
                            await conn.SendMessageAsync(message, cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"Had to send a message to a connection that no longer exists {message}");
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
        
    }
}