using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class NostrEventService
    {
        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        private readonly ILogger<NostrEventService> _logger;
        private readonly IOptionsMonitor<RelayOptions> _options;
        private readonly StateManager _stateManager;
        public event EventHandler<NostrEventsMatched>? EventsMatched;
        public event EventHandler<RelayNostrEvent[]>? NewEvents;

        public NostrEventService(IDbContextFactory<RelayDbContext> dbContextFactory, ILogger<NostrEventService> logger,
            IOptionsMonitor<RelayOptions> options, StateManager stateManager)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _options = options;
            _stateManager = stateManager;
        }

        private long ComputeCost(RelayNostrEvent evt, out bool isToAdmin)
        {
            var adminPubKey = _options.CurrentValue.AdminPublicKey;
            isToAdmin = false;
            if (evt.PublicKey == adminPubKey)
            {
                isToAdmin = true;
                return 0;
            }

            if (evt.Kind == 4 && evt.Tags.Any(tag =>
                    tag.TagIdentifier == "p" &&
                    tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                isToAdmin = true;
                return 0;
            }

            if (!_options.CurrentValue.EventCostPerByte)
            {
                return _options.CurrentValue.EventCost;
            }

            return _options.CurrentValue.EventCost * Encoding.UTF8.GetByteCount(evt.ToJson<RelayNostrEvent,RelayNostrEventTag>(false));
        }

        public async Task<List<(string eventId, bool success, string reason)>> AddEvent(RelayNostrEvent[] evt)
        {
            var eventResults = new List<(string eventId, bool success, string reason)>();
            var evtIds = evt.Select(e => e.Id).ToArray();
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var alreadyPresentEventIds =
                await context.Events.Where(e => evtIds.Contains(e.Id)).Select(e => e.Id).ToArrayAsync();
            evt = evt.Where(e => !alreadyPresentEventIds.Contains(e.Id)).ToArray();
            eventResults.AddRange(alreadyPresentEventIds.Select(s => (s, true, "duplicate: Event has been processed before")));
            var invalidnip22 = evt.Where(e =>
                !((_options.CurrentValue.Nip22BackwardLimit is null ||
                  (DateTimeOffset.UtcNow - e.CreatedAt) <= _options.CurrentValue.Nip22BackwardLimit) &&
                 (_options.CurrentValue.Nip22ForwardLimit is null ||
                  (e.CreatedAt - DateTimeOffset.UtcNow) <= _options.CurrentValue.Nip22ForwardLimit)));
            
            eventResults.AddRange(invalidnip22.Select(s => (s.Id, false, "invalid: event creation date is too far off from the current time. Is your system clock in sync?")));
            evt = evt.Except(invalidnip22).ToArray();

            if (_options.CurrentValue.EventCost > 0 || _options.CurrentValue.PubKeyCost > 0)
            {
                var eventsGroupedByAuthor = evt.GroupBy(e => e.PublicKey);
                var eventsGroupedByAuthorItems = eventsGroupedByAuthor as IGrouping<string, RelayNostrEvent>[] ??
                                                 eventsGroupedByAuthor.ToArray();
                var authors = eventsGroupedByAuthorItems.Select(events => events.Key).ToHashSet();
                var balanceLookup =
                    (await context.Balances.Where(balance => authors.Contains(balance.PublicKey)).ToListAsync())
                    .ToDictionary(balance => balance.PublicKey);

                var notvalid = new List<RelayNostrEvent>();
                foreach (var eventsGroupedByAuthorItem in eventsGroupedByAuthorItems)
                {
                    balanceLookup.TryGetValue(eventsGroupedByAuthorItem.Key, out var authorBalance);
                    authorBalance ??= new Balance()
                    {
                        CurrentBalance = _options.CurrentValue.PubKeyCost * -1,
                    };
                    foreach (var eventsGroupedByAuthorItemEvt in eventsGroupedByAuthorItem)
                    {
                        var cost = ComputeCost(eventsGroupedByAuthorItemEvt, out var isToAdmin);
                        if (!isToAdmin && (authorBalance.CurrentBalance - cost) < 0)
                        {
                            notvalid.Add(eventsGroupedByAuthorItemEvt);
                        }
                        else if (cost != 0)
                        {
                            authorBalance.CurrentBalance -= _options.CurrentValue.EventCost;
                            await context.BalanceTransactions.AddAsync(new BalanceTransaction()
                            {
                                BalanceId = eventsGroupedByAuthorItem.Key,
                                Timestamp = eventsGroupedByAuthorItemEvt.CreatedAt ?? DateTimeOffset.UtcNow,
                                Value = cost * -1,
                                EventId = eventsGroupedByAuthorItemEvt.Id
                            });
                        }
                    }
                }
                eventResults.AddRange(notvalid.Select(s => (s.Id, false, "invalid: this relay has a cost associated with this event and you did not have sufficient balance")));

                evt = evt.Where(e => !notvalid.Contains(e)).ToArray();
            }


            var removedEvents = new List<string>();
            if (_options.CurrentValue.EnableNip09)
            {
                var deletionEvents = evt.Where(e => e.Kind == 5).ToArray();
                if (deletionEvents.Any())
                {
                    var eventsToDeleteByPubKey = deletionEvents.Select(evt2 => (evt2.PublicKey, evt2.Tags.FindAll(tag =>
                                tag.TagIdentifier.Equals("e", StringComparison.InvariantCultureIgnoreCase))
                            .Select(tag => tag.Data.First())))
                        .GroupBy(tuple => tuple.PublicKey)
                        .ToDictionary(tuples => tuples.Key, tuples => tuples.SelectMany(tuple => tuple.Item2));
                    foreach (var eventsToDeleteByPubKeyItem in eventsToDeleteByPubKey)
                    {
                        await context.Events.Where(evt2 =>
                                evt2.PublicKey.Equals(eventsToDeleteByPubKeyItem.Key) &&
                                !evt2.Deleted && eventsToDeleteByPubKeyItem.Value.Contains(evt2.Id))
                            .ForEachAsync(evt2 =>
                            {
                                // clients still receive a copy of the original note so we shouldnt remove from filter results
                                // removedEvents.Add(evt2);  
                                evt2.Deleted = true;
                            });
                    }
                }
            }

            var evtsToSave = evt;
            if (_options.CurrentValue.EnableNip16)
            {
                var replaceableEvents = evt.Where(e => e.Kind is >= 10000 and < 20000).ToArray();
                var replacedEvents = new List<RelayNostrEvent>();
                foreach (var eventsToReplace in replaceableEvents)
                {
                    
                    replacedEvents.AddRange(context.Events.Where(evt2 =>
                        evt2.PublicKey.Equals(eventsToReplace.PublicKey) && eventsToReplace.Kind == evt2.Kind &&
                        evt2.CreatedAt < eventsToReplace.CreatedAt));
                }

                context.Events.RemoveRange(replacedEvents);
                removedEvents.AddRange(replacedEvents.Select(e => e.Id));
                //ephemeral events
                evtsToSave = evt.Where(e => e.Kind is not (>= 20000 and < 30000)).ToArray();
            }
            if (_options.CurrentValue.EnableNip33)
            {
                var replaceableEvents = evt.Where(e => e.Kind is >= 30000 and < 40000).ToArray();
                var replacedEvents = new List<RelayNostrEvent>();
                foreach (var eventsToReplace in replaceableEvents)
                {
                    var dValue = eventsToReplace.GetTaggedData<RelayNostrEvent, RelayNostrEventTag>("d").FirstOrDefault() ?? "";

                    var caluse = PredicateBuilder.New<RelayNostrEvent>()
                        .And(@event => @event.PublicKey == eventsToReplace.PublicKey)
                        .And(@event => @event.Kind == eventsToReplace.Kind)
                        .And(@event => @event.CreatedAt < eventsToReplace.CreatedAt);

                        
                    var toreplace =  await context.Events.Where(caluse).ToListAsync();
                    toreplace = toreplace.Where(@event => dValue == (
                        @event.GetTaggedData<RelayNostrEvent, RelayNostrEventTag>("d").FirstOrDefault() ?? "")).ToList();
                    replacedEvents.AddRange(toreplace);
                }

                context.Events.RemoveRange(replacedEvents);
                removedEvents.AddRange(replacedEvents.Select(e => e.Id));
            }

            List<NostrEventsMatched> eventsMatcheds = new();
            _stateManager.ConnectionSubscriptionsToFilters.Keys.ForEach(pair =>
            {
                if (!_stateManager.ConnectionSubscriptionsToFilters.TryGetValues(pair, out var values)) return;
                foreach (var subscriptionFilter in values)
                {
                        
                    var matched = evt.Filter<RelayNostrEvent,RelayNostrEventTag>( subscriptionFilter).ToArray();
                    if (!matched.Any()) continue;
                    _logger.LogInformation(
                        $"Updated connection subscription {pair} with {matched.Length} new events");
                            
                    var connectionId = pair[..pair.IndexOf('-')];
                    var subscriptionId = pair[(pair.IndexOf('-')+1)..];
                    eventsMatcheds.Add(new NostrEventsMatched()
                    {
                        Events = matched,
                        ConnectionId = connectionId,
                        SubscriptionId = subscriptionId
                    });
                }
            });
                
               
            eventResults.AddRange(evtsToSave.Select(@event => (@event.Id, true, "")));
            await context.Events.AddRangeAsync(
                evtsToSave.Select(@event => 
                    JsonSerializer.Deserialize<RelayNostrEvent>( JsonSerializer.Serialize(@event)))!);
            
            
            _logger.LogInformation($"Processing/Saving {evt.Length} new events: {removedEvents.Count} removed, {evtsToSave.Length} saved");
            await context.SaveChangesAsync();
            NewEvents?.Invoke(this, evt);
            eventsMatcheds.ForEach(InvokeMatched);
            return eventResults;
        }

        public void InvokeMatched(NostrEventsMatched eventsMatched)
        {
            EventsMatched?.Invoke(this, eventsMatched);
        }

        public  async Task<RelayNostrEvent[]> GetFromDB(NostrSubscriptionFilter[] filter)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            return await context.Events
                .Include(e => e.Tags)
                .Where(e => !e.Deleted)
                .Filter<RelayNostrEvent, RelayNostrEventTag>(filter).ToArrayAsync();
        }
    }
}