using System.Threading.Tasks;

namespace Relay
{
    public interface INostrMessageHandler
    {
        public Task Handle(string connectionId, string msg);
    }
}