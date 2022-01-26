using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class EventNostrMessageHandler : INostrMessageHandler, IHostedService
    {
        private readonly NostrEventService _nostrEventService;
        private readonly ILogger<EventNostrMessageHandler> _logger;
        private const string PREFIX = "EVENT";

        private readonly Channel<(string, string)> PendingMessages = Channel.CreateUnbounded<(string, string)>();
        public EventNostrMessageHandler(NostrEventService nostrEventService, ILogger<EventNostrMessageHandler> logger)
        {
            _nostrEventService = nostrEventService;
            _logger = logger;
        }

        private async Task ProcessEventMessages(CancellationToken cancellationToken)
        {
            
            while (await PendingMessages.Reader.WaitToReadAsync(cancellationToken))
            {
                if (PendingMessages.Reader.TryRead(out var evt))
                {
                    _logger.LogInformation($"Handling Event message for connection: {evt.Item1} \n{evt.Item2}");
                    var json = JsonDocument.Parse(evt.Item2).RootElement;
                    var e = JsonSerializer.Deserialize<NostrEvent>(json[1].GetRawText());
                    if (e.Verify())
                    {
                        await _nostrEventService.AddEvent(e);
                    }
                }
            }
            
            
           
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }
            
            await PendingMessages.Writer.WriteAsync((connectionId, msg));

        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = ProcessEventMessages(cancellationToken);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}