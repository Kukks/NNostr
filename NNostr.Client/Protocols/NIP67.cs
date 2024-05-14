using System.Web;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP67
{
    public static int EventKind = 33194;
    public record NIP67UriPayload(
        ECXOnlyPubKey Pubkey,
        ECPrivKey Secret,
        string[] Relays,
        string[] RequiredCommands,
        string[] OptionalCommands,
        string? Budget,
        string? Identity)
    {
        public override string ToString()
        {
            var result =
                $"nostr+walletauth://{Pubkey.ToHex()}?relay={string.Join("&relay=", Relays)}&secret={Secret.CreateXOnlyPubKey().ToHex()}&required_commands={string.Join(" ", RequiredCommands)}";
            
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
        var query = HttpUtility.ParseQueryString(uri.Query);
        
        var relays = query.GetValues("relay") ?? Array.Empty<string>();
        var secret = NostrExtensions.ParseKey(query["secret"]);
        var requiredCommands = query.GetValues("required_commands")?.SelectMany(s => s.Split(" ")).Distinct().ToArray() ?? Array.Empty<string>();
        var optionalCommands = query.GetValues("optional_commands")?.SelectMany(s => s.Split(" ")).Distinct().ToArray() ?? Array.Empty<string>();
        var budget = query.GetValues("budget")?.FirstOrDefault();
        var identity = query.GetValues("identity")?.FirstOrDefault();
        
        return new NIP67UriPayload(NostrExtensions.ParsePubKey(uri.Host), secret, relays, requiredCommands, optionalCommands, budget, identity);
    }
}