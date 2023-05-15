using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using NBitcoin;
using NBitcoin.Secp256k1;
using NNostr.Client;
using Relay;
using Xunit;

namespace NNostr.Tests;

public class RelayTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Relay.Program> _factory;

    public RelayTests(WebApplicationFactory<Relay.Program> factory)
    {
        _factory = factory;
    }

    public class TestServerClient : NostrClient
    {
        private readonly TestServer _server;

        public TestServerClient(TestServer server, Uri relay, Action<WebSocket>? websocketConfigure = null) : base(relay, websocketConfigure)
        {
            _server = server;
        }

        protected override Task<WebSocket> Connect()
        {
            var r = _server.CreateWebSocketClient();
            return r.ConnectAsync(_relay, _cts.Token);
        }
    }

    [Fact]
    public async Task CanUseReplaceableEvents()
    {
        var uri = new Uri("wss://localhost:5001");
        var client = new TestServerClient(_factory.Server, uri);
        await  client.Connect();
        var p = ECPrivKey.Create(RandomUtils.GetBytes(32));
        var pub = p.CreateXOnlyPubKey().ToHex();
        var replaceableEvent= new NostrEvent()
        {
            Kind = 15000,
            Content = $"testing NNostr replaceable",
        };
        
        await replaceableEvent.ComputeIdAndSignAsync(p);
        
        await client.SendEventsAndWaitUntilReceived(new []{replaceableEvent}, CancellationToken.None);
        Assert.Equal(1, await client.SubscribeForEvents(new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new []{15000},
                Authors = new []{pub}
            }
        }, true, CancellationToken.None).CountAsync());
        
        replaceableEvent= new NostrEvent()
        {
            Kind = 15000,
            Content = $"testing NNostr replaceable2",
        };
        
        await client.SendEventsAndWaitUntilReceived(new []{replaceableEvent}, CancellationToken.None);
        Assert.Equal("testing NNostr replaceable2", (await client.SubscribeForEvents(new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new []{15000},
                Authors = new []{pub}
            }
        }, true, CancellationToken.None).SingleAsync()).Content);

    }
}