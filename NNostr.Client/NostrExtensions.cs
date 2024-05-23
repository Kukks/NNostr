using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using LinqKit;
using NBitcoin.Secp256k1;
using NNostr.Client.JsonConverters;
using SHA256 = System.Security.Cryptography.SHA256;

namespace NNostr.Client
{
    public static class NostrExtensions
    {
        
        public static string ToIdPreimage<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent, bool withoutId)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            var content = StringEscaperJsonConverter.JavaScriptStringEncode(nostrEvent.Content, false);
            return
                $"[{(withoutId ? 0 : $"\"{nostrEvent.Id}\"")},\"{nostrEvent.PublicKey}\",{nostrEvent.CreatedAt?.ToUnixTimeSeconds()},{nostrEvent.Kind},[{string.Join(',', nostrEvent.Tags.Select(tag => tag))}],\"{content}\"]";
        }

        public static string ComputeEventId(this string eventJson)
        {
            return eventJson.ComputeSha256Hash().AsSpan().ToHex();
        }

        public static string ComputeId(this NostrEvent nostrEvent) => nostrEvent.ComputeId<NostrEvent, NostrEventTag>();

        public static string ComputeId<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return nostrEvent.ToIdPreimage<TNostrEvent, TEventTag>(true).ComputeEventId();
        }

        public static string ComputeSignature(this NostrEvent nostrEvent, ECPrivKey priv) =>
            nostrEvent.ComputeSignature<NostrEvent, NostrEventTag>(priv);

        public static string ComputeSignature<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent, ECPrivKey priv)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return nostrEvent.ToIdPreimage<TNostrEvent, TEventTag>(true).ComputeBIP340Signature(priv);
        }

        public static async Task<NostrEvent> ComputeIdAndSignAsync(this NostrEvent nostrEvent, ECPrivKey priv,
            bool handlenip4 = true, int powDifficulty = 0)
        {
            return await nostrEvent.ComputeIdAndSignAsync<NostrEvent, NostrEventTag>(priv, handlenip4, powDifficulty);
        }

        internal static async Task<TNostrEvent> ComputeIdAndSignAsync<TNostrEvent, TEventTag>(this TNostrEvent nostrEvent,
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

            return nostrEvent;
        }

        public static NostrEvent SetReferencedPublickKey(this NostrEvent nostrEvent, string identity)
        {
            nostrEvent.SetTag("p", identity);
            return nostrEvent;
        }
        public static NostrEvent SetReferencedPublickKey(this NostrEvent nostrEvent, ECXOnlyPubKey identity)
        {
           return SetReferencedPublickKey(nostrEvent, identity.ToHex());
        }  
        public static NostrEvent SetReferencedEvent(this NostrEvent nostrEvent, string eventId)
        {
            nostrEvent.SetTag("e", eventId);
            return nostrEvent;
        }

        public static void SetTag(this NostrEvent nostrEvent, string identifier, params string[] data) =>
            nostrEvent.SetTag<NostrEvent, NostrEventTag>(identifier, data);

        public static NostrEvent SetTag(this NostrEvent nostrEvent, string identifier, string data)
        {
            nostrEvent.SetTag(identifier, new[] {data});
            return nostrEvent;
            
        }

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
            var hash = nostrEvent.ToIdPreimage<TNostrEvent, TEventTag>(true).ComputeSha256Hash();
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
            return ParseKey(key.FromHex().ToArray());
        }

        public static ECPrivKey ParseKey(byte[] key)
        {
            return ECPrivKey.Create(key);
        }

        public static ECXOnlyPubKey ParsePubKey(string key)
        {
            return ParsePubKey(key.FromHex().ToArray());
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

        public static string[] GetTaggedPublicKeys(this NostrEvent e)
        {
            return e.GetTaggedData("p");
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



        public static IEnumerable<TNostrEvent>
            FilterByLimit<TNostrEvent, TEventTag>(this IEnumerable<TNostrEvent> events, int? limitFilter)
            where TNostrEvent : BaseNostrEvent<TEventTag> where TEventTag : NostrEventTag
        {
            return limitFilter is not null ? events.OrderBy(e => e.CreatedAt).TakeLast(limitFilter.Value) : events;
        }

        public static IEnumerable<TNostrEvent> Filter<TNostrEvent, TEventTag>(this IEnumerable<TNostrEvent> events,
            NostrSubscriptionFilter[] filters) where TNostrEvent : BaseNostrEvent<TEventTag>
            where TEventTag : NostrEventTag
        {
            var results = filters.Select(filter =>
            {
                var res = events.Filter<TNostrEvent, TEventTag>(filter);
                return FilterByLimit<TNostrEvent, TEventTag>(res, filter.Limit);
            });
            // union the results
            return results.Aggregate((a, b) => a.Union(b));
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

            var tagFilters = filter.GetAdditionalTagFilters();
            filterQuery = tagFilters.Where(tagFilter => tagFilter.Value.Any()).Aggregate(filterQuery,
                (current, tagFilter) => current.Where(e =>
                    e.Tags.Any(tag =>
                        tag.TagIdentifier == tagFilter.Key && tagFilter.Value.Contains(tag.Data[0]))));

            return filterQuery;
        }

        public static async IAsyncEnumerable<NostrEvent> SubscribeForEvents(this INostrClient client,
            NostrSubscriptionFilter[] filters, bool stopWhenEoseSent, [EnumeratorCancellation]CancellationToken cancellationToken)
        {
         var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);   
            var subscriptionId = Guid.NewGuid().ToString();
            var _receivedEvents = Channel.CreateUnbounded<NostrEvent>(new() {SingleReader = true, SingleWriter = true});
            
            var tcs = new TaskCompletionSource<bool>();
            linkedCts.Token.Register(() => tcs.TrySetResult(true));
            void OnClientOnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) args)
                {
                    if (args.subscriptionId == subscriptionId)
                    {
                        foreach (var @event in args.events)
                        {
                            _receivedEvents.Writer.TryWrite(@event);
                        }
                    }
                }
            void OnClientOnEoseReceived(object? sender, string s)
            {
                if (s == subscriptionId)
                {
                    tcs.TrySetResult(true);
                }
            }
            

            void NostrClientOnStateChanged(object? sender, WebSocketState? e)
            {
                if (e is not WebSocketState.Open)
                    tcs.TrySetResult(true);
            } void NostrClientOnStateChanged2(object? sender, (Uri, WebSocketState?) valueTuple)
            {
                if (sender is CompositeNostrClient client2 && client2.States.Values.All(state => state is not WebSocketState.Open))
                    tcs.TrySetResult(true);
            }
            if (stopWhenEoseSent)
            {
                client.EoseReceived += OnClientOnEoseReceived;
            }
            client.EventsReceived += OnClientOnEventsReceived;
            if(client is NostrClient nostrClient)
            {
                nostrClient.StateChanged += NostrClientOnStateChanged;
            }else if(client is CompositeNostrClient nostrClient2)
            {
                nostrClient2.StateChanged += NostrClientOnStateChanged2;
            }
            try
            {
                
                await client.CreateSubscription(subscriptionId, filters, linkedCts.Token);
                while (true)
                {
                    var delayTask = tcs.Task;
                    var readTask = _receivedEvents.Reader.WaitToReadAsync().AsTask();

                    if (await Task.WhenAny(delayTask, readTask) == delayTask)
                    {
                        break; // Cancellation has been requested
                    }
                    while (_receivedEvents.Reader.TryRead(out var @event))
                    {
                        yield return @event;
                    }
                }
            }
            finally
            {
                await client.CloseSubscription(subscriptionId);
                client.EventsReceived -= OnClientOnEventsReceived;
                client.EoseReceived -= OnClientOnEoseReceived;
                if(client is NostrClient nostrClient2)
                {
                    nostrClient2.StateChanged -= NostrClientOnStateChanged;
                }else if(client is CompositeNostrClient nostrClient3)
                {
                    nostrClient3.StateChanged -= NostrClientOnStateChanged2;
                }
            }
        }

        public static async Task SendEventsAndWaitUntilReceived(this INostrClient client, NostrEvent[] events,
            CancellationToken cancellationToken)
        {
            // create a subscription listening to event ids
            // send events
            // wait until all events are received either via subscription ro via ok received
            var tcs = new TaskCompletionSource<bool>();
            var evtIds = events.Select(e => e.Id).ToHashSet();
            var subId = Guid.NewGuid().ToString();
            await client.CreateSubscription(subId, new[]
            {
                new NostrSubscriptionFilter
                {
                    Ids = evtIds.ToArray()
                }
            }, cancellationToken);
            void OnClientOnOkReceived(object sender, (string eventId, bool success, string messafe) args)
            {
                if (evtIds.Remove(args.eventId) && !evtIds.Any())
                {
                    tcs.TrySetResult(true);
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
                            tcs.TrySetResult(true);
                            break;
                        }
                    }
                }
            }
            
            void NostrClientOnStateChanged(object? sender, WebSocketState? e)
            {
                if (e is not WebSocketState.Open)
                    tcs.TrySetResult(true);
            } void NostrClientOnStateChanged2(object? sender, (Uri, WebSocketState?) valueTuple)
            {
                if (sender is CompositeNostrClient client2 && client2.States.Values.All(state => state is not WebSocketState.Open))
                    tcs.TrySetResult(true);
            }
            client.EventsReceived += OnClientOnEventsReceived;
            client.OkReceived += OnClientOnOkReceived;
            
            if(client is NostrClient nostrClient)
            {
                nostrClient.StateChanged += NostrClientOnStateChanged;
            }else if(client is CompositeNostrClient nostrClient2)
            {
                nostrClient2.StateChanged += NostrClientOnStateChanged2;
            }
            try
            {
                foreach (var evt in events)
                {
                    await client.PublishEvent(evt, cancellationToken);
                }
                
                await WaitForTaskCompletionSourceWithCancellationToken(tcs, cancellationToken);
            }
            finally
            {
                client.OkReceived -= OnClientOnOkReceived;
                client.EventsReceived -= OnClientOnEventsReceived;
                if(client is NostrClient nostrClient2)
                {
                    nostrClient2.StateChanged -= NostrClientOnStateChanged;
                }else if(client is CompositeNostrClient nostrClient3)
                {
                    nostrClient3.StateChanged -= NostrClientOnStateChanged2;
                }
            }

        }

        public static async Task<List<NostrEvent>> FetchEvents(this INostrClient client,
            NostrSubscriptionFilter[] filters, CancellationToken cancellationToken)
        {
            HashSet<NostrEvent> result = new();
            TaskCompletionSource<bool> tcs = new();
            var subId = Guid.NewGuid().ToString();

            client.EoseReceived += clientOnEoseReceived;
            client.EventsReceived += ClientOnEventsReceived;
            switch (client)
            {
                case NostrClient nostrClient:
                    nostrClient.StateChanged += NostrClientOnStateChanged;
                    break;
                case CompositeNostrClient nostrClient2:
                    nostrClient2.StateChanged += NostrClientOnStateChanged2;
                    break;
            }

            await client.CreateSubscription(subId, filters, cancellationToken);

            void ClientOnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) e)
            {
                if (e.subscriptionId != subId) return;
                foreach (var nostrEvent in e.events)
                {
                    result.Add(nostrEvent);
                }
            }

            void clientOnEoseReceived(object? sender, string e)
            {
                if (e == subId)
                {
                    tcs.TrySetResult(true);
                }
            }

            void NostrClientOnStateChanged(object? sender, WebSocketState? e)
            {
                if (e is not WebSocketState.Open)
                    tcs.TrySetResult(true);
            }

            void NostrClientOnStateChanged2(object? sender, (Uri, WebSocketState?) valueTuple)
            {
                if (sender is CompositeNostrClient client2 &&
                    client2.States.Values.All(state => state is not WebSocketState.Open))
                    tcs.TrySetResult(true);
            }

            try
            {
                await WaitForTaskCompletionSourceWithCancellationToken(tcs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                client.EventsReceived -= ClientOnEventsReceived;
                client.EoseReceived -= clientOnEoseReceived;
                if(client is NostrClient nostrClient2)
                {
                    nostrClient2.StateChanged -= NostrClientOnStateChanged;
                }else if(client is CompositeNostrClient nostrClient3)
                {
                    nostrClient3.StateChanged -= NostrClientOnStateChanged2;
                }
                
            }

            return result.ToList();
        }

        public static  async Task WaitForTaskCompletionSourceWithCancellationToken<T>(TaskCompletionSource<T> tcs,
            CancellationToken cancellationToken)
        {
#if NETSTANDARD

            await Task.WhenAny(tcs.Task, new Task(async o =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }, cancellationToken));
