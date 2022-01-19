using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using NBitcoin.Secp256k1;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public static class NostrExtensions
    {

        public static IQueryable<NostrEvent> Filter(this IQueryable<NostrEvent> events, bool includeDeleted = false, params NostrSubscriptionFilter[] filters)
        {
            IQueryable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!includeDeleted)
                {
                    filterQuery = filterQuery.Where(e => !e.Deleted);
                }
                if (filter.Ids?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>  filter.Ids.Contains(e.Id));
                }

                if (filter.Kinds?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>  filter.Kinds.Contains(e.Kind));
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
                    filterQuery = filterQuery.Where(e => authors.Contains(e.PublicKey));
                }

                if (filter.EventId?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[1])));
                }

                if (filter.PublicKey?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[1])));
                }

                result = result is null ? filterQuery : result.Union(filterQuery);

            }
            
            return result;
        }
        public static IEnumerable<NostrEvent> Filter(this IEnumerable<NostrEvent> events, bool includeDeleted = false, params NostrSubscriptionFilter[] filters)
        {
            IEnumerable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!includeDeleted)
                {
                    filterQuery = filterQuery.Where(e => !e.Deleted);
                }
                if (filter.Ids?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>  filter.Ids.Contains(e.Id));
                }

                if (filter.Kinds?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>  filter.Kinds.Contains(e.Kind));
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
                    filterQuery = filterQuery.Where(e => authors.Contains(e.PublicKey));
                }

                if (filter.EventId?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[1])));
                }

                if (filter.PublicKey?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[1])));
                }

                result = result is null ? filterQuery : result.Union(filterQuery);

            }
            
            return result;
        }
    }
}