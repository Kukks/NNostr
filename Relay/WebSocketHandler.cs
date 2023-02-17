using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Relay
{
    public class WebSocketHandler
    {
        private readonly IEnumerable<INostrMessageHandler> _nostrMessageHandlers;
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;
        private readonly ILogger<WebSocketHandler> _logger;
        private ConnectionManager WebSocketWebSocketConnectionManager { get; set; }

        public WebSocketHandler(ConnectionManager webSocketWebSocketConnectionManager,
            IEnumerable<INostrMessageHandler> nostrMessageHandlers,
            NostrEventService nostrEventService, StateManager stateManager, ILogger<WebSocketHandler> logger)
        {
            _nostrMessageHandlers = nostrMessageHandlers;
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
            _logger = logger;
            WebSocketWebSocketConnectionManager = webSocketWebSocketConnectionManager;
        }

        public virtual async Task OnConnected(WebSocket socket)
        {
            var newConnection = Guid.NewGuid().ToString();
            WebSocketWebSocketConnectionManager.Connections.TryAdd(newConnection, socket);
            _logger.LogInformation($"New connection: {newConnection}");
        }

        public virtual async Task OnDisconnected(WebSocket socket)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;
            
            if (id is not null && WebSocketWebSocketConnectionManager.Connections.TryRemove(id, out _) &&
                _stateManager.ConnectionToSubscriptions.TryGetValues(id, out var subscriptions))
            {
                var orphanedFilters = new List<string>();
                
                foreach (var subscription in subscriptions.ToArray())
                {
                    _stateManager.RemoveSubscription(id, subscription, out var orphanedFilters2);
                    orphanedFilters = orphanedFilters.Union(orphanedFilters2).ToList();
                }

                orphanedFilters.ForEach(_nostrEventService.RemoveFilter);
            }
            
            _logger.LogInformation($"Removed connection: {id}");
        }

        public Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, string msg)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;
            
            return Task.WhenAll(_nostrMessageHandlers.AsParallel().Select(handler => handler.Handle(id, msg)));
        }
    }
}