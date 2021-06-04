using System.Threading.Tasks;

namespace NNostr.Relay
{
    public class CloseNostrMessageHandler : INostrMessageHandler
    {
        private const string PREFIX = "CLOSE ";
        private readonly SubscriptionService _subscriptionService;

        public CloseNostrMessageHandler(SubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;
        }

        public async Task Handle(string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var id = msg.Substring(PREFIX.Length);
            _subscriptionService.WebhookSubscriptions.Remove(id);
            _subscriptionService.Filters.Remove(id);
        }
    }
}