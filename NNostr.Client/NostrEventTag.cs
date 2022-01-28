using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Relay.JsonConverters;

namespace NNostr.Client
{
    [JsonConverter(typeof(NostrEventTagJsonConverter))]
    public class NostrEventTag
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }
        public string EventId { get; set; }
        public string TagIdentifier { get; set; }
        public List<string> Data { get; set; } = new();
[JsonIgnore]
        public NostrEvent Event { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(Data.Prepend(TagIdentifier));
        }
    }
}