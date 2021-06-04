using System.Threading.Tasks;

namespace NNostr.Relay
{
    public class EventNostrMessageHandler : INostrMessageHandler
    {
        private const string PREFIX = "EVENT ";

        public async Task Handle(string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var json = msg.Substring(PREFIX.Length);
        }
    }
}