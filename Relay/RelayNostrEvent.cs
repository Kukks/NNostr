using System.Text.Json.Serialization;
using NNostr.Client;

namespace Relay;

public class RelayNostrEvent : BaseNostrEvent<RelayNostrEventTag>
{
    [JsonIgnore] public bool Deleted { get; set; }
}