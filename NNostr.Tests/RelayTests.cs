using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.Secp256k1;
using NNostr.Client;
using Relay;
using Xunit;

namespace NNostr.Tests;

public class RelayTestServer : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _config;

    public RelayTestServer(Dictionary<string, string>? config = null)
    {
        _config = config ?? new();
    }

    public RelayTestServer(bool newDb = false, Dictionary<string, string>? config = null)
    {
        _config = config ?? new();
        _config.AddOrReplace("CONNECTIONSTRINGS:RelayDatabase",
            $"User ID=postgres;Host=127.0.0.1;Port=65466;Database=relaytest-{Guid.NewGuid()};persistsecurityinfo=True");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(configurationBuilder => configurationBuilder.AddInMemoryCollection(_config));
    }
}

public class RelayTests
{
    public RelayTests()
    {
    }

    public class TestServerClient : NostrClient
    {
        private readonly TestServer _server;

        public TestServerClient(TestServer server) : base(new Uri("wss://lol.com"), null)
        {
            _server = server;
        }

        protected override Task<WebSocket> Connect()
        {
            var r = _server.CreateWebSocketClient();
            return r.ConnectAsync(_server.BaseAddress, _cts.Token);
        }
    }

    [Fact]
    public async Task CanUseBasics()
    {
        await using var server = new RelayTestServer(true);
        var client = new TestServerClient(server.Server);
        await client.Connect();
        var p = ECPrivKey.Create(RandomUtils.GetBytes(32));


        var event1 = new NostrEvent()
        {
            Kind = 1,
            Content = $"testing NNostr",
        };

        await event1.ComputeIdAndSignAsync(p);
        var tcseose = new TaskCompletionSource<string>();
        client.MessageReceived+= (sender, s) =>
        {
            Console.WriteLine("sdsd");
            
        };
        client.EoseReceived += (sender, s) =>
        {
            tcseose.TrySetResult(s);
        };
        
        await client.Connect();


        await client.CreateSubscription("TESTX", new[]
        {
            new NostrSubscriptionFilter()
            {
                Authors = new[] {event1.PublicKey}
            }
        }, CancellationToken.None);
        
        var evts =  client.SubscribeForEvents(new []{ new NostrSubscriptionFilter()
        {
            Authors = new []{event1.PublicKey}
        }}, true, CancellationToken.None);
        
        
        await client.SendEventsAndWaitUntilReceived(new[] {event1}, CancellationToken.None);
        
        Assert.Equal("TESTX", await tcseose.Task);
        var matched = await evts.SingleAsync();
        Assert.Equal(matched.Id, event1.Id);

    }

    [Fact]
    public async Task CanUseReplaceableEvents()
    {
        await using var server = new RelayTestServer(true);
        var client = new TestServerClient(server.Server);
        await client.Connect();
        var p = ECPrivKey.Create(RandomUtils.GetBytes(32));
        var pub = p.CreateXOnlyPubKey().ToHex();
        var replaceableEvent = new NostrEvent()
        {
            Kind = 15000,
            Content = $"testing NNostr replaceable",
        };

        await replaceableEvent.ComputeIdAndSignAsync(p);

        await client.SendEventsAndWaitUntilReceived(new[] {replaceableEvent}, CancellationToken.None);
        Assert.Equal(1, await client.SubscribeForEvents(new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new[] {15000},
                Authors = new[] {pub}
            }
        }, true, CancellationToken.None).CountAsync());

        replaceableEvent = new NostrEvent()
        {
            Kind = 15000,
            Content = $"testing NNostr replaceable2",
        };

        await replaceableEvent.ComputeIdAndSignAsync(p);
        await client.SendEventsAndWaitUntilReceived(new[] {replaceableEvent}, CancellationToken.None);
        Assert.Equal("testing NNostr replaceable2", (await client.SubscribeForEvents(new[]
        {
            new NostrSubscriptionFilter()
            {
                Kinds = new[] {15000},
                Authors = new[] {pub}
            }
        }, true, CancellationToken.None).SingleAsync()).Content);
    }
}