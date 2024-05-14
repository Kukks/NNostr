using System.Threading.Tasks;

namespace Relay
{
    public interface INostrMessageHandler
    {
        public Task Handle(string connectionId, string msg) {
            try {
                return HandleCore(connectionId, msg);
            } catch {
                return Task.CompletedTask;
            }
        }
        public Task HandleCore(string connectionId, string msg);
    }
}