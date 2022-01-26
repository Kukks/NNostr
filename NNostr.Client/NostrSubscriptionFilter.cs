using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NNostr.Client;
using Relay.Data;

namespace Relay
{
    public class NostrSubscriptionFilter
    {
        [JsonPropertyName("ids")] public string[]? Ids { get; set; }
        [JsonPropertyName("authors")] public string[]? Authors { get; set; }
        [JsonPropertyName("kinds")] public int[]? Kinds { get; set; }
        [JsonPropertyName("#e")] public string[]? EventId { get; set; }
        [JsonPropertyName("#p")] public string[]? PublicKey { get; set; }
        [JsonPropertyName("since")][JsonConverter(typeof(UnixTimestampSecondsJsonConverter))] public DateTimeOffset? Since { get; set; }
        [JsonPropertyName("until")][JsonConverter(typeof(UnixTimestampSecondsJsonConverter))] public DateTimeOffset? Until { get; set; }
        
        [JsonExtensionDataAttribute]
        public IDictionary<string, JsonElement> ExtensionData { get; set; }

        public Dictionary<string, string[]> GetAdditionalTagFilters()
        {
            var tagFilters = ExtensionData.Where(pair => pair.Key.StartsWith("#") && pair.Value.ValueKind == JsonValueKind.Array);
            return tagFilters.ToDictionary(tagFilter => tagFilter.Key.Substring(1), tagFilter => tagFilter.Value.EnumerateArray().ToEnumerable().Select(element => element.GetString()).ToArray()!)!;
        }

    }
}