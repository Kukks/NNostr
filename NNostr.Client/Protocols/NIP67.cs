using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP67
{
    public static int EventKind = 33194;
    public record NIP67UriPayload(
        ECXOnlyPubKey Pubkey,
        string Secret,
        string[] Relays,
        string[] RequiredCommands,
        string[] OptionalCommands,
        string? Budget,
        string? Identity)
    {
        public override string ToString()
        {
            var result =
                $"{UriScheme}://{Pubkey.ToHex()}?relay={string.Join("&relay=", Relays)}&secret={Secret}&required_commands={string.Join(" ", RequiredCommands)}";
            
            if(OptionalCommands.Length > 0)
                result += $"&optional_commands={string.Join(" ", OptionalCommands)}";
            if(Budget is not null)
                result += $"&budget={Budget}";
            if(Identity is not null)
                result += $"&identity={Identity}";
            return result;
        }
    }
    
    
    public const string UriScheme = "nostr+walletconnect";
    
    //nostr+walletauth://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0ef42ab558e9d4?relay=wss%3A%2F%2Frelay.damus.io&secret=b8a30fafa48d4795b6c0eec169a383de&required_commands=pay_invoice%20pay_keysend%20make_invoice%20lookup_invoice&optional_commands=list_transactions&budget=10000%2Fdaily
    
    public static NIP67UriPayload ParseUri(Uri uri)
    {
        if (!uri.Scheme.Equals(UriScheme, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("Invalid scheme", nameof(uri));
        var query = HttpUtility.ParseQueryString(uri.Query);
        
        var relays = query.GetValues("relay") ?? Array.Empty<string>();
        var secret = query["secret"];
        var requiredCommands = query.GetValues("required_commands")?.SelectMany(s => s.Split(" ")).Distinct().ToArray() ?? Array.Empty<string>();
        var optionalCommands = query.GetValues("optional_commands")?.SelectMany(s => s.Split(" ")).Distinct().ToArray() ?? Array.Empty<string>();
        var budget = query.GetValues("budget")?.FirstOrDefault();
        var identity = query.GetValues("identity")?.FirstOrDefault();
        
        return new NIP67UriPayload(NostrExtensions.ParsePubKey(uri.Host), secret, relays, requiredCommands, optionalCommands, budget, identity);
    }

    public record Nip67ConfirmationEventContent()
    {
          //format is :
     /*
      *{
    "secret": "b8a30fafa48d4795b6c0eec169a383de", // string, the secret from the URI
    "commands": [ // array of strings, commands that the wallet agrees to support
        "pay_invoice",
        "pay_keysend",
        "make_invoice",
        "lookup_invoice",
        "list_transactions",
    ],
    "relay": "wss://relay.damus.io", // Optional string, alternative relay that the wallet will use
    "lud16": "user@example.com", // Optional string, user's lightning address
}
      * 
      */
     [JsonPropertyName("secret")]
     public string Secret { get; set; }
     
     [JsonPropertyName("commands")]
     public string[] Commands { get; set; }
     [JsonPropertyName("relay")]
     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
     public string? Relay { get; set; }
     [JsonPropertyName("lud16")]
     [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
     public string? Lud16 { get; set; }
     
     
    }
    public static async Task<NostrEvent> ConstructNip67ConfirmationEvent(NIP67UriPayload payload,Nip67ConfirmationEventContent conf,ECPrivKey key)
    {
        var confirmationEvent = new NostrEvent()
        {
            Kind = EventKind,
            Content = JsonSerializer.Serialize(conf),

        }.SetTag("d", payload.Pubkey.ToHex());
        await confirmationEvent.EncryptNip04EventAsync(key, null, true);
        return confirmationEvent;
    }
    
    public static async Task<(Uri Nip47Url, Nip67ConfirmationEventContent Nip67Payload)> Nip47FromNip67Event(NostrEvent e, ECPrivKey key, Uri? relay = null)
    {
        if(e.Kind != EventKind)
            throw new ArgumentException("Invalid event kind", nameof(e));

        var content = await e.DecryptNip04EventAsync(key, null, true);
        
        var payload = JsonSerializer.Deserialize<Nip67ConfirmationEventContent>(content);
        relay ??= new Uri(payload.Relay ?? throw new ArgumentException("Relay is required"));
        
        return (NIP47.CreateUri(e.GetPublicKey(), key,relay, lud16:payload.Lud16), payload);
    }
    
    
}