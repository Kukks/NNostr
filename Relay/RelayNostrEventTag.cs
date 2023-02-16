using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NNostr.Client;

namespace Relay;

[JsonConverter(typeof(RelayNostrEventTagJsonConverter))]

public class RelayNostrEventTag : NostrEventTag, IEqualityComparer<NostrEventTag>
{
    [JsonIgnore]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [JsonIgnore] public string EventId { get; set; }
    [JsonIgnore] public RelayNostrEvent Event { get; set; }


}