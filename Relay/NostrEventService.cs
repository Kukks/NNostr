using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        public event EventHandler<NostrEventsMatched> EventsMatched;

        private ConcurrentDictionary<string, NostrSubscriptionFilter> ActiveFilters { get; set; } =
            new();

        public NostrEventService(IDbContextFactory<RelayDbContext> dbContextFactory, ILogger<NostrEventService> logger, IOptions<RelayOptions> options)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _options = options;
        }

        public async Task AddEvent(params NostrEvent[] evt)
        {
            var evtIds = evt.Select(e => e.Id).ToArray();
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var alreadyPresentEventIds =
                await context.Events.Where(e => evtIds.Contains(e.Id)).Select(e => e.Id).ToArrayAsync();
            evt = evt.Where(e => !alreadyPresentEventIds.Contains(e.Id)).ToArray();
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
                var deletionEvents = evt.Where(evt => evt.Kind == 5).ToArray();
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

            await context.Events.AddRangeAsync(evt);
            await context.SaveChangesAsync();
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