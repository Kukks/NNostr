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

        public static IQueryable<NostrEvent> Filter(this IQueryable<NostrEvent> events, params NostrSubscriptionFilter[] filters)
        {
            IQueryable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!string.IsNullOrEmpty(filter.Id))
                {
                    filterQuery = filterQuery.Where(e => e.Id == filter.Id);
                }

                if (filter.Kind != null)
                {
                    filterQuery = filterQuery.Where(e => e.Kind == filter.Kind);
                }

                if (filter.Since != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt > filter.Since);
                }

                var authors = filter.Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
                if (authors?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => authors.Contains(e.PublicKey));
                }

                if (!string.IsNullOrEmpty(filter.EventId))
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && tag.Data[1] == filter.EventId));
                }

                if (!string.IsNullOrEmpty(filter.PublicKey))
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && tag.Data[1] == filter.PublicKey));
                }

                result = result is null ? filterQuery : result.Union(filterQuery);

            }
            
            return result;
        }
        public static IEnumerable<NostrEvent> Filter(this IEnumerable<NostrEvent> events, params NostrSubscriptionFilter[] filters)
        {
            IEnumerable<NostrEvent> result = null;
            foreach (var filter in filters)
            {
                var filterQuery = events;
                if (!string.IsNullOrEmpty(filter.Id))
                {
                    filterQuery = filterQuery.Where(e => e.Id == filter.Id);
                }

                if (filter.Kind != null)
                {
                    filterQuery = filterQuery.Where(e => e.Kind == filter.Kind);
                }

                if (filter.Since != null)
                {
                    filterQuery = filterQuery.Where(e => e.CreatedAt > filter.Since);
                }
                
                var authors = filter.Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
                if (authors?.Any() is true)
                {
                    filterQuery = filterQuery.Where(e => authors.Contains(e.PublicKey));
                }

                if (!string.IsNullOrEmpty(filter.EventId))
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "e" && tag.Data[1] == filter.EventId));
                }

                if (!string.IsNullOrEmpty(filter.PublicKey))
                {
                    filterQuery = filterQuery.Where(e =>
                        e.Tags.Any(tag => tag.TagIdentifier == "p" && tag.Data[1] == filter.PublicKey));
                }

                result = result is null ? filterQuery : result.Union(filterQuery);

            }
            
            return result;
        }
    }
}