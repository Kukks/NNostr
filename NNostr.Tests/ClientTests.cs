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

     // [Fact]
    public async Task CanParseUri()
    {
        
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