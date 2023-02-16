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
        public string TagIdentifier { get; set; }
        public List<string> Data { get; set; } = new();

        public override string ToString()
        {
            var d = TagIdentifier is null ? Data : Data.Prepend(TagIdentifier);
            return JsonSerializer.Serialize(d, _unsafeJsonEscapingOptions);
        }
        
        public bool Equals(NostrEventTag? x, NostrEventTag? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.TagIdentifier == y.TagIdentifier && x.Data.SequenceEqual(y.Data);
        }

        public int GetHashCode(NostrEventTag obj)
        {
            return HashCode.Combine(obj.TagIdentifier, obj.Data);
        }
    }
}