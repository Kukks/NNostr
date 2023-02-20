using System.Text.Json.Serialization;

namespace NNostr.Client.Protocols;

public class NIP05Response
{
    [JsonPropertyName("names")]
    public Dictionary<string, string> Names { get; set; }

    [JsonPropertyName("relays")]
    public Dictionary<string, List<string>> Relays { get; set; }
}