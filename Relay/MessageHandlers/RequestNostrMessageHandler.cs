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

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith($"[\"{PREFIX}"))
            {
                return;
            }

            _logger.LogTrace($"Handling REQ message for connection: {connectionId} \n{msg}");
            var json = JsonDocument.Parse(msg).RootElement;

            var id = json[1].GetString();
            var filters = new List<NostrSubscriptionFilter>();
            for (int i = 2; i < json.GetArrayLength(); i++)
            {
                filters.Add(JsonSerializer.Deserialize<NostrSubscriptionFilter>(json[i].GetRawText()));
            }

            var results = new List<NostrEventsMatched>();
            _stateManager.SubscriptionToFilter.TryGetValues(id, out var existingFilters);
            _stateManager.SubscriptionToFilter.Remove(id);

            var newids = new List<string>();
            foreach (var filter in filters)
            {
                var x = await _nostrEventService.AddFilter(filter);
                results.Add(x);
                newids.Add(x.FilterId);
                if (!_stateManager.ConnectionToFilter.Contains(connectionId, x.FilterId))
                    _stateManager.ConnectionToFilter.Add(connectionId, x.FilterId);
                if (!_stateManager.FilterToConnection.Contains(x.FilterId, connectionId))
                    _stateManager.FilterToConnection.Add(x.FilterId, connectionId);
                _stateManager.SubscriptionToFilter.Add(id, x.FilterId);
            }

            _stateManager.ConnectionToSubscriptions.Add(connectionId, id);

            var removedFilters = existingFilters?.Except(newids);
            if (removedFilters is not null)
                foreach (var removedFilter in removedFilters)
                {
                    _stateManager.FilterToConnection.Remove(removedFilter);
                    if (!_stateManager.ConnectionToFilter.ContainsValue(removedFilter))
                    {
                        _nostrEventService.RemoveFilter(removedFilter);
                    }
                }

            TaskCompletionSource tcsForEose = new();
            List<string> waitingForFilters = new List<string>(results.Select(matched => matched.FilterId));
            results.ForEach(matched =>
            {
                if (_options.CurrentValue.EnableNip15)
                {
                    matched.OnEventsSent = tuple =>
                    {
                        if (waitingForFilters.Remove(tuple.Item2.FilterId) && waitingForFilters.Count == 0)
                        {
                            tcsForEose.SetResult();
                        }
                    };
                }

                _nostrEventService.InvokeMatched(matched);
            });
            if (_options.CurrentValue.EnableNip15)
            {
                var task = tcsForEose.Task.WaitAsync(TimeSpan.FromMinutes(5)).ContinueWith(_ => _stateManager.PendingMessages.Writer.WaitToWriteAsync().AsTask()).ContinueWith(_ => 
                     _stateManager.PendingMessages.Writer.WriteAsync((connectionId,
                        JsonSerializer.Serialize(new[]
                        {
                            "EOSE",
                            id
                        }))));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await task;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e,
                            $"Did not get confirmation of event stream ending to send EOSE for subscription {id} of connection {connectionId}");
                    }
                });
            }
        }
    }
}