using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
            if (!_stateManager.FilterToConnection.TryGetValue(e.FilterId, out var connections)) return;
            foreach (var connection in connections)
            {
                await _stateManager.PendingMessages.Writer.WriteAsync(
                    new Tuple<string, string>(connection, JsonSerializer.Serialize(e.Events)));
            }
        }

        private async Task SendMessageAsync(WebSocket socket, string message, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open)
                return;

            await socket.SendAsync(buffer: new ArraySegment<byte>(array: Encoding.UTF8.GetBytes(message),
                    offset: 0,
                    count: message.Length),
                messageType: WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: cancellationToken);
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
                            await SendMessageAsync(conn, evt.Item2, cancellationToken);
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