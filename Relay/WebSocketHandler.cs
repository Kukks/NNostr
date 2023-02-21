using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using NNostr.Client;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Relay
{
    public class WebSocketHandler
    {
        private readonly IOptionsMonitor<RelayOptions> _options;
        private readonly IEnumerable<INostrMessageHandler> _nostrMessageHandlers;
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;
        private readonly ILogger<WebSocketHandler> _logger;
        private ConnectionManager WebSocketWebSocketConnectionManager { get; set; }

        public WebSocketHandler(
            IOptionsMonitor<RelayOptions> options,
            ConnectionManager webSocketWebSocketConnectionManager,
            IEnumerable<INostrMessageHandler> nostrMessageHandlers,
            NostrEventService nostrEventService, StateManager stateManager, ILogger<WebSocketHandler> logger)
        {
            _options = options;
            _nostrMessageHandlers = nostrMessageHandlers;
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
            _logger = logger;
            WebSocketWebSocketConnectionManager = webSocketWebSocketConnectionManager;
            _options.OnChange(relayOptions =>
            {
                if (relayOptions.AdminPrivateKey is not null)
                {
                    TemporaryAdminPrivateKey = null;
                }
            });
        }

        public static ECPrivKey? TemporaryAdminPrivateKey { get; internal set; }

        public virtual async Task OnConnected(WebSocket socket)
        {
            var newConnection = Guid.NewGuid().ToString();
            WebSocketWebSocketConnectionManager.Connections.TryAdd(newConnection, socket);
            _logger.LogInformation($"New connection: {newConnection}");

            if (_options.CurrentValue.AdminKey is null)
            {
                if (_options.CurrentValue.AdminPrivateKey is null && TemporaryAdminPrivateKey is null)
                {
          
                    Context.Instance.TryCreateECPrivKey(RandomUtils.GetBytes(32), out var privKey);
                    TemporaryAdminPrivateKey = privKey;
                    _logger.LogInformation("Admin private key is null, generated a temporary one so that user can configure relay");
                }

                if (_options.CurrentValue.AdminPrivateKey is null)
                {
                    string message = $"This relay has not yet been configured. We have generated a temporary admin key that you can use to configure. Simply import the following {TemporaryAdminPrivateKey.ToHex()} and send a DM to itself with \"/admin config\" to see config and \"/admin update {{CONFIG}} to set config.";
                    _stateManager.PendingMessages.Writer.TryWrite((newConnection,
                        JsonSerializer.Serialize(new[] {"NOTICE", message})));
                }
            }
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