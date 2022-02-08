using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class NostrEventService
    {
        private ConcurrentDictionary<string, NostrEvent[]> CachedFilterResults =
            new();

        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        private readonly ILogger<NostrEventService> _logger;
        private readonly IOptions<RelayOptions> _options;
        public event EventHandler<NostrEventsMatched>? EventsMatched;
        public event EventHandler<NostrEvent[]>? NewEvents;

        private ConcurrentDictionary<string, NostrSubscriptionFilter> ActiveFilters { get; set; } =
            new();

        public NostrEventService(IDbContextFactory<RelayDbContext> dbContextFactory, ILogger<NostrEventService> logger, IOptions<RelayOptions> options)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _options = options;
        }

        private long ComputeCost(NostrEvent evt, out bool isToAdmin)
        {
            var adminPubKey = _options.Value.AdminPublicKey;
            isToAdmin = false;
            if (evt.PublicKey == adminPubKey)
            {
                isToAdmin = true;
                return 0;
            }
            if(evt.Kind == 4 && evt.Tags.Any(tag => tag.TagIdentifier == "p" && tag.Data.First().Equals(adminPubKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                isToAdmin = true;
                return 0;
            }

            if (!_options.Value.EventCostPerByte)
            {
                return _options.Value.EventCost;
            }
            
            return _options.Value.EventCost * Encoding.UTF8.GetByteCount(evt.ToJson(false));
        }
        
        public async Task<string[]> AddEvent(params NostrEvent[] evt)
        {
            var evtIds = evt.Select(e => e.Id).ToArray();
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var alreadyPresentEventIds =
                await context.Events.Where(e => evtIds.Contains(e.Id)).Select(e => e.Id).ToArrayAsync();
            evt = evt.Where(e => !alreadyPresentEventIds.Contains(e.Id)).ToArray();

            if (_options.Value.EventCost > 0 || _options.Value.PubKeyCost > 0)
            {
                var eventsGroupedByAuthor = evt.GroupBy(e => e.PublicKey);
                var eventsGroupedByAuthorItems = eventsGroupedByAuthor as IGrouping<string, NostrEvent>[] ?? eventsGroupedByAuthor.ToArray();
                var authors = eventsGroupedByAuthorItems.Select(events => events.Key).ToHashSet();
                var balanceLookup = (await context.Balances.Where(balance => authors.Contains(balance.PublicKey)).ToListAsync()).ToDictionary(balance => balance.PublicKey);
                
                var notvalid = new List<NostrEvent>();
                foreach (var eventsGroupedByAuthorItem in eventsGroupedByAuthorItems)
                {
                    balanceLookup.TryGetValue(eventsGroupedByAuthorItem.Key, out var authorBalance);
                    authorBalance ??= new Balance()
                    {
                        CurrentBalance = _options.Value.PubKeyCost * -1,
                    };
                    // if (authorBalance.CurrentBalance < 0 ||
                    //     (authorBalance.CurrentBalance == 0 && _options.Value.EventCost > 0))
                    // {
                    //     notvalid.AddRange(eventsGroupedByAuthorItem);
                    // }
                    foreach (var eventsGroupedByAuthorItemEvt in eventsGroupedByAuthorItem)
                    {
                        var cost = ComputeCost(eventsGroupedByAuthorItemEvt, out var isToAdmin);
                        if (!isToAdmin && (authorBalance.CurrentBalance - cost) < 0)
                        {
                            notvalid.Add(eventsGroupedByAuthorItemEvt);
                        }
                        else if (cost != 0)
                        {
                            authorBalance.CurrentBalance -= _options.Value.EventCost;
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

                evt = evt.Where(e => !notvalid.Contains(e)).ToArray();
            }
            
            _logger.LogInformation($"Saving {evt.Length} new events");
            foreach (var nostrSubscriptionFilter in ActiveFilters)
            {
                var matched = evt.Filter(false, nostrSubscriptionFilter.Value).ToArray();
                if (!matched.Any()) continue;

                var matchedList = matched.ToArray();
                _logger.LogInformation($"Updated filter {nostrSubscriptionFilter.Key} with {matchedList.Length} new events");
                if (CachedFilterResults.TryGetValue(nostrSubscriptionFilter.Key, out var currentFilterValues))
                {
                    var updatedResult = currentFilterValues.Concat(matchedList).ToArray();
                    CachedFilterResults[nostrSubscriptionFilter.Key] = updatedResult;
                }
                else
                {
                    CachedFilterResults.TryAdd(nostrSubscriptionFilter.Key, matchedList);
                }

                EventsMatched?.Invoke(this, new NostrEventsMatched()
                {
                    Events = matchedList,
                    FilterId = nostrSubscriptionFilter.Key
                });
            }

            if (_options.Value.EnableNip09)
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
                                evt2.PublicKey.Equals(eventsToDeleteByPubKeyItem.Key,
                                    StringComparison.InvariantCultureIgnoreCase) &&
                                !evt2.Deleted && eventsToDeleteByPubKeyItem.Value.Contains(evt2.Id))
                            .ForEachAsync(evt2 => evt2.Deleted = true);
                    }
                }
            }

            await context.EventTags.AddRangeAsync(evt.SelectMany(e => e.Tags));
            await context.Events.AddRangeAsync(evt);
            await context.SaveChangesAsync();
            NewEvents?.Invoke(this, evt);
            return evt.Select(e => e.Id).ToArray();
        }

        public async Task<(string filterId, NostrEvent[] matchedEvents)> AddFilter(NostrSubscriptionFilter filter)
        {
            var id = JsonSerializer.Serialize(filter).ComputeSha256Hash().ToHex();
            ActiveFilters.TryAdd(id, filter);
            return (id, await CachedFilterResults.GetOrAddAsync(id, GetFromDB));
        }

        public async Task<NostrEvent[]> FetchData(params NostrSubscriptionFilter[] filter)
        {
            var result = new List<NostrEvent>();
            foreach (var nostrSubscriptionFilter in filter)
            {
                var id = JsonSerializer.Serialize(nostrSubscriptionFilter).ComputeSha256Hash().ToHex();
                result.AddRange(await CachedFilterResults.GetOrAddAsync(id, s =>  GetFromDB(nostrSubscriptionFilter)));
            }
            return result.Distinct().ToArray();
        }

        private async Task<NostrEvent[]> GetFromDB(string filterId)
        {
            if (ActiveFilters.TryGetValue(filterId, out var filter))
            {
                return await GetFromDB(filter);
            }

            throw new ArgumentOutOfRangeException(nameof(filterId), "Filter is not active");
        }

        private async Task<NostrEvent[]> GetFromDB(NostrSubscriptionFilter filter)
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            return await context.Events.Include(e => e.Tags).Filter(false, filter).ToArrayAsync();
        }

        public void RemoveFilter(string removedFilter)
        {
            if (!ActiveFilters.Remove(removedFilter, out _)) return;
            _logger.LogInformation($"Removing filter: {removedFilter}");
            CachedFilterResults.Remove(removedFilter, out _);
        }
    }
}