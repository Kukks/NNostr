using System.Text.Json.Serialization;
using NNostr.Client.JsonConverters;

namespace NNostr.Client;

public abstract  class BaseNostrEvent<TEventTag> where TEventTag: NostrEventTag
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("pubkey")]
    public string PublicKey { get; set; }
    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(UnixTimestampSecondsJsonConverter))]
    public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("kind")]
    public int Kind { get; set; }
    [JsonPropertyName("content")]
    [JsonConverter(typeof(StringEscaperJsonConverter))]
    public string? Content { get; set; }

    [JsonPropertyName("tags")] public List<TEventTag> Tags { get; set; } = new();
        
    [JsonPropertyName("sig")]
    public string Signature { get; set; }
        
}