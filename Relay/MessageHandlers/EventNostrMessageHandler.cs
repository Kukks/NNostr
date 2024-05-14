using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class EventNostrMessageHandler : INostrMessageHandler, IHostedService
    {
        private readonly NostrEventService _nostrEventService;
        private readonly ILogger<EventNostrMessageHandler> _logger;
        private readonly StateManager _stateManager;
        private readonly IOptionsMonitor<RelayOptions> _options;
        private const string PREFIX = "EVENT";
        private CancellationTokenSource? _cts;
        private readonly Channel<(string, string)> PendingMessages = Channel.CreateUnbounded<(string, string)>();

        public EventNostrMessageHandler(NostrEventService nostrEventService,
            ILogger<EventNostrMessageHandler> logger,
            StateManager stateManager,
            IOptionsMonitor<RelayOptions> options)
        {
            _nostrEventService = nostrEventService;
            _logger = logger;
            _stateManager = stateManager;
            _options = options;
            
        }

        private async Task ProcessEventMessages(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            while (await PendingMessages.Reader.WaitToReadAsync(_cts.Token))
            {
                if (PendingMessages.Reader.TryRead(out var evt))
                {
                    try
                    {
                        var json = JsonDocument.Parse(evt.Item2).RootElement;
                        var e = JsonSerializer.Deserialize<RelayNostrEvent>(json[1].GetRawText());
                        if (_options.CurrentValue.Nip13Difficulty > 0)
                        {
                            var count = e.CountPowDifficulty<RelayNostrEvent, RelayNostrEventTag>(_options.CurrentValue.Nip13Difficulty);

                            if (count < _options.CurrentValue.Nip13Difficulty)
                            {
                                WriteOkMessage(evt.Item1, e.Id, false, $"pow: difficulty {count} is less than {_options.CurrentValue.Nip13Difficulty}");
                            }
                        }else if (e.Verify<RelayNostrEvent, RelayNostrEventTag>())
                        {
                            var tuple = await _nostrEventService.AddEvent(e);
                            
                            WriteOkMessage(evt.Item1, tuple.eventId, tuple.success, tuple.reason);
                            
                        }
                        else 
                        {
                            WriteOkMessage(evt.Item1, e.Id, false, "invalid: event could not be verified");
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "failed to handle event message");
                    }
                }
            }
        }

        private void WriteOkMessage(string connection, string eventId, bool success, string reason)
        {
            _stateManager.PendingMessages.Writer.TryWrite((connection, JsonSerializer.Serialize(new object[]
            {
                "OK",
                eventId,
                success,
                reason
            })));
        }

        public async Task HandleCore(string connectionId, string msg)
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
            _cts?.Cancel();
            return Task.CompletedTask;
        }
    }
}