using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class RequestNostrMessageHandler : INostrMessageHandler
    {
        private readonly IOptionsMonitor<RelayOptions> _options;
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;
        private readonly ILogger<RequestNostrMessageHandler> _logger;


        private const string PREFIX = "REQ";

        public RequestNostrMessageHandler(
            IOptionsMonitor<RelayOptions> options,
            NostrEventService nostrEventService,
            StateManager stateManager, ILogger<RequestNostrMessageHandler> logger)
        {
            _options = options;
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
            _logger = logger;
        }

        public async Task HandleCore(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }
            var json = JsonDocument.Parse(msg).RootElement;

            var id = json[1].GetString();
            var filters = new List<NostrSubscriptionFilter>();
            for (var i = 2; i < json.GetArrayLength(); i++)
            {
                filters.Add(JsonSerializer.Deserialize<NostrSubscriptionFilter>(json[i].GetRawText()));
            }

            _stateManager.AddSubscription(connectionId, id, filters.ToArray());
            var matchedEvents = await _nostrEventService.GetFromDB(filters.ToArray());
            if (matchedEvents.Length > 0)
            {
                var matched = new NostrEventsMatched()
                {
                    Events = matchedEvents,
                    ConnectionId = connectionId,
                    SubscriptionId = id
                };
                _nostrEventService.InvokeMatched(matched);
                
                if (_options.CurrentValue.EnableNip15)
                {
                    matched.OnSent.Task.ContinueWith(task =>
                    {
                        return _stateManager.PendingMessages.Writer.WaitToWriteAsync().AsTask().ContinueWith(_ =>
                            SendEOSE(connectionId, id));
                    });
                }
            }
            else if (_options.CurrentValue.EnableNip15)
            {
                await SendEOSE(connectionId, id);
            }
        }

        private async Task SendEOSE(string connectionId, string subscriptionId)
        {
            await _stateManager.PendingMessages.Writer.WriteAsync((connectionId,
                JsonSerializer.Serialize(new[]
                {
                    "EOSE",
                    subscriptionId
                })));
        }
    }
}