using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Relay
{
    public class WebSocketHandler
    {
        private readonly IEnumerable<INostrMessageHandler> _nostrMessageHandlers;
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;
        private ConnectionManager WebSocketWebSocketConnectionManager { get; set; }

        public WebSocketHandler(ConnectionManager webSocketWebSocketConnectionManager,
            IEnumerable<INostrMessageHandler> nostrMessageHandlers,
            NostrEventService nostrEventService, StateManager stateManager)
        {
            _nostrMessageHandlers = nostrMessageHandlers;
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
            WebSocketWebSocketConnectionManager = webSocketWebSocketConnectionManager;
        }

        public virtual async Task OnConnected(WebSocket socket)
        {
            WebSocketWebSocketConnectionManager.Connections.TryAdd(Guid.NewGuid().ToString(), socket);
        }

        public virtual async Task OnDisconnected(WebSocket socket)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;
            if (id is not null && WebSocketWebSocketConnectionManager.Connections.TryRemove(id, out _) &&
                _stateManager.ConnectionToSubscriptions.TryGetValue(id, out var subscriptions))
            {
                var orphanedFilters = new List<string>();
                foreach (var subscription in subscriptions)
                {
                    _stateManager.RemoveSubscription(id, subscription, out var orphanedFilters2);
                    orphanedFilters = orphanedFilters.Union(orphanedFilters2).ToList();
                }

                orphanedFilters.ForEach(_nostrEventService.RemoveFilter);
            }
        }

        public Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, string msg)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;
            
            return Task.WhenAll(_nostrMessageHandlers.AsParallel().Select(handler => handler.Handle(id, msg)));
        }
    }
}