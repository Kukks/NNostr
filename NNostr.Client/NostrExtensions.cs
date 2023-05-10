using LinqKit;

using NBitcoin.Secp256k1;

namespace NNostr.Client
{
    public static class NostrExtensions
    {
        public static string ToJson<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent, bool withoutId)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return
                $"[{(withoutId ? 0 : $"\"{nostrEvent.Id}\"")},\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag))}],\"{nostrEvent.Content}\"]";
        }

        public static string ComputeEventId(this string eventJson)
        {
            return eventJson.ComputeSha256Hash().AsSpan().ToHex();
        }

        public static string ComputeId(this NostrEvent nostrEvent) => nostrEvent.ComputeId<NostrEvent, NostrEventTag>();

        public static string ComputeId<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return nostrEvent.ToJson<TNostrEvent, TEventTag>(true).ComputeEventId();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv) =>
            nostrEvent.ComputeSignature<NostrEvent, NostrEventTag>(priv);

        public static string ComputeSignature<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent, ECPrivKey priv)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return nostrEvent.ToJson<TNostrEvent, TEventTag>(true).ComputeBIP340Signature(priv);
        }

        public static async ValueTask ComputeIdAndSignAsync(this NostrEvent nostrEvent, ECPrivKey priv,
            bool handlenip4 = true, int powDifficulty = 0) =>
            await nostrEvent.ComputeIdAndSignAsync<NostrEvent, NostrEventTag>(priv, handlenip4, powDifficulty);

        public static async ValueTask ComputeIdAndSignAsync<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent,
            ECPrivKey priv, bool handlenip4 = true, int powDifficulty = 0) where TNostrEvent : BaseNostrEvent<TEventTag>
            where TEventTag : NostrEventTag, new()
        {
            if (nostrEvent is not {PublicKey: null} && nostrEvent.PublicKey != priv.CreateXOnlyPubKey().ToHex())
            {
                throw new ArgumentException("Public key of the event does not match sender of this event", nameof(priv));
            }
            nostrEvent.PublicKey ??= priv.CreateXOnlyPubKey().ToHex();
            nostrEvent.CreatedAt ??= DateTimeOffset.UtcNow;
            if (handlenip4 && nostrEvent.Kind == 4)
            {
                await nostrEvent.EncryptNip04EventAsync<TNostrEvent, TEventTag>(priv);
            }

            nostrEvent.Id = nostrEvent.ComputeId<TNostrEvent, TEventTag>();
            nostrEvent.Signature = nostrEvent.ComputeSignature<TNostrEvent, TEventTag>(priv);

            ulong counter = 0;
            while (nostrEvent.CountPowDifficulty<TNostrEvent, TEventTag>(powDifficulty) < powDifficulty)
            {
                nostrEvent.SetTag<TNostrEvent, TEventTag>("nonce", counter.ToString(), powDifficulty.ToString());
            }
        }

        public static void SetTag(this NostrEvent nostrEvent, string identifier, params string[] data) =>
            nostrEvent.SetTag<NostrEvent, NostrEventTag>(identifier, data);

        public static void SetTag<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent, string identifier,
            params string[] data) where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag, new()
        {
            nostrEvent.Tags.RemoveAll(tag => tag.TagIdentifier == identifier);
            nostrEvent.Tags.Add(new TEventTag()
            {
                TagIdentifier = identifier,
                Data = data.ToList()
            });
        }

        public static int CountPowDifficulty(this NostrEvent nostrEvent, int? powDifficulty = null) =>
            nostrEvent.CountPowDifficulty<NostrEvent, NostrEventTag>(powDifficulty);

        public static int CountPowDifficulty<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent,
            int? powDifficulty = null) where TNostrEvent : BaseNostrEvent<TEventTag>
            where TEventTag : NostrEventTag, new()
        {
            var pow = 0;
            foreach (var c in nostrEvent.Id)
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

            var diffAttempt = nostrEvent.GetTaggedData<TNostrEvent, TEventTag>("nonce").ElementAtOrDefault(1);
            if (!string.IsNullOrEmpty(diffAttempt) && int.TryParse(diffAttempt, out var i))
            {
                return i < powDifficulty ? i : pow;
            }

            return 0;

        }

        public static bool Verify(this NostrEvent nostrEvent) => Verify<NostrEvent, NostrEventTag>(nostrEvent);

        public static bool Verify<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag, new()
        {
            var hash = nostrEvent.ToJson<TNostrEvent, TEventTag>(true).ComputeSha256Hash();
            if (hash.AsSpan().ToHex() != nostrEvent.Id)
            {
                return false;
            }

            var pub = nostrEvent.GetPublicKey<TNostrEvent, TEventTag>();
            if (!SecpSchnorrSignature.TryCreate(Convert.FromHexString(nostrEvent.Signature), out var sig))
            {
                return false;
            }

            return pub.SigVerifyBIP340(sig, hash);
        }

        public static ECXOnlyPubKey GetPublicKey(this NostrEvent nostrEvent) =>
            GetPublicKey<NostrEvent, NostrEventTag>(nostrEvent);

        public static ECXOnlyPubKey GetPublicKey<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag, new()
        {
            return ParsePubKey(nostrEvent.PublicKey);
        }

        public static ECPrivKey ParseKey(string key)
        {
            return ParseKey(Convert.FromHexString(key));
        }

        public static ECPrivKey ParseKey(byte[] key)
        {
            return ECPrivKey.Create(key);
        }

        public static ECXOnlyPubKey ParsePubKey(string key)
        {
            return ParsePubKey(Convert.FromHexString(key));
        }

        public static ECXOnlyPubKey ParsePubKey(byte[] key)
        {
            return Context.Instance.CreateXOnlyPubKey(key);
        }

        public static string ToHex(this ECPrivKey key)
        {
            Span<byte> output = stackalloc byte[32];
            key.WriteToSpan(output);
            return output.ToHex();
        }

        public static byte[] ToBytes(this ECPrivKey key)
        {
            Span<byte> output = stackalloc byte[32];
            key.WriteToSpan(output);
            return output.ToArray();
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
            return GetTaggedData<NostrEvent, NostrEventTag>(e, identifier);
        }

        public static string[] GetTaggedData<TNostrEvent, TEventTag>(this TNostrEvent e, string identifier)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return e.Tags.Where(tag => tag.TagIdentifier == identifier).Select(tag => tag.Data.First()).ToArray();
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
                filterQuery = filterQuery.Where(authors.Aggregate(PredicateBuilder.New<TNostrEvent>(),
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

        public static IEnumerable<TNostrEvent>
            FilterByLimit<TNostrEvent, TEventTag>(this IEnumerable<TNostrEvent> events, int? limitFilter)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return limitFilter is not null ? events.OrderBy(e => e.CreatedAt).TakeLast(limitFilter.Value) : events;
        }

        public static IEnumerable<TNostrEvent> Filter<TNostrEvent, TEventTag>(this IEnumerable<TNostrEvent> events,
            NostrSubscriptionFilter filter) where TNostrEvent : BaseNostrEvent<TEventTag>
            where TEventTag : NostrEventTag
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

        public static async Task SendEventsAndWaitUntilReceived(this INostrClient client, NostrEvent[] events,
            CancellationToken cancellationToken)
        {
            // create a subscription listening to event ids
            // send events
            // wait until all events are received either via subscription ro via ok received
            var tcs = new TaskCompletionSource();
            var evtIds = events.Select(e => e.Id).ToHashSet();
            var subId = Guid.NewGuid().ToString();
            await client.CreateSubscription(subId, new[]
            {
                new NostrSubscriptionFilter
                {
                    EventId = evtIds.ToArray()
                }
            }, cancellationToken);
            void OnClientOnOkReceived(object sender, (string eventId, bool success, string messafe) args)
            {
                if (evtIds.Remove(args.eventId) && !evtIds.Any())
                {
                    tcs.TrySetResult();
                }
            }
            void OnClientOnEventsReceived(object sender, (string subscriptionId, NostrEvent[] events) args)
            {
                if (args.subscriptionId == subId)
                {
                    foreach (var nostrEvent in args.events)
                    {
                        if (evtIds.Remove(nostrEvent.Id) && !evtIds.Any())
                        {
                            tcs.TrySetResult();
                            break;
                        }
                    }
                }
            }
            client.EventsReceived += OnClientOnEventsReceived;
            client.OkReceived += OnClientOnOkReceived;
            try
            {
                foreach (var evt in events)
                {
                    await client.PublishEvent(evt, cancellationToken);
                }
                await tcs.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                client.OkReceived -= OnClientOnOkReceived;
                client.EventsReceived -= OnClientOnEventsReceived;
            }

        }
    }
}