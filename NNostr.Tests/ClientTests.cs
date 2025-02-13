using System;
using NBitcoin.Secp256k1;
using NNostr.Client;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NNostr.Client.Protocols;
using Xunit;

namespace NNostr.Tests;


public class NostrWalletCOnnectTests
{
    public NostrWalletCOnnectTests()
    {

    }


    [Fact]
    public async Task RsspectCancelltaion()
    {
        var broken =
            "nostr+walletconnect://142cc22d6c00258709ae2b5312c54d40420b0aa495839ce5f13ae0025f1263db?relay=wss://relay.getalby.com/v1&secret=e06cffe97a30701d309d8c041ef67e2b37f1524c128ce594501f21cd723350d8";
        
        var result = NIP47.ParseUri(new Uri(broken));

        var pool = new NostrClientPool();
        
        var cts = new CancellationTokenSource();
        var connectionResult = pool.GetClientAndConnect(result.relays, cts.Token);
   
       var res =  await connectionResult;
       Assert.Equal(1, Assert.IsType<NostrClientPool.UsageDisposable>(res.Item2).ClientWrapper.UsageCount);
       
       var connectionResult2 = await pool.GetClientAndConnect(result.relays, cts.Token);

       Assert.Equal(2, Assert.IsType<NostrClientPool.UsageDisposable>(connectionResult2.Item2).ClientWrapper.UsageCount);
       Assert.Equal(res.Item1, connectionResult2.Item1);
       
       
       
    }
    

     // [Fact]
    public async Task CanParseUri()
    {
        
        var nwa = "nostr+walletauth://58c79c9e299fc2f8f153774c88a7be24b65c59d65ed1bca4525c58c9c656c793?relay=wss%3A%2F%2Frelay.mutinywallet.com%2F&secret=96f6569301468799&required_commands=pay_invoice&identity=71bfa9cbf84110de617e959021b08c69524fcaa1033ffd062abd0ae2657ba24c";
        var nwaPayload = NIP67.ParseUri(new Uri(nwa));
        
        
        
        var uri = new Uri("");
        var result = NIP47.ParseUri(uri);

        CompositeNostrClient client = new CompositeNostrClient(result.relays);
        await client.ConnectAndWaitUntilConnected();
        var commands = await client.FetchNIP47AvailableCommands(result.pubkey);

        var infoResponse = await client.SendNIP47Request<NIP47.GetInfoResponse>(result.pubkey, result.secret, new NIP47.GetInfoRequest());
        
        Assert.NotNull(infoResponse);

        var txs = await client.SendNIP47Request<NIP47.ListTransactionsResponse>(result.pubkey, result.secret,
            new NIP47.ListTransactionsRequest());
        var balance = await client.SendNIP47Request<NIP47.GetBalanceResponse>(result.pubkey, result.secret, new NIP47.NIP47Request("get_balance"));

        var invoice = await client.SendNIP47Request<NIP47.Nip47Transaction>(result.pubkey, result.secret, new NIP47.MakeInvoiceRequest()
        {
            AmountMsats = 1000,
            Description = "test",
            DescriptionHash = null,
            ExpirySeconds = 1000
        });

        var invoiceResponse = await client.SendNIP47Request<NIP47.Nip47Transaction>(result.pubkey, result.secret, new NIP47.LookupInvoiceRequest()
        {
            PaymentHash = invoice.PaymentHash
        });
        
        var invoiceResponse2 = await client.SendNIP47Request<NIP47.Nip47Transaction>(result.pubkey, result.secret, new NIP47.LookupInvoiceRequest()
        {
            PaymentHash = "dummy"
        });

        
        var x = false;
        
        
        

    }
}

public class ClientTests
    {
        public static (ECPrivKey PrivateKey, string PrivateKeyHex, ECXOnlyPubKey PublicKey, string PublicKeyHex)
            CreateUser(
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
    }