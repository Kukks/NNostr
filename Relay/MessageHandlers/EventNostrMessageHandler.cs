using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Relay.Data;

namespace Relay
{
    public class EventNostrMessageHandler : INostrMessageHandler
    {
        private readonly NostrEventService _nostrEventService;
        private readonly ILogger<EventNostrMessageHandler> _logger;
        private const string PREFIX = "EVENT";

        public EventNostrMessageHandler(NostrEventService nostrEventService, ILogger<EventNostrMessageHandler> logger)
        {
            _nostrEventService = nostrEventService;
            _logger = logger;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }

            _logger.LogInformation($"Handling Event message for connection: {connectionId} \n{msg}");
            var json = JsonDocument.Parse(msg).RootElement;
            var e = JsonSerializer.Deserialize<NostrEvent>(json[1].GetRawText());
            if (e.Verify())
            {
                await _nostrEventService.AddEvent(e);
            }
        }
    }
}