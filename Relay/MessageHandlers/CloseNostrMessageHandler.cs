using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Relay
{
    public class CloseNostrMessageHandler : INostrMessageHandler
    {
        private readonly StateManager _stateManager;
        private readonly NostrEventService _nostrEventService;
        private readonly ILogger<CloseNostrMessageHandler> _logger;
        private const string PREFIX = "CLOSE";

        public CloseNostrMessageHandler(StateManager stateManager, NostrEventService nostrEventService, ILogger<CloseNostrMessageHandler> logger)
        {
            _stateManager = stateManager;
            _nostrEventService = nostrEventService;
            _logger = logger;
        }

        public async Task HandleCore(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }
            var json = JsonDocument.Parse(msg).RootElement;
            var id = $"{connectionId}_{json[1].GetString()}";
            _stateManager.RemoveSubscription(connectionId, id);
        }
    }
}