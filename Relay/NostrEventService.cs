using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Relay.Data;

namespace Relay
{
    public class NostrEventService
    {
        private ConcurrentDictionary<string, NostrEvent[]> CachedFilterResults =
            new();

        private readonly IDbContextFactory<RelayDbContext> _dbContextFactory;
        public event EventHandler<NostrEventsMatched> EventsMatched;

        private ConcurrentDictionary<string, NostrSubscriptionFilter> ActiveFilters { get; set; } =
            new();

        public NostrEventService(IDbContextFactory<RelayDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task AddEvent(params NostrEvent[] evt)
        {
            var evtIds = evt.Select(e => e.Id).ToArray();
            await using var context = _dbContextFactory.CreateDbContext();
            var alreadyPresentEventIds =
                await context.Events.Where(e => evtIds.Contains(e.Id)).Select(e => e.Id).ToArrayAsync();
            evt = evt.Where(e => !alreadyPresentEventIds.Contains(e.Id)).ToArray();
            foreach (var nostrSubscriptionFilter in ActiveFilters)
            {
                var matched = evt.AsQueryable().Filter(nostrSubscriptionFilter.Value);
                if (!await matched.AnyAsync()) continue;

                var matchedList = await matched.ToArrayAsync();
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

            await context.Events.AddRangeAsync(evt);
            await context.SaveChangesAsync();
        }

        public async Task<(string filterId, NostrEvent[] matchedEvents)> AddFilter(NostrSubscriptionFilter filter)
        {
            var id = JsonSerializer.Serialize(filter).ComputeSha256Hash().ToHex();
            ActiveFilters.TryAdd(id, filter);
            return (id, await CachedFilterResults.GetOrAddAsync(id, GetFromDB));
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
            await using var context = _dbContextFactory.CreateDbContext();
            return await context.Events.Include(e => e.Tags).Filter(filter).ToArrayAsync();
        }

        public void RemoveFilter(string removedFilter)
        {
            if (ActiveFilters.Remove(removedFilter, out _))
            {
                CachedFilterResults.Remove(removedFilter, out _);
            }
        }
    }
}