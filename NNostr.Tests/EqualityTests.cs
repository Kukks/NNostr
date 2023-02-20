using System.Collections.Generic;
using System.Text.Json;
using NNostr.Client;
using Relay;
using Xunit;

namespace NNostr.Tests;

public class EqualityTests
{
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