using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class NostrEventsMatched
    {
        public string FilterId { get; set; }
        public RelayNostrEvent[] Events { get; set; }
    }
}