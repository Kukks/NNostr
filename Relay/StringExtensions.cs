using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using NBitcoin.Secp256k1;
using NNostr.Client;

namespace Relay
{
    public static class StringExtensions
    {
        // from https://stackoverflow.com/a/65265024/275504
        /// <summary>
        /// Adds a key/value pair to the <see cref="ConcurrentDictionary{TKey,TValue}"/> by using the specified function 
        /// if the key does not already exist. Returns the new value, or the existing value if the key exists.
        /// </summary>
        public static async Task<TResult> GetOrAddAsync<TKey, TResult>(
            this ConcurrentDictionary<TKey, TResult> dict,
            TKey key, Func<TKey, Task<TResult>> asyncValueFactory)
        {
            if (dict.TryGetValue(key, out TResult resultingValue))
            {
                return resultingValue;
            }

            var newValue = await asyncValueFactory(key);
            return dict.GetOrAdd(key, newValue);
        }
    }

    public static class Extensions
    {
        public static IQueryable<TNostrEvent> Filter<TNostrEvent, TEventTag>(this IQueryable<TNostrEvent> events,
            NostrSubscriptionFilter filter) where TNostrEvent : BaseNostrEvent<TEventTag>
            where TEventTag : NostrEventTag
        {
            var filterQuery = events;

            if (filter.Ids?.Any() is true)
            {
                filterQuery = filterQuery.Where(filter.Ids.Aggregate(PredicateBuilder.New<TNostrEvent>(),
                    (current, temp) => current.Or(p => p.Id.StartsWith(temp))));
            }

            if (filter.Kinds?.Any() is true)
            {
                filterQuery = filterQuery.Where(e => filter.Kinds.Contains(e.Kind));
            }

            if (filter.Since != null)
            {
                filterQuery = filterQuery.Where(e => e.CreatedAt > filter.Since);
            }

            if (filter.Until != null)
            {
                filterQuery = filterQuery.Where(e => e.CreatedAt < filter.Until);
            }

            var authors = filter.Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
            if (authors?.Any() is true)
            {
                authors = authors.Select(s => s + "%").ToArray();
                var filterQuery2 = filterQuery.Where(x => authors.Any(y => EF.Functions.Like(x.PublicKey, y)));
                filterQuery = filterQuery2;
            }

            if (filter.ReferencedEventIds?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.ReferencedEventIds.Contains(tag.Data[0])));
            }

            if (filter.ReferencedPublicKeys?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.ReferencedPublicKeys.Contains(tag.Data[0])));
            }

            var tagFilters = filter.GetAdditionalTagFilters().Where(pair => pair.Value.Any());


            foreach (var tagFilter in tagFilters)
            {
                filterQuery = filterQuery
                    .Where(e => e.Tags.Any(tag =>
                        tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Contains(tag.Data[0])));
            }

            if (filter.Limit is not null)
            {
                filterQuery = filterQuery.OrderByDescending(e => e.CreatedAt).Take(filter.Limit.Value);
            }

            return filterQuery;
        }
    }
}