using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NNostr.Client.JsonConverters;

namespace NNostr.Client
{
    [JsonConverter(typeof(NostrEventTagJsonConverter))]
    public class NostrEventTag
    {
        private static JsonSerializerOptions _unsafeJsonEscapingOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public string EventId { get; set; }
        public string TagIdentifier { get; set; }
        public List<string> Data { get; set; } = new();
        [JsonIgnore] public NostrEvent Event { get; set; }

        public override string ToString()
        {
            var d = TagIdentifier is null ? Data : Data.Prepend(TagIdentifier);
            return JsonSerializer.Serialize(d, _unsafeJsonEscapingOptions);
        }
    }
}