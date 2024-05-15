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
        private readonly StateManager _stateManager;
        private readonly ILogger<WebSocketHandler> _logger;
        private ConnectionManager WebSocketWebSocketConnectionManager { get; set; }
        public event EventHandler<string>? NewConnection;

        public WebSocketHandler(
            ConnectionManager webSocketWebSocketConnectionManager,
            IEnumerable<INostrMessageHandler> nostrMessageHandlers,
            StateManager stateManager, 
            ILogger<WebSocketHandler> logger)
        {
            _nostrMessageHandlers = nostrMessageHandlers;
            _stateManager = stateManager;
            _logger = logger;
            WebSocketWebSocketConnectionManager = webSocketWebSocketConnectionManager;
        }


        public virtual async Task OnConnected(WebSocket socket)
        {
            var newConnection = Guid.NewGuid().ToString().Replace("-", "");
            WebSocketWebSocketConnectionManager.Connections.TryAdd(newConnection, socket);
            _logger.LogInformation($"New connection: {newConnection}");
            NewConnection?.Invoke(this, newConnection);
        }

        public virtual async Task OnDisconnected(WebSocket socket)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;

            if (id is not null)
            {
                _stateManager.RemoveConnection(id);
            }

            _logger.LogInformation($"Removed connection: {id}");
        }

        public Task ReceiveAsync(WebSocket socket, WebSocketReceiveResult result, string msg)
        {
            var id = WebSocketWebSocketConnectionManager.Connections.FirstOrDefault(pair => pair.Value == socket).Key;

            _logger.LogTrace($"Received message from connection: {id} \n{msg}");
            return Task.WhenAll(_nostrMessageHandlers.AsParallel().Select(handler => handler.Handle(id, msg)));
        }
    }
}