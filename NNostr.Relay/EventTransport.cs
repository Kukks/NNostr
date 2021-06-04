using System;
using System.Text.Json.Serialization;

namespace NNostr.Relay
{
    public class EventTransport
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("pubkey")]
        public string PublicKey { get; set; }
        [JsonPropertyName("created_at")] 
        public DateTimeOffset? Since { get; set; }
        [JsonPropertyName("kind")] 
        public int Kind { get; set; }
        [JsonPropertyName("tags")] 
        public string[][] Tags { get; set; }
        [JsonPropertyName("content")]
        public string Content { get; set; }
        [JsonPropertyName("sig")]
        public string Signature { get; set; }
    }
}