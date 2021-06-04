using System.Threading.Tasks;

namespace NNostr.Relay
{
    public interface INostrMessageHandler
    {
        public Task Handle(string msg);
    }
}