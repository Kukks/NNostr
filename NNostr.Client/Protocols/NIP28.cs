using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin.Secp256k1;

namespace NNostr.Client.Protocols;

public static class NIP28
{
    public static NostrSubscriptionFilter[] CreateChannelFilter(
        string channelId, 
        string channelCreator, 
        string[]? ownPubkeys, 
        bool includeAllChannelUpdates = false, 
        bool getGlobalMutes = false,
        bool getGlobalHides = false)
    {
        var result = new List<NostrSubscriptionFilter>();
        result.Add(new NostrSubscriptionFilter()
        {
            Kinds = new []{40},
            Ids = new []{channelId}
        });
        result.Add(new NostrSubscriptionFilter()
        {
            Kinds = new []{41},
            Limit = includeAllChannelUpdates ? (int?)null : 1,
            ReferencedEventIds = new []{channelId},
            Authors = new []{channelCreator}
        });
        
        if (getGlobalMutes || ownPubkeys is not null)
        {
            result.Add(new NostrSubscriptionFilter()
            {
                Kinds = new[] {44},
                Authors = getGlobalMutes ? null : ownPubkeys
            });
        }

        if (getGlobalHides || ownPubkeys is not null)
        {
            result.Add(new NostrSubscriptionFilter()
            {
                Kinds = new[] {43},
                Authors = getGlobalHides ? null : ownPubkeys
            });
        }

        result.Add(new NostrSubscriptionFilter()
        {
            Kinds = new[] {42},
            ReferencedEventIds = new []{channelId},
        });
        return result.ToArray();
    }


    public static async Task<NostrEvent> CreateChannelEvent(ChannelContent channelContent, ECPrivKey? key = null)
    {
        var r = new NostrEvent()
        {
            Kind = 40,
            Content = JsonSerializer.Serialize(channelContent)
        };
        if (key is not null)
            return await r.ComputeIdAndSignAsync(key);
        return r; 
    }

    public static async Task<NostrEvent> UpdateChannelEvent(ChannelContent channelContent, string channelId,
        string? recommendedRelay = null, ECPrivKey? key = null)
    {
        var r = new NostrEvent()
        {
            Kind = 41,
            Content = JsonSerializer.Serialize(channelContent)
        };
        r.SetTag("e", recommendedRelay is null ? new[] {channelId} : new[] {channelId, recommendedRelay});
        
        if (key is not null)
            return await r.ComputeIdAndSignAsync(key);
        return r; 
    }

    public static async Task<NostrEvent> MuteUserEvent(ReasonContent content, string pubkey, ECPrivKey? key = null)
    {
        var r = new NostrEvent()
        {
            Kind = 44,
            Content = JsonSerializer.Serialize(content)
        };
        r.SetTag("p", pubkey);
        if (key is not null)
            return await r.ComputeIdAndSignAsync(key);
        return r; 
    }

    public static async Task<NostrEvent> HideMessageEvent(ReasonContent content, string eventId, ECPrivKey? key = null)
    {
        var r = new NostrEvent()
        {
            Kind = 43,
            Content = JsonSerializer.Serialize(content)
        };
        r.SetTag("e", eventId);
        if (key is not null)
            return await r.ComputeIdAndSignAsync(key);
        return r; 
    }

    public class ChannelContent
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("about")]
        public string About { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("picture")]
        public string Picture { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public class ReasonContent
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }
    }
}