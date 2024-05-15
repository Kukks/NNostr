namespace Relay
{
    public class NostrEventsMatched
    {
        public string ConnectionId { get; set; }
        public string SubscriptionId { get; set; }
        public RelayNostrEvent[] Events { get; set; }
    }
}