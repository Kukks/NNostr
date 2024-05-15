using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Secp256k1;
using NNostr.Client;
using NNostr.Client.Protocols;
using Relay;
using Xunit;

namespace NNostr.Tests;

public class EqualityTests
{
    [Fact]

    public async Task  SerializeTest()
    {
        var content =
            "{\"RoundStates\":[],\"CoinJoinFeeRateMedians\":[],\"AffiliateInformation\":{\"RunningAffiliateServers\":[],\"AffiliateData\":{}}}";
        var nostrEvent = new NostrEvent
        {
            Kind = 1,
            Content = content,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1715753690)
        };
        var key = "nsec198vkwdzprv5wpzka7q2hn23x205j759dquevsrtdc8y5e23r8ags6mnkwk".FromNIP19Nsec();
        nostrEvent = await nostrEvent.ComputeIdAndSignAsync(key);
        
        Assert.NotNull(nostrEvent);
        Assert.Equal("33b45956ea31ca61928e190227c66777e1f8c79a7551f6af1508c54a5534b5b8", nostrEvent.PublicKey);
        Assert.Equal("eb65725ada2acda2004e87cbdd8319a1289aabde1230f1985887e8edb16f2444", nostrEvent.Id);
        
        var x = JsonSerializer.Serialize(nostrEvent);
        var y = JsonSerializer.Deserialize<NostrEvent>(x);
        Assert.NotNull(y);
        Assert.Equal(nostrEvent.Content, y.Content);

    }
    
    
    [Fact]
    public void EqualityWorksBetweenEventBaseClasses()
    {
        var nostrEvent = new NostrEvent()
        {
            Content = "test",
            Kind = 1,
            Tags = new List<NostrEventTag>()
            {
                new()
                {
                    TagIdentifier = "p",
                    Data = new List<string>()
                    {
                        "test"
                    }
                },
                new()
                {
                    TagIdentifier = "something",
                    Data = new List<string>()
                    {
                        "test2"
                    }
                }
            }
        };
        var relayNostrEvent = new RelayNostrEvent()
        {
            Content = "test",
            Kind = 1,
            Tags = new List<RelayNostrEventTag>()
            {
                new()
                {
                    TagIdentifier = "p",
                    Data = new List<string>()
                    {
                        "test"
                    }
                },
                new()
                {
                    TagIdentifier = "something",
                    Data = new List<string>()
                    {
                        "test2"
                    }
                }
            }
        };


        var nEventStr = JsonSerializer.Serialize(nostrEvent);
        var rEventStr = JsonSerializer.Serialize(relayNostrEvent);
        Assert.Equal(nEventStr, rEventStr);
        var nEvent = JsonSerializer.Deserialize<NostrEvent>(nEventStr);
        var rEvent = JsonSerializer.Deserialize<NostrEvent>(rEventStr);

        nEventStr = JsonSerializer.Serialize(nostrEvent);
        rEventStr = JsonSerializer.Serialize(relayNostrEvent);
        Assert.Equal(nEventStr, rEventStr);
    }
}