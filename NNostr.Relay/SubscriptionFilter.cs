using System;
using System.Linq;
using System.Text.Json.Serialization;
using NNostr.Relay.Data;

namespace NNostr.Relay
{
    public class SubscriptionFilter
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("authors")] public string[] Authors { get; set; }
        [JsonPropertyName("kind")] public int? Kind { get; set; }
        [JsonPropertyName("#e")] public string? EventId { get; set; }
        [JsonPropertyName("#p")] public string? PublicKey { get; set; }
        [JsonPropertyName("since")] public DateTimeOffset? Since { get; set; }

        public IQueryable<Event> Filter(IQueryable<Event> events)
        {
            var result = events;
            if (!string.IsNullOrEmpty(Id))
            {
                result = result.Where(e => e.Id == Id);
            }

            if (Kind != null)
            {
                result = result.Where(e => e.Kind == Kind);
            }

            if (Since != null)
            {
                result = result.Where(e => e.CreatedAt > Since);
            }

            if (!string.IsNullOrEmpty(Author))
            {
                result = result.Where(e => e.PublicKey == Author);
            }

            var authors = Authors?.Where(s => !string.IsNullOrEmpty(s))?.ToArray();
            if (authors?.Any() is true)
            {
                result = result.Where(e => authors.Contains(e.PublicKey));
            }

            if (!string.IsNullOrEmpty(EventId))
            {
                result = result.Where(e =>
                    e.Tags.Any(tag => tag.Tags.Count > 1 && tag.Tags[0] == "e" && tag.Tags[1] == EventId));
            }

            if (!string.IsNullOrEmpty(PublicKey))
            {
                result = result.Where(e =>
                    e.Tags.Any(tag => tag.Tags.Count > 1 && tag.Tags[0] == "p" && tag.Tags[1] == PublicKey));
            }

            return result;

        }
    }
}