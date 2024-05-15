using System.Text.Json;
using System.Text.Json.Serialization;

namespace NNostr.Client.JsonConverters
{
    public class UnixTimestampSecondsJsonConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("datetime was not in number format");
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64());
            }
            catch (Exception e)
            {
                return null;
            }
            
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteNumberValue(value.Value.ToUnixTimeSeconds());
            }
        }
    }
}