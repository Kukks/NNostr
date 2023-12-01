using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Web;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP47
{


    public class NostrWalletConnectServer:IAsyncDisposable
    {
        private readonly INostrClient _nostrClient;
        private readonly ECPrivKey _mainKey;
        private readonly string[] _supportedCommands;
        private readonly Func<ECXOnlyPubKey, Nip47Request, CancellationToken, Task<Nip47Response>> _requestHandler;
        private readonly ECXOnlyPubKey _mainPubKey;
        private readonly string _mainPubKeyHex;
        private string subscriptionId = Guid.NewGuid().ToString();
        private readonly Channel<NostrEvent> _requests = Channel.CreateUnbounded<NostrEvent>();

        public NostrWalletConnectServer(INostrClient nostrClient, ECPrivKey mainKey, string[] supportedCommands, Func<ECXOnlyPubKey, Nip47Request, CancellationToken,Task<Nip47Response>> requestHandler)
        {
            _nostrClient = nostrClient;
            _mainKey = mainKey;
            _supportedCommands = supportedCommands;
            _requestHandler = requestHandler;
            _mainPubKey = mainKey.CreateXOnlyPubKey();
            _mainPubKeyHex = _mainPubKey.ToHex();
        }

        public async Task Stop()
        {
            if(_cts is not null)
                _cts.Cancel();
            _nostrClient.EventsReceived -= NostrClientOnEventsReceived;
            await _nostrClient.CloseSubscription(subscriptionId);
        }
        
        public async Task Start(CancellationToken cancellationToken = default)
        {
            await Stop();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var infoEvent = await  CreateInfoEvent(_supportedCommands).ComputeIdAndSignAsync(_mainKey);
            await _nostrClient.PublishEvent(infoEvent, cancellationToken);
            _nostrClient.EventsReceived += NostrClientOnEventsReceived;
            _ = NostrClient.ProcessChannel(_requests, OnRequest, cancellationToken);
            var filters = new NostrSubscriptionFilter[]
            {
                new()
                {
                   ReferencedPublicKeys = new []{_mainPubKeyHex},
                   Limit = 0,
                   Kinds = new []{RequestEventKind}
                }
            };
            await _nostrClient.CreateSubscription(subscriptionId, filters, cancellationToken);
        }

        private async  Task<bool> OnRequest(NostrEvent evt, CancellationToken arg2)
        {
            Nip47Response? response;
            try
            {
                var request = JsonSerializer.Deserialize<Nip47Request>(evt.Content);

                if (!_supportedCommands.Contains(request.Method))
                {
                    response = new Nip47Response()
                    {
                        ResultType = ErrorCodes.NotImplemented,
                        Error = new Nip47Response.Nip47ResponseError()
                        {
                            Code = ErrorCodes.NotImplemented,
                            Message = $"{request.Method} is not implemented"
                        }
                    };

                }
                else
                {
                    response = await _requestHandler.Invoke(evt.GetPublicKey(), request, arg2);

                }
            }
            catch (Exception e) when (e is not OperationCanceledException )
            {
                response = new Nip47Response()
                {
                    ResultType = ErrorCodes.Internal,
                    Error = new Nip47Response.Nip47ResponseError()
                    {
                        Code = ErrorCodes.Internal,
                        Message = e.Message
                    }
                };
            }
            if(response is null)
                return true;
            var eventReply = CreateResponseEvent(evt, response);
            await eventReply.EncryptNip04EventAsync(_mainKey, null, true);
            eventReply = await eventReply.ComputeIdAndSignAsync(_mainKey, false);
            await  _nostrClient.PublishEvent(eventReply, arg2);
            return true;
        }


        private readonly HashSet<string> _seenEvents = new();
        private CancellationTokenSource? _cts;

        private void NostrClientOnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) e)
        {
            foreach (var nostrEvent in e.events)
            {
                if(nostrEvent.Kind != RequestEventKind || 
                   nostrEvent.GetTaggedPublicKeys().FirstOrDefault() != _mainPubKeyHex || 
                   Math.Abs((DateTimeOffset.UtcNow - nostrEvent.CreatedAt)!.Value.TotalMinutes) <= 10 ||
                   ! _seenEvents.Add(nostrEvent.Id) )
                    continue;
                
                _requests.Writer.TryWrite(nostrEvent);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            _cts?.Dispose();
            _requests.Writer.TryComplete();
        }
    }
    
    public static async Task<string[]?> FetchNIP47AvailableCommands(this INostrClient nostrClient, ECXOnlyPubKey serverKey, CancellationToken cancellationToken = default)
    {
        var filter = new NostrSubscriptionFilter()
        {
            Authors = new []{serverKey.ToHex()},
            Limit = 1,
            Kinds = new []{InfoEvent}
        };
        
        var result =await nostrClient.FetchEvents(new[]{filter}, cancellationToken);
        return   result?.FirstOrDefault()?.Content?.Split(" ");
    }
    
    public static async Task<Nip47Response> SendNIP47Request(this INostrClient nostrClient, ECXOnlyPubKey serverKey, ECPrivKey secretKey, Nip47Request request, CancellationToken cancellationToken = default)
    {
        var evt = CreateRequestEvent(request, serverKey);
        await evt.EncryptNip04EventAsync(secretKey, null, true);
        evt = await evt.ComputeIdAndSignAsync(secretKey, false);
        var responseEvt = await  nostrClient.SendEventAndWaitForReply(evt, cancellationToken);
        return JsonSerializer.Deserialize<Nip47Response>(responseEvt.Content);
            
    }
    public static class ErrorCodes
    {
        public const  string RateLimited = "RATE_LIMITED";
        public const  string NotImplemented = "NOT_IMPLEMENTED";
        public const  string InsufficientBalance = "INSUFFICIENT_BALANCE";
        public const  string QuotaExceeded = "QUOTA_EXCEEDED";
        public const  string Restricted = "RESTRICTED";
        public const  string Unauthorized = "UNAUTHORIZED";
        public const  string Internal = "INTERNAL";
        public const  string Other = "OTHER";
    }
    
    public const string UriScheme = "nostr+walletconnect";

    public static Uri CreateUri(ECXOnlyPubKey pubkey,ECPrivKey secret, Uri relay, string? lud16 = null, params Uri[] additionalRelays)
    {
        NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);
        query["relay"] = relay.ToString();
        query["secret"] = secret.CreateXOnlyPubKey().ToHex();
        if(lud16 is not null)
            query["lud16"] = lud16;
        if (additionalRelays.Length > 0)
        {
            foreach (var additionalRelay in additionalRelays)
            {
                query.Add("relay", additionalRelay.ToString());
            }
        }
        var uriBuilder = new UriBuilder(UriScheme, pubkey.ToHex())
        {
            Query = query.ToString()
        };
        return uriBuilder.Uri;
    }
    
    public static (ECXOnlyPubKey pubkey, ECPrivKey secret, Uri[] , string? lud16) ParseUri(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        var lud16 = query["lud16"];
        var relays = query.GetValues("relay")?? Array.Empty<string>();
        var relaysUris =   relays.Prepend(query["relay"]).Select(s => new Uri(s)).ToArray();
        return (NostrExtensions.ParsePubKey(uri.Host), NostrExtensions.ParseKey(query["secret"]),  relaysUris, lud16);
    }
    
    public static NostrEvent CreateRequestEvent(Nip47Request request, ECXOnlyPubKey destination)
    {
        var result =  new NostrEvent()
        {
            Kind = RequestEventKind,
            Content = JsonSerializer.Serialize(request),
            CreatedAt = DateTimeOffset.Now
            
        };
        result.SetReferencedPublickKey(destination);
        return result;
    }
    public static NostrEvent CreateInfoEvent(string[] supportedCommands)
    {
        var result =  new NostrEvent()
        {
            Kind = InfoEvent,
            Content = string.Join(" ",supportedCommands),
            CreatedAt = DateTimeOffset.Now
        };
        return result;
    }

    public static NostrEvent CreateResponseEvent(NostrEvent requestEvent, Nip47Response response)
    {
        var result = new NostrEvent()
        {
            Kind = ResponseEventKind,
            Content = JsonSerializer.Serialize(response),
            CreatedAt = DateTimeOffset.Now
        };
        result.SetReferencedPublickKey(requestEvent.GetPublicKey());
        result.SetReferencedEvent(requestEvent.Id);

        return result;
    }
    
    
    
    public const int InfoEvent = 13194;
    public const int RequestEventKind = 23194;
    public const int ResponseEventKind = 23194;

    public class Nip47Request
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }
        [JsonPropertyName("params")]
        public JsonObject Parameters { get; set; }
    }
    
    public class Nip47Response
    {
        [JsonPropertyName("result_type")]
        public string ResultType { get; set; }
        [JsonPropertyName("error")]
        public Nip47ResponseError? Error { get; set; }
        [JsonPropertyName("result")]
        public JsonObject? Result { get; set; }


        public class Nip47ResponseError
        {
            [JsonPropertyName("code")]
            public string Code { get; set; }
            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }
    
}