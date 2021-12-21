using System;
using System.Text.Json.Serialization;
using Relay.Data;

namespace Relay
{
    public class NostrSubscriptionFilter
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("authors")] public string[] Authors { get; set; }
        [JsonPropertyName("kind")] public int? Kind { get; set; }
        [JsonPropertyName("#e")] public string? EventId { get; set; }
        [JsonPropertyName("#p")] public string? PublicKey { get; set; }
        [JsonPropertyName("since")][JsonConverter(typeof(UnixTimestampSecondsJsonConverter))] public DateTimeOffset? Since { get; set; }

    }
}