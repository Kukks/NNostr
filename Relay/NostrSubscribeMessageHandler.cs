using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Relay.Data;

namespace Relay
{
    public class CloseNostrMessageHandler : INostrMessageHandler
    {
        private readonly StateManager _stateManager;
        private readonly NostrEventService _nostrEventService;
        private const string PREFIX = "CLOSE ";

        public CloseNostrMessageHandler(StateManager stateManager, NostrEventService nostrEventService)
        {
            _stateManager = stateManager;
            _nostrEventService = nostrEventService;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var id = $"{connectionId}_{msg.Substring(PREFIX.Length).Trim()}";
            _stateManager.RemoveSubscription(connectionId, id, out var orphanedFilters);
            foreach (var orphanedFilter in orphanedFilters)
            {
                _nostrEventService.RemoveFilter(orphanedFilter);
            }
        }
    }

    public class EventNostrMessageHandler : INostrMessageHandler
    {
        private readonly NostrEventService _nostrEventService;
        private const string PREFIX = "EVENT ";

        public EventNostrMessageHandler(NostrEventService nostrEventService)
        {
            _nostrEventService = nostrEventService;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var e = JsonSerializer.Deserialize<NostrEvent>(msg.Substring(PREFIX.Length).Trim());
            if (e.Verify())
            {
                await _nostrEventService.AddEvent(e);
            }
        }
    }

    public class NostrSubscribeMessageHandler : INostrMessageHandler
    {
        private readonly NostrEventService _nostrEventService;
        private readonly StateManager _stateManager;


        private const string PREFIX = "REQ ";

        public NostrSubscribeMessageHandler(NostrEventService nostrEventService,
            StateManager stateManager)
        {
            _nostrEventService = nostrEventService;
            _stateManager = stateManager;
        }

        public async Task Handle(string connectionId, string msg)
        {
            if (!msg.StartsWith(PREFIX))
            {
                return;
            }

            var body = msg.Substring(PREFIX.Length);
            var splitIndex = body.IndexOf(" ", StringComparison.InvariantCulture);
            var id = $"{connectionId}_{body.Substring(0, splitIndex)}";
            var json = body.Substring(splitIndex + 1);

            var parsed = JsonSerializer.Deserialize<NostrSubscriptionFilter[]>(json);
            var results = new List<NostrEvent[]>();
            _stateManager.SubscriptionToFilter.TryGetValue(id, out var existingFilters);
            _stateManager.SubscriptionToFilter.Remove(id);

            var newids = new List<string>();
            foreach (var filter in parsed)
            {
                var x = await _nostrEventService.AddFilter(filter);
                results.Add(x.matchedEvents);
                newids.Add(x.filterId);
                if(!_stateManager.ConnectionToFilter.Contains(connectionId, x.filterId))
                    _stateManager.ConnectionToFilter.Add(connectionId, x.filterId);
                if(!_stateManager.FilterToConnection.Contains(x.filterId, connectionId))
                    _stateManager.FilterToConnection.Add(x.filterId, connectionId);
                _stateManager.SubscriptionToFilter.Add(id, x.filterId);
            }

            _stateManager.ConnectionToSubscriptions.Add(connectionId, id);


            var removedFilters = existingFilters.Except(newids);
            foreach (var removedFilter in removedFilters)
            {
                _stateManager.FilterToConnection.Remove(removedFilter);
                if (!_stateManager.ConnectionToFilter.ContainsValue(removedFilter))
                {
                    _nostrEventService.RemoveFilter(removedFilter);
                }
            }

            await _stateManager.PendingMessages.Writer.WriteAsync(new Tuple<string, string>(connectionId,
                JsonSerializer.Serialize(results.Aggregate(
                    (events, nostrEvents) =>
                        events.Union(nostrEvents).ToArray()))));
        }
    }
}