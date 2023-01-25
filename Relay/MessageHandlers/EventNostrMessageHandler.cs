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
        private readonly IOptions<RelayOptions> _options;
        private const string PREFIX = "EVENT";

        private readonly Channel<(string, string)> PendingMessages = Channel.CreateUnbounded<(string, string)>();

        public EventNostrMessageHandler(NostrEventService nostrEventService,
            ILogger<EventNostrMessageHandler> logger,
            StateManager stateManager,
            IOptions<RelayOptions> options)
        {
            _nostrEventService = nostrEventService;
            _logger = logger;
            _stateManager = stateManager;
            _options = options;
        }

        private async Task ProcessEventMessages(CancellationToken cancellationToken)
        {
            while (await PendingMessages.Reader.WaitToReadAsync(cancellationToken))
            {
                if (PendingMessages.Reader.TryRead(out var evt))
                {
                    try
                    {
                        _logger.LogInformation($"Handling Event message for connection: {evt.Item1} \n{evt.Item2}");
                        var json = JsonDocument.Parse(evt.Item2).RootElement;
                        var e = JsonSerializer.Deserialize<NostrEvent>(json[1].GetRawText());
                        if (_options.Value.Nip13Difficulty > 0)
                        {
                            var count = e.CountPowDifficulty(_options.Value.Nip13Difficulty);

                            if (count < _options.Value.Nip13Difficulty)
                            {
                                
                                WriteOkMessage(evt.Item1, e.Id, false, $"pow: difficulty {count} is less than {_options.Value.Nip13Difficulty}");
                            }
                        }else if (e.Verify())
                        {
                            var added = await _nostrEventService.AddEvent(new[] {e});
                            if (_options.Value.EnableNip20)
                            {
                                foreach (var tuple in added)
                                {
                                    WriteOkMessage(evt.Item1, tuple.eventId, tuple.success, tuple.reason);
                                }
                            }
                            else if (added.Any(tuple => !tuple.success))
                            {
                                foreach (var tuple in added)
                                {
                                    _stateManager.PendingMessages.Writer.TryWrite((evt.Item1,
                                        JsonSerializer.Serialize(new[]
                                        {
                                            "NOTICE",
                                            $"Event {tuple.eventId} was not added to this relay. This relay charges {_options.Value.PubKeyCost} for new pubkey registrations and {_options.Value.EventCost} per event {(_options.Value.EventCostPerByte ? "byte" : "")}. Send a message to {_options.Value.AdminPublicKey} for more info "
                                        })));
                                }
                            }
                        }
                        else if (_options.Value.EnableNip20)
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