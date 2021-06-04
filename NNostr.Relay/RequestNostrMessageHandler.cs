using System;
using System.Threading.Tasks;

namespace NNostr.Relay
{
    public class RequestNostrMessageHandler : INostrMessageHandler
    {
        private readonly SubscriptionService _subscriptionService;

        public RequestNostrMessageHandler(SubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        private const string PREFIX = "REQ ";

        public async Task Handle(string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var body = msg.Substring(PREFIX.Length);
            var splitIndex = body.IndexOf(" ", StringComparison.InvariantCulture);
            var id = body.Substring(0, splitIndex);
            var json = body.Substring(splitIndex + 1);
        }
    }
}