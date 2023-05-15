using System;
using NBitcoin.Secp256k1;
using NNostr.Client;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using NBitcoin;
using Relay;
using Xunit;
using Program = Microsoft.VisualStudio.TestPlatform.TestHost.Program;

namespace NNostr.Tests;

public class ClientTests
{
    private (ECPrivKey PrivateKey, string PrivateKeyHex, ECXOnlyPubKey PublicKey, string PublicKeyHex) CreateUser(
        string privKeyHex)
    {
        Assert.True(Context.Instance.TryCreateECPrivKey(Convert.FromHexString(privKeyHex), out var privKey));
        Debug.Assert(privKey != null, nameof(privKey) + " != null");
        return (privKey, privKeyHex,
            privKey.CreateXOnlyPubKey(), privKey.CreateXOnlyPubKey().ToBytes().AsSpan().ToHex());
    }

    [Fact]
    public async Task CanHandleNIP04()
    {
        var user1 = CreateUser("7f4c11a9742721d66e40e321ca50b682c27f7422190c14a187525e69e604836a");
        var user2 = CreateUser("203b892f1d671fec43a04b36c452de631c9cf55b7a93b75d97ff1e41d217f038");
        var evtFromUser1ToUser2 = new NostrEvent()
        {
            Content = "test",
            Kind = 4,
            Tags = new List<NostrEventTag>()
            {
                new()
                {
                    TagIdentifier = "p",
                    Data = new List<string>()
                    {
                        user2.PublicKeyHex
                    }
                }
            }
        };

        await evtFromUser1ToUser2.EncryptNip04EventAsync(user1.PrivateKey);
        Assert.Equal("test", await evtFromUser1ToUser2.DecryptNip04EventAsync(user2.PrivateKey));
        Assert.Equal("test", await evtFromUser1ToUser2.DecryptNip04EventAsync(user1.PrivateKey));
    }

    [Fact]
    public void CanHandlePrivatePublicKeyFormats()
    {
        var privKeyHex = "7f4c11a9742721d66e40e321ca50b682c27f7422190c14a187525e69e604836a";
        
        Assert.True(Context.Instance.TryCreateECPrivKey(Convert.FromHexString(privKeyHex), out var privKey));
        Debug.Assert(privKey != null, nameof(privKey) + " != null");
        var pubKey = privKey.CreateXOnlyPubKey();
        Assert.Equal("7cef86754ddf07395c289c30fe31219de938c6d707d6b478a8682fc75795e8b9",
            pubKey.ToBytes().AsSpan().ToHex());
    }

    [Fact]
    public async Task CanUseClient()
    {
        var uri = new Uri("wss://nostr.btcmp.com");
        var client = new NostrClient(uri);
        _ = client.Connect();
        await client.WaitUntilConnected(CancellationToken.None);
        var k = ECPrivKey.Create(RandomUtils.GetBytes(32));
        var khex = k.ToHex();
        var user1 = CreateUser(khex);

        var evt = new NostrEvent()
        {
            Kind = 1,
            Content = "testing NNostr",
        };
        await evt.ComputeIdAndSignAsync(user1.PrivateKey);
        await client.SendEventsAndWaitUntilReceived(new[] {evt}, CancellationToken.None);
        
    }
    
    [Fact]
    public async Task CanUseClient2()
    {
        var uri = new Uri("wss://localhost:5001");
        var client = new NostrClient(uri);
        await  client.Connect();
        var k = ECPrivKey.Create(RandomUtils.GetBytes(32));
        var khex = k.ToHex();
        var user1 = CreateUser(khex);
        var evts = new List<NostrEvent>();
        for (int i = 0; i < 2; i++)
        {
            var evt = new NostrEvent()
            {
                Kind = 1,
                Content = $"testing NNostr {i}",
            };
            await evt.ComputeIdAndSignAsync(user1.PrivateKey);
            evts.Add(evt);
        }
        
        await client.SendEventsAndWaitUntilReceived(evts.ToArray(), CancellationToken.None);
        var subscription = new NostrSubscriptionFilter()
        {
            Ids = evts.Select(e => e.Id).ToArray()
        };

        var counter = await client.SubscribeForEvents(new[] {subscription}, true, CancellationToken.None).CountAsync();

        Assert.Equal(2, counter);
        
       
    }
}