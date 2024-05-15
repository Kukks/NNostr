using System.Data.Entity;
using System.Linq;
using LinqKit;
using Microsoft.EntityFrameworkCore;
using NNostr.Client;

namespace Relay;

public static class Extensions
{
    public static IQueryable<TNostrEvent> Filter<TNostrEvent, TEventTag>(this IQueryable<TNostrEvent> events,
        NostrSubscriptionFilter[] filters) where TNostrEvent : BaseNostrEvent<TEventTag>
        where TEventTag : NostrEventTag
    {
        var results = filters.Select(events.Filter<TNostrEvent, TEventTag>);
        // union the results
        return results.Aggregate((a, b) => a.Union(b));
    }

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

        return filterQuery.AsStreaming();
    }
}