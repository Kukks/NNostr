using System.Text.Json.Serialization;
using NNostr.Client;

namespace Relay;

public class RelayNostrEvent:NostrEvent
{
    
        
    [JsonIgnore] public bool Deleted { get; set; }
}