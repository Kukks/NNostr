using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Web;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP47
{
    public class NostrWalletConnectServer : IAsyncDisposable
    {
        private readonly INostrClient _nostrClient;
        private readonly ECPrivKey _mainKey;
        private readonly string[] _supportedCommands;
        private readonly Func<ECXOnlyPubKey, Nip47Request, CancellationToken, Task<Nip47Response>> _requestHandler;
        private readonly ECXOnlyPubKey _mainPubKey;
        private readonly string _mainPubKeyHex;
        private string subscriptionId = Guid.NewGuid().ToString();
        private readonly Channel<NostrEvent> _requests = Channel.CreateUnbounded<NostrEvent>();

        public NostrWalletConnectServer(INostrClient nostrClient, ECPrivKey mainKey, string[] supportedCommands,
            Func<ECXOnlyPubKey, Nip47Request, CancellationToken, Task<Nip47Response>> requestHandler)
        {
            _nostrClient = nostrClient;
            _mainKey = mainKey;
            _supportedCommands = supportedCommands;
            _requestHandler = requestHandler;
            _mainPubKey = mainKey.CreateXOnlyPubKey();
            _mainPubKeyHex = _mainPubKey.ToHex();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await StopAsync(cancellationToken);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var infoEvent = await CreateInfoEvent(_supportedCommands, Array.Empty<string>()).ComputeIdAndSignAsync(_mainKey);
            await _nostrClient.PublishEvent(infoEvent, cancellationToken);
            _nostrClient.EventsReceived += NostrClientOnEventsReceived;
            _ = NostrClient.ProcessChannel(_requests, OnRequest, cancellationToken);
            var filters = new NostrSubscriptionFilter[]
            {
                new()
                {
                    ReferencedPublicKeys = new[] {_mainPubKeyHex},
                    Limit = 0,
                    Kinds = new[] {RequestEventKind}
                }
            };
            await _nostrClient.CreateSubscription(subscriptionId, filters, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts is not null)
                _cts.Cancel();
            _nostrClient.EventsReceived -= NostrClientOnEventsReceived;
            await _nostrClient.CloseSubscription(subscriptionId, cancellationToken);
        }

        private async Task<bool> OnRequest(NostrEvent evt, CancellationToken arg2)
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
            catch (Exception e) when (e is not OperationCanceledException)
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

            if (response is null)
                return true;
            var eventReply = CreateResponseEvent(evt, response);
            await eventReply.EncryptNip04EventAsync(_mainKey, null, true);
            eventReply = await eventReply.ComputeIdAndSignAsync(_mainKey, false);
            await _nostrClient.PublishEvent(eventReply, arg2);
            return true;
        }


        private readonly HashSet<string> _seenEvents = new();
        private CancellationTokenSource? _cts;

        private void NostrClientOnEventsReceived(object? sender, (string subscriptionId, NostrEvent[] events) e)
        {
            foreach (var nostrEvent in e.events)
            {
                if (nostrEvent.Kind != RequestEventKind ||
                    nostrEvent.GetTaggedPublicKeys().FirstOrDefault() != _mainPubKeyHex ||
                    Math.Abs((DateTimeOffset.UtcNow - nostrEvent.CreatedAt)!.Value.TotalMinutes) <= 10 ||
                    !_seenEvents.Add(nostrEvent.Id))
                    continue;

                _requests.Writer.TryWrite(nostrEvent);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None);
            _cts?.Dispose();
            _requests.Writer.TryComplete();
        }
    }

    public static async Task<(string[] Commands, string[] Notifications)?> FetchNIP47AvailableCommands(this INostrClient nostrClient,
        ECXOnlyPubKey serverKey, CancellationToken cancellationToken = default)
    {
        var filter = new NostrSubscriptionFilter()
        {
            Authors = new[] {serverKey.ToHex()},
            Limit = 1,
            Kinds = new[] {InfoEvent}
        };

        var result = (await nostrClient.FetchEvents(new[] {filter}, cancellationToken)).FirstOrDefault();
        if (result is null)
        {
            return null;
        }
        var commands =  result.Content!.Split(" ");
        var notifications = result?.GetTaggedData("notifications").SelectMany(s => s.Split(" ")).Distinct().ToArray();
        
        return (commands, notifications);
    }

    public static async Task<Nip47Response> SendNIP47Request(this INostrClient nostrClient, ECXOnlyPubKey serverKey,
        ECPrivKey secretKey, INIP47Request request, CancellationToken cancellationToken = default)
    {
        return await nostrClient.SendNIP47Request(serverKey, secretKey, request.ToNip47Request(), cancellationToken);
    }
    
    public static async Task<T> SendNIP47Request<T>(this INostrClient nostrClient, ECXOnlyPubKey serverKey,
        ECPrivKey secretKey, INIP47Request request, CancellationToken cancellationToken = default) where T : class
    {
        var res =  await nostrClient.SendNIP47Request(serverKey, secretKey, request, cancellationToken);
        if (res.Error is not null)
            throw new Exception($"{res.Error.Code}:{res.Error.Message}");
        return res.Deserialize<T>() ?? throw new Exception("Invalid response");
    }
    
    public static async Task<Nip47Response> SendNIP47Request(this INostrClient nostrClient, ECXOnlyPubKey serverKey,
        ECPrivKey secretKey, Nip47Request request, CancellationToken cancellationToken = default)
    {
        var evt = CreateRequestEvent(request, serverKey);
        await evt.EncryptNip04EventAsync(secretKey, null, true);
        evt = await evt.ComputeIdAndSignAsync(secretKey, false);
        var responseEvt = await nostrClient.SendEventAndWaitForReply(evt, cancellationToken);
        var decryptedContent = await responseEvt.DecryptNip04EventAsync(secretKey, null, true);
        return JsonSerializer.Deserialize<Nip47Response>(decryptedContent);
    }

    public static async IAsyncEnumerable<Nip47Notification> SubscribeNip47Notifications(this INostrClient nostrClient, ECXOnlyPubKey serverKey,
        ECPrivKey secretKey,[EnumeratorCancellation] CancellationToken cancellationToken )
    {
        await foreach (var nostrEvent in nostrClient.SubscribeForEvents(new NostrSubscriptionFilter[]
                       {
                           new()
                           {
                               Authors = new[] {serverKey.ToHex()},
                               ReferencedPublicKeys = new[] {secretKey.CreateXOnlyPubKey().ToHex()}
                           }
                       }, false, cancellationToken))
        {
                if (nostrEvent.Kind != NotificationEventKind)
                    continue;
                
                var decryptedContent = await nostrEvent.DecryptNip04EventAsync(secretKey, null, true);
                var notification =  JsonSerializer.Deserialize<Nip47Notification>(decryptedContent);
                if (notification is not null)
                    yield return notification;
            
        }
    }

    public static class ErrorCodes
    {
        public const string RateLimited = "RATE_LIMITED";
        public const string NotImplemented = "NOT_IMPLEMENTED";
        public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
        public const string QuotaExceeded = "QUOTA_EXCEEDED";
        public const string Restricted = "RESTRICTED";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Internal = "INTERNAL";
        public const string Other = "OTHER";
    }

    public const string UriScheme = "nostr+walletconnect";

    public static Uri CreateUri(ECXOnlyPubKey pubkey, ECPrivKey secret, Uri relay, string? lud16 = null,
        params Uri[] additionalRelays)
    {
        NameValueCollection query = HttpUtility.ParseQueryString(string.Empty);
        query["relay"] = relay.ToString();
        query["secret"] = secret.CreateXOnlyPubKey().ToHex();
        if (lud16 is not null)
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

    public static (ECXOnlyPubKey pubkey, ECPrivKey secret, Uri[] relays, string lud16) ParseUri(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        var lud16 = query["lud16"];
        var relays = query.GetValues("relay") ?? Array.Empty<string>();
        var relaysUris = relays.Prepend(query["relay"]).Select(s => new Uri(s)).ToArray();
        return (NostrExtensions.ParsePubKey(uri.Host), NostrExtensions.ParseKey(query["secret"]), relaysUris, lud16);
    }

    public static NostrEvent CreateRequestEvent(Nip47Request request, ECXOnlyPubKey destination)
    {
        var result = new NostrEvent()
        {
            Kind = RequestEventKind,
            Content = JsonSerializer.Serialize(request),
            CreatedAt = DateTimeOffset.Now
        };
        result.SetReferencedPublickKey(destination);
        return result;
    }

    public static NostrEvent CreateInfoEvent(string[] supportedCommands, string[] supportedNotifications)
    {
        if (supportedNotifications.Any() is true && !supportedCommands.Contains("notifications"))
        {
            supportedCommands = supportedCommands.Append("notifications").ToArray();
        }
        var result = new NostrEvent()
        {
            Kind = InfoEvent,
            Content = string.Join(" ", supportedCommands),
            CreatedAt = DateTimeOffset.Now
        };
        if (supportedNotifications.Any())
        {
            result.SetTag("notifications", string.Join(" ", supportedNotifications));
        }
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
    public const int ResponseEventKind = 23195;
    public const int NotificationEventKind = 23196;

    public class Nip47Request
    {
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("params")] public JsonObject Parameters { get; set; }

        public static Nip47Request Create(string method, object parameters)
        {
            var parametersJson = JsonSerializer.Deserialize<JsonObject>(JsonSerializer.Serialize(parameters));
            parametersJson?.Remove("Method");
            return new Nip47Request()
            {
                Method = method,
                Parameters = parametersJson?? new JsonObject()
            };
        }
    }

    public class Nip47Notification
    {
        
        [JsonPropertyName("notification_type")] public string NotificationType { get; set; }
        [JsonPropertyName("notification")] public JsonObject? Notification { get; set; }
        
        public T? Deserialize<T>() where T:class
        {
            return Notification?.Deserialize<T>();
        }

    }

    public class Nip47Response
    {
        [JsonPropertyName("result_type")] public string ResultType { get; set; }
        [JsonPropertyName("error")] public Nip47ResponseError? Error { get; set; }
        [JsonPropertyName("result")] public JsonObject? Result { get; set; }
        
        public T? Deserialize<T>() where T:class
        {
            return Result?.Deserialize<T>();
        }

        public class Nip47ResponseError
        {
            [JsonPropertyName("code")] public string Code { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; }
        }
    }
    

public class NIP47Request:INIP47Request
{
    public NIP47Request(string method)
    {
        Method = method;
    }

    public string Method { get; }
}

public interface INIP47Request
    {
        [JsonIgnore] string Method { get; }

        public virtual Nip47Request ToNip47Request()
        {
            return Nip47Request.Create(Method, this);
        }
    }

    public class PayKeysendRequest : INIP47Request
    {
        public string Method => "pay_keysend";

        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("pubkey")] public string Pubkey { get; set; }

        [JsonPropertyName("preimage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Preimage { get; set; }

        [JsonPropertyName("tlv_records")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TlvRecord[]? TlvRecords { get; set; }
    }

    public class TlvRecord
    {
        [JsonPropertyName("type")] public string Type { get; set; }

        [JsonPropertyName("value")] public string Value { get; set; }
    }

    public class PayInvoiceRequest : INIP47Request
    {
        public string Method => "pay_invoice";
        public string Invoice { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }
    }

    public class PayInvoiceResponse
    {
        [JsonPropertyName("preimage")] public string Preimage { get; set; }
    }

    public class MakeInvoiceRequest : INIP47Request
    {
        public string Method => "make_invoice";
        [JsonPropertyName("amount")] public long AmountMsats { get; set; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("description_hash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DescriptionHash { get; set; }

        [JsonPropertyName("expiry")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? ExpirySeconds { get; set; }
    }


    public class Nip47Transaction
    {
// "type": "incoming", // "incoming" for invoices, "outgoing" for payments
// "invoice": "string", // encoded invoice, optional
// "description": "string", // invoice's description, optional
// "description_hash": "string", // invoice's description hash, optional
// "preimage": "string", // payment's preimage, optional if unpaid
// "payment_hash": "string", // Payment hash for the payment
// "amount": 123, // value in msats
// "fees_paid": 123, // value in msats
// "created_at": unixtimestamp, // invoice/payment creation time
// "expires_at": unixtimestamp, // invoice expiration time, optional if not applicable
// "metadata": {} // generic metadata that can be used to add things like zap/boostagram details for a payer name/comment/etc.

        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("invoice")] public string? Invoice { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("description_hash")] public string? DescriptionHash { get; set; }
        [JsonPropertyName("preimage")] public string? Preimage { get; set; }
        [JsonPropertyName("payment_hash")] public string PaymentHash { get; set; }
        [JsonPropertyName("amount")] public long AmountMsats { get; set; }
        [JsonPropertyName("fees_paid")] public long FeesPaidMsats { get; set; }
        [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
        [JsonPropertyName("expires_at")] public long? ExpiresAt { get; set; }
        [JsonPropertyName("settled_at")] public long? SettledAt { get; set; }
        [JsonPropertyName("metadata")] public JsonObject Metadata { get; set; }
    }

    public class GetInfoRequest : INIP47Request
    {
        public string Method => "get_info";
    }

    public class GetInfoResponse
    {
// "alias": "string",
// "color": "hex string",
// "pubkey": "hex string",
// "network": "string", // mainnet, testnet, signet, or regtest
// "block_height": 1,
// "block_hash": "hex string",
// "methods": ["pay_invoice", "get_balance", "make_invoice", "lookup_invoice", "list_transactions", "get_info"], // list of supported methods for this connection
// }
// }

        [JsonPropertyName("alias")] public string Alias { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; }
        [JsonPropertyName("pubkey")] public string Pubkey { get; set; }
        [JsonPropertyName("network")] public string Network { get; set; }
        [JsonPropertyName("block_height")] public long BlockHeight { get; set; }
        [JsonPropertyName("block_hash")] public string BlockHash { get; set; }
        [JsonPropertyName("methods")] public string[] Methods { get; set; }
    }

    public class GetBalanceResponse
    {
        [JsonPropertyName("balance")] public long BalanceMsats { get; set; }
    }

    public class ListTransactionsRequest : INIP47Request
    {
// "from": 1693876973, // starting timestamp in seconds since epoch (inclusive), optional
// "until": 1703225078, // ending timestamp in seconds since epoch (inclusive), optional
// "limit": 10, // maximum number of invoices to return, optional
// "offset": 0, // offset of the first invoice to return, optional
// "unpaid": true, // include unpaid invoices, optional, default false
// "type": "incoming", // "incoming" for invoices, "outgoing" for payments, undefined for both

        public string Method => "list_transactions";

        [JsonPropertyName("from")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long From { get; set; }

        [JsonPropertyName("until")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long Until { get; set; }

        [JsonPropertyName("limit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Limit { get; set; }

        [JsonPropertyName("offset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Offset { get; set; }

        [JsonPropertyName("unpaid")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Unpaid { get; set; }

        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Type { get; set; }
    }


    public class ListTransactionsResponse
    {
        [JsonPropertyName("transactions")] public Nip47Transaction[] Transactions { get; set; }
    }

    public class LookupInvoiceRequest : INIP47Request
    {
        public string Method => "lookup_invoice";

        [JsonPropertyName("payment_hash")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PaymentHash { get; set; }

        [JsonPropertyName("invoice")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Invoice { get; set; }
    }
}