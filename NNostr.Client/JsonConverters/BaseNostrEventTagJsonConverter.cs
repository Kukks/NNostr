using System.Text.Json;
using System.Text.Json.Serialization;

namespace NNostr.Client.JsonConverters;

public abstract class BaseNostrEventTagJsonConverter<TNostrEventTag> : JsonConverter<TNostrEventTag> where TNostrEventTag:NostrEventTag, new()
{

    public override TNostrEventTag? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        TNostrEventTag result = new();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Nostr Event Tags are an array");
        }

        reader.Read();
        var i = 0;
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (i == 0)
            {
                result.TagIdentifier = StringEscaperJsonConverter.JavaScriptStringEncode(reader.GetString(), false);
            }
            else
            {
                result.Data.Add(StringEscaperJsonConverter.JavaScriptStringEncode(reader.GetString(), false));
            }

            reader.Read();
            i++;
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, TNostrEventTag value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        writer.WriteStringValue(value.TagIdentifier);
        value.Data?.ForEach(writer.WriteStringValue);

        writer.WriteEndArray();
    }
}