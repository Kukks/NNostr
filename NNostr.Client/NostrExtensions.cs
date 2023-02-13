using LinqKit;

using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class NostrExtensions
    {
        public static string ToJson(this NostrEvent nostrEvent, bool withoutId)
        {
            return
                $"[{(withoutId ? 0 : $"\"{nostrEvent.Id}\"")},\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag))}],\"{nostrEvent.Content}\"]";
        }

        public static string ComputeEventId(this string eventJson)
        {
            return eventJson.ComputeSha256Hash().AsSpan().ToHex();
        }

        public static string ComputeId(this NostrEvent nostrEvent)
        {
            return nostrEvent.ToJson(true).ComputeEventId();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv)
        {
            return nostrEvent.ToJson(true).ComputeBIP340Signature(priv);
        }

        public static async ValueTask ComputeIdAndSignAsync(this NostrEvent nostrEvent, ECPrivKey priv, bool handlenip4 = true, int powDifficulty = 0)
        {
            if (handlenip4 && nostrEvent.Kind == 4)
            {
                await nostrEvent.EncryptNip04EventAsync(priv);
            }
            nostrEvent.Id = nostrEvent.ComputeId();
            nostrEvent.Signature = nostrEvent.ComputeSignature(priv);

            ulong counter = 0;
            while (nostrEvent.CountPowDifficulty(powDifficulty) < powDifficulty)
            {
                nostrEvent.SetTag("nonce", counter.ToString(), powDifficulty.ToString());
            }
        }

        public static void SetTag(this NostrEvent nostrEvent, string identifier, params string[] data)
        {
            nostrEvent.Tags.RemoveAll(tag => tag.TagIdentifier == identifier);
            nostrEvent.Tags.Add(new NostrEventTag()
            {
                TagIdentifier = identifier,
                Data = data.ToList(),
                EventId = nostrEvent.Id,
                Event = nostrEvent
            });
        }

        public static int CountPowDifficulty(this NostrEvent @event, int? powDifficulty = null)
        {
            var pow = 0;
            foreach (var c in @event.Id)
            {
                if (c == '0')
                {
                    pow++;
                }
                else
                {
                    break;
                }
            }

            if (powDifficulty is null)
            {
                return pow;
            }

            var diffAttempt = @event.GetTaggedData("nonce").ElementAtOrDefault(1);
            if (!string.IsNullOrEmpty(diffAttempt) && int.TryParse(diffAttempt, out var i))
            {
                return i < powDifficulty ? i : pow;
            }

            return 0;

        }

        public static bool Verify(this NostrEvent nostrEvent)
        {
            var hash = nostrEvent.ToJson(true).ComputeSha256Hash();
            if (hash.AsSpan().ToHex() != nostrEvent.Id)
            {
                return false;
            }

            var pub = nostrEvent.GetPublicKey();
            if (!SecpSchnorrSignature.TryCreate(Convert.FromHexString(nostrEvent.Signature), out var sig))
            {
                return false;
            }

            return pub.SigVerifyBIP340(sig, hash);
        }

        public static ECXOnlyPubKey GetPublicKey(this NostrEvent nostrEvent)
        {
            return ParsePubKey(nostrEvent.PublicKey);
        }

        public static ECPrivKey ParseKey(string key)
        {
            return ECPrivKey.Create(Convert.FromHexString(key));
        }

        public static ECXOnlyPubKey ParsePubKey(string key)
        {
            return Context.Instance.CreateXOnlyPubKey(Convert.FromHexString(key));
        }

        public static string ToHex(this ECPrivKey key)
        {
            Span<byte> output = stackalloc byte[32];
            key.WriteToSpan(output);
            return output.ToHex();
        }

        public static string ToHex(this ECXOnlyPubKey key)
        {
            return key.ToBytes().AsSpan().ToHex();
        }

        public static string[] GetTaggedEvents(this NostrEvent e)
        {
            return e.GetTaggedData("e");
        }

        public static string[] GetTaggedData(this NostrEvent e, string identifier)
        {
            return e.Tags.Where(tag => tag.TagIdentifier == identifier).Select(tag => tag.Data.First()).ToArray();
        }

        public static IQueryable<NostrEvent> Filter(this IQueryable<NostrEvent> events, NostrSubscriptionFilter filter)
        {
            var filterQuery = events;

            if (filter.Ids?.Any() is true)
            {
                filterQuery = filterQuery.Where(filter.Ids.Aggregate(PredicateBuilder.New<NostrEvent>(),
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
                filterQuery = filterQuery.Where(authors.Aggregate(PredicateBuilder.New<NostrEvent>(),
                    (current, temp) => current.Or(p => p.PublicKey.StartsWith(temp))));
            }

            if (filter.EventId?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[0])));
            }

            if (filter.PublicKey?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[0])));
            }

            var tagFilters = filter.GetAdditionalTagFilters();
            filterQuery = tagFilters.Where(tagFilter => tagFilter.Value.Any()).Aggregate(filterQuery,
                (current, tagFilter) => current.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Equals(tag.Data[1]))));

            if (filter.Limit is not null)
            {
                filterQuery = filterQuery.OrderBy(e => e.CreatedAt).TakeLast(filter.Limit.Value);
            }
            return filterQuery;
        }

        public static IEnumerable<NostrEvent> FilterByLimit(this IEnumerable<NostrEvent> events, int? limitFilter)
        {
            return limitFilter is not null ? events.OrderBy(e => e.CreatedAt).TakeLast(limitFilter.Value) : events;
        }

        public static IEnumerable<NostrEvent> Filter(this IEnumerable<NostrEvent> events, NostrSubscriptionFilter filter)
        {
            var filterQuery = events;


            if (filter.Ids?.Any() is true)
            {
                filterQuery = filterQuery.Where(e => filter.Ids.Any(s => e.Id.StartsWith(s)));
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
                filterQuery = filterQuery.Where(e => authors.Any(s => e.PublicKey.StartsWith(s)));
            }

            if (filter.EventId?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "e" && filter.EventId.Contains(tag.Data[0])));
            }

            if (filter.PublicKey?.Any() is true)
            {
                filterQuery = filterQuery.Where(e =>
                    e.Tags.Any(tag => tag.TagIdentifier == "p" && filter.PublicKey.Contains(tag.Data[0])));
            }

            var tagFilters = filter.GetAdditionalTagFilters();
            filterQuery = tagFilters.Where(tagFilter => tagFilter.Value.Any()).Aggregate(filterQuery,
                (current, tagFilter) => current.Where(e =>
                    e.Tags.Any(tag =>
                        tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Contains(tag.Data[0]))));

            return filterQuery;
        }
    }
}