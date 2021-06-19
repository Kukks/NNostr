using System.Text.Json;
using System.Threading.Tasks;
using Relay.Data;

namespace Relay
{
    public class EventNostrMessageHandler : INostrMessageHandler
    {
        private readonly NostrEventService _nostrEventService;
        private const string PREFIX = "EVENT";

        public EventNostrMessageHandler(NostrEventService nostrEventService)
        {
            _nostrEventService = nostrEventService;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }

            var json = JsonDocument.Parse(msg).RootElement;
            var e = JsonSerializer.Deserialize<NostrEvent>(json[1].GetRawText());
            if (e.Verify())
            {
                await _nostrEventService.AddEvent(e);
            }
        }
    }
}