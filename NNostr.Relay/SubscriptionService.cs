using System.Collections.Generic;
using System.Net.WebSockets;

namespace NNostr.Relay
{
    public class SubscriptionService
    {
        public MultiValueDictionary<string, WebSocket> WebhookSubscriptions { get; set; } =
            new MultiValueDictionary<string, WebSocket>();

        public Dictionary<string, SubscriptionFilter> Filters { get; set; } =
            new Dictionary<string, SubscriptionFilter>();
    }
}