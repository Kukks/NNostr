using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
namespace Relay
{
    public class RequestNostrMessageHandler : INostrMessageHandler
    {
        private readonly IOptionsMonitor<RelayOptions> _options;
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;
        private readonly ILogger<RequestNostrMessageHandler> _logger;


        private const string PREFIX = "REQ";

        public RequestNostrMessageHandler(
            IOptionsMonitor<RelayOptions> options,
            NostrEventService nostrEventService,
            StateManager stateManager, ILogger<RequestNostrMessageHandler> logger)
        {
            _options = options;
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
            _logger = logger;
        }

        public Microsoft.Extensions.Logging.ILogger Logger => _logger;

        public async Task HandleCore(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }

            var json = JsonDocument.Parse(msg).RootElement;

            var id = json[1].GetString();
            var filters = new List<NostrSubscriptionFilter>();
            for (var i = 2; i < json.GetArrayLength(); i++)
            {
                filters.Add(JsonSerializer.Deserialize<NostrSubscriptionFilter>(json[i].GetRawText()));
            }

            _stateManager.AddSubscription(connectionId, id, filters.ToArray());
            _logger.LogInformation($"Added subscription {id} for {connectionId}");
            var matchedEvents = await _nostrEventService.GetFromDB(filters.ToArray());
            using (matchedEvents.Item1)
            {
                var count = 0;
                await foreach (var e in matchedEvents.Item2)
                {
                    await _stateManager.SendEvent(connectionId, id, e);
                    count++;
                }
                
                _logger.LogInformation($"sent {count} initial events to {connectionId} for subscription {id}");
            }

            if (_options.CurrentValue.EnableNip15)
            {
                await _stateManager.SendEOSE(connectionId, id);
            }
        }

       
    }
}