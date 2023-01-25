using System.Text.Json.Serialization;
using NNostr.Client.JsonConverters;

namespace NNostr.Client
{
    public class NostrEvent: IEqualityComparer<NostrEvent>
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
        public string Content { get; set; }
        
        [JsonPropertyName("tags")]
        public List<NostrEventTag> Tags { get; set; }
        
        [JsonPropertyName("sig")]
        public string Signature { get; set; }

        public bool Equals(NostrEvent? x, NostrEvent? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x?.GetType() != y?.GetType()) return false;
            return x?.Id == y?.Id;
        }

        public int GetHashCode(NostrEvent obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}