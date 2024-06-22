using System.Text.Json.Serialization;
using NNostr.Client.JsonConverters;

namespace NNostr.Client
{
    [JsonConverter(typeof(NostrEventTagJsonConverter))]
    public class NostrEventTag
    {
        public string TagIdentifier { get; set; }
        public List<string> Data { get; set; } = new();

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