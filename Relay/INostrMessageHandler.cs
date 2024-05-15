using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Relay
{
    public interface INostrMessageHandler
    {
        ILogger Logger { get; }
        public Task Handle(string connectionId, string msg) {
            try {
                return HandleCore(connectionId, msg);
            } catch(Exception e){
                Logger.LogError($"Error handling message {msg}", e);
                return Task.CompletedTask;
            }
        }
        public Task HandleCore(string connectionId, string msg);
    }
}