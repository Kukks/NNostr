using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using NBitcoin.Secp256k1;
using Relay.Data;

namespace Relay
{
    public static class NostrExtensions
    {
        public static string ToJson(this NostrEvent nostrEvent)
        {
            return
                $"[0,\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag.ToString()))}],\"{nostrEvent.Content}\"]";
        }
        public static string ComputeId(this NostrEvent nostrEvent)
        {
            return nostrEvent.ToJson().ComputeSha256Hash().ToHex();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv)
        {
            return nostrEvent.ToJson().ComputeSignature(priv);
        }

        public static bool Verify(this NostrEvent nostrEvent)
        {
            var hash = nostrEvent.ToJson().ComputeSha256Hash();
            if (hash.ToHex() != nostrEvent.Id)
            {
                return false;
            }
            ECPubKey pub = nostrEvent.GetPublicKey();
            if (!SecpSchnorrSignature.TryCreate(nostrEvent.Signature.DecodHexData(), out var sig))
            {
                return false;
            }

            return pub.SigVerifySchnorr(sig, hash);
        }

        public static ECXOnlyPubKey GetPublicKey(this NostrEvent nostrEvent)
        {
           return Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(nostrEvent.PublicKey));
        }
        
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

                if (!string.IsNullOrEmpty(filter.Author))
                {
                    filterQuery = filterQuery.Where(e => e.PublicKey == filter.Author);
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