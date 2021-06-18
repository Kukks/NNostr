using Relay.Data;

namespace Relay
{
    public class NostrEventsMatched
    {
        public string FilterId { get; set; }
        public NostrEvent[] Events { get; set; }
    }
}