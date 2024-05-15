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
        Assert.True(nostrEvent.Verify());
        Assert.True(y.Verify());

        var newx  = JsonSerializer.Deserialize<NostrEvent>(@"{
  ""id"": ""70603616fd393ccc7cc5b8fd1eed1dd1f4d540401632a9d87c3a31c6eb36269f"",
  ""pubkey"": ""a4199ba85c577c36fa39b32fb401b6433420648793a742793cff2d110611b791"",
  ""created_at"": 1715767998,
  ""kind"": 1,
  ""tags"": [],
  ""content"": ""{\""RoundStates\"":[],\""CoinJoinFeeRateMedians\"":[],\""AffiliateInformation\"":{\""RunningAffiliateServers\"":[],\""AffiliateData\"":{}}}"",
  ""sig"": ""9115ccdc90f55307109b5f1650113212822b5ac78b641dae67f507a6c4d334d1ea13779f0b2f2e2c03abb8ef2489d165c4933180c1858b2563af330709788764""
}");
        
        Assert.True(newx.Verify());



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