#else
                await tcs.Task.WaitAsync(cancellationToken);
#endif
        }

        public static async Task<NostrEvent> SendEventAndWaitForReply(this INostrClient client, NostrEvent nostrEvent, NostrSubscriptionFilter[] expectedReplyFilter,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<NostrEvent>();
            var evtId = nostrEvent.Id;
            var subId = Guid.NewGuid().ToString();
            
            void OnClientOnEventsReceived(object sender, (string subscriptionId, NostrEvent[] events) args)
            {
                if (args.subscriptionId == subId)
                {
                    foreach (var nostrEvent in args.events)
                    {
                        if (nostrEvent.Id != evtId)
                        {
                            tcs.TrySetResult(nostrEvent);
                            break;
                        }
                    }
                }
            }
            
            void NostrClientOnStateChanged(object? sender, WebSocketState? e)
            {
                if (e is not WebSocketState.Open)
                    tcs.TrySetResult(null);
            } void NostrClientOnStateChanged2(object? sender, (Uri, WebSocketState?) valueTuple)
            {
                if (sender is CompositeNostrClient client2 && client2.States.Values.All(state => state is not WebSocketState.Open))
                    tcs.TrySetResult(null);
            }
            client.EventsReceived += OnClientOnEventsReceived;
            
            if(client is NostrClient nostrClient)
            {
                nostrClient.StateChanged += NostrClientOnStateChanged;
            }else if(client is CompositeNostrClient nostrClient2)
            {
                nostrClient2.StateChanged += NostrClientOnStateChanged2;
            }
            await client.CreateSubscription(subId, expectedReplyFilter, cancellationToken);
            try
            {
                await client.PublishEvent(nostrEvent, cancellationToken);
                return await tcs.Task;
            }
            finally
            {
                client.EventsReceived -= OnClientOnEventsReceived;
                if(client is NostrClient nostrClient2)
                {
                    nostrClient2.StateChanged -= NostrClientOnStateChanged;
                }else if(client is CompositeNostrClient nostrClient3)
                {
                    nostrClient3.StateChanged -= NostrClientOnStateChanged2;
                }
            }
        }
        
        public static async Task<NostrEvent> SendEventAndWaitForReply(this INostrClient client,NostrEvent nostrEvent,
            CancellationToken cancellationToken)
        {
            
            var evtId = nostrEvent.Id;
 
            var expectedReplyFilter = new[]
            {
                new NostrSubscriptionFilter
                {
                    ReferencedEventIds = new[] {evtId},
                    ReferencedPublicKeys = new []{nostrEvent.PublicKey}
                }
            };
            
            return await  SendEventAndWaitForReply(client, nostrEvent, expectedReplyFilter, cancellationToken);
            

        }
    }
}

#if NETSTANDARD
public static class Convert
{
    public static byte[] FromHexString(string hexString)
    {
        var bytes = new byte[hexString.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = System.Convert.ToByte(hexString.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    public static byte[] FromBase64String(string base64String)
    {
        return System.Convert.FromBase64String(base64String);
    }
    public static string ToBase64String(byte[] bytes)
    {
        return System.Convert.ToBase64String(bytes);
    }
}
#endif