using System;
using System.Text.Json;
using System.Threading.Tasks;
using NNostr.Client;
using Xunit;
using Xunit.Abstractions;

namespace NNostr.Tests;

public class NIP57Tests
{
    private readonly ITestOutputHelper _output;

    public NIP57Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ValidateZapReceipt()
    {
        var privKeyStr = "0514ce6aae1eb9897d32ccce0cc40c6942a1f2f04f5554618dd97f430c9e386f";
        var privKey = NostrExtensions.ParseKey(privKeyStr);
        var pubKey = privKey.CreateXOnlyPubKey();
        var pubKeyHex = pubKey.ToHex();

        var evtRequestStr =
            """{"id":"cd8bb08cb5a74d67d49d73f8838057385fb8c584427629d81b54e29a1c7708bb","pubkey":"8cacc4de163d6547e740ac4338a3c4569ce4028f0299fae841a00028d68c04e3","created_at":1717408922,"kind":9734,"content":"Great post 👍","tags":[["p","23378c18cb34edc0ea5f979b41703df2799fed769595e31312fef04b8011c0d6"],["amount","21000"],["relays","wss://nostr.mom"],["e","e6dceff3a4cd834edd17c170a41146086e6a200c5f012232bc80840856ddfa27"]],"sig":"a950026e445b27c818013f97a120d1c3c21f94a0e0362379c76cad1a95131ec14071f9b4339afaca2e17144138058db77f415202b92ce52763966fdda97c219d"}""";
        var evtReceipt = new NostrEvent()
        {
            Kind = 9735,
            Content = "Great post 👍",
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1717408928),
            PublicKey = pubKeyHex,
            Tags = {
                new(){ TagIdentifier = "p", Data = { "23378c18cb34edc0ea5f979b41703df2799fed769595e31312fef04b8011c0d6" }},
                new(){ TagIdentifier = "e", Data = { "e6dceff3a4cd834edd17c170a41146086e6a200c5f012232bc80840856ddfa27" }},
                new(){ TagIdentifier = "description", Data = { evtRequestStr }},
            }
        };
        evtReceipt = await evtReceipt.ComputeIdAndSignAsync(privKey);
        
        var evtReceiptStr = JsonSerializer.Serialize(evtReceipt);

        _output.WriteLine("Receipt:");
        _output.WriteLine(evtReceiptStr);
        
        // Since we both sign and verify from the same 'ToIdPreimage', this will always succeed, even if the computation of the id is invalid
        // Use a tool like https://nak.nostr.com/ to copy/paste the event receipt string and verify if the signature is valid
        Assert.True(evtReceipt.Verify());
    }
}