using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Relay.JsonConverters;

namespace Relay.Data
{
    [JsonConverter(typeof(NostrEventTagJsonConverter))]
    public class NostrEventTag
    {
        public string Id { get; set; }
        public string EventId { get; set; }
        public string TagIdentifier { get; set; }
        public List<string> Data { get; set; }

        public NostrEvent Event { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(Data.Prepend(TagIdentifier));
        }
    }
}