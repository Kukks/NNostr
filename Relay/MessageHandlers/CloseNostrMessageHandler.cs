using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Relay
{
    public class CloseNostrMessageHandler : INostrMessageHandler
    {
        private readonly StateManager _stateManager;
        private readonly NostrEventService _nostrEventService;
        private const string PREFIX = "CLOSE";

        public CloseNostrMessageHandler(StateManager stateManager, NostrEventService nostrEventService)
        {
            _stateManager = stateManager;
            _nostrEventService = nostrEventService;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }
            var json = JsonDocument.Parse(msg).RootElement;

            var id = $"{connectionId}_{json[1].GetString()}";
            _stateManager.RemoveSubscription(connectionId, id, out var orphanedFilters);
            foreach (var orphanedFilter in orphanedFilters)
            {
                _nostrEventService.RemoveFilter(orphanedFilter);
            }
        }
    }
}