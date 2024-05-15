using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NNostr.Client.JsonConverters;

public class StringEscaperJsonConverter : JsonConverter<string?>
{
    public static string JavaScriptStringEncode(string value, bool addDoubleQuotes)
    {
        if (string.IsNullOrEmpty(value))
            return addDoubleQuotes ? "\"\"" : string.Empty;

        int len = value.Length;
        bool needEncode = false;
        char c;
        for (int i = 0; i < len; i++)
        {
            c = value[i];

            if (c >= 0 && c <= 31 || c == 34 || c == 39 || c == 60 || c == 62 || c == 92)
            {
                needEncode = true;
                break;
            }
        }

        if (!needEncode)
            return addDoubleQuotes ? "\"" + value + "\"" : value;

        var sb = new System.Text.StringBuilder();
        if (addDoubleQuotes)
            sb.Append('"');

        for (int i = 0; i < len; i++)
        {
            c = value[i];
            // if (c == 0)
            // {
            //     sb.AppendFormat("\\u{0:x4}", (int)c);
            // }
            // else
            // {
            //     
            //     sb.Append(c);
            // }
            if (c == 39 || c == 62 || c == 60)
            {
                sb.Append(c);
                continue;
            }

            if (c >= 0 && c <= 7 || c == 11 || c >= 14 && c <= 31 || c == 39 || c == 60 || c == 62)
                sb.AppendFormat("\\u{0:x4}", (int) c);
            else
                switch ((int) c)
                {
                    case 8:
                        sb.Append("\\b");
                        break;

                    case 9:
                        sb.Append("\\t");
                        break;

                    case 10:
                        sb.Append("\\n");
                        break;

                    case 12:
                        sb.Append("\\f");
                        break;

                    case 13:
                        sb.Append("\\r");
                        break;

                    case 34:
                        sb.Append("\\\"");
                        break;

                    case 92:
                        sb.Append("\\\\");
                        break;

                    default:
                        sb.Append(c);
                        break;
                }
        }

        if (addDoubleQuotes)
            sb.Append('"');

        return sb.ToString();
    }

public static string JavaScriptStringDecode(string encodedString, bool removeDoubleQuotes)
{
    if (string.IsNullOrEmpty(encodedString))
        return encodedString;

    if (removeDoubleQuotes && encodedString.StartsWith("\"") && encodedString.EndsWith("\""))
    {
        encodedString = encodedString.Substring(1, encodedString.Length - 2);
    }

    var sb = new StringBuilder(encodedString.Length);
    for (int i = 0; i < encodedString.Length; i++)
    {
        char c = encodedString[i];
        if (c == '\\' && i + 1 < encodedString.Length)
        {
            switch (encodedString[i + 1])
            {
                case 'b':
                    sb.Append('\b');
                    i++;
                    break;
                case 't':
                    sb.Append('\t');
                    i++;
                    break;
                case 'n':
                    sb.Append('\n');
                    i++;
                    break;
                case 'f':
                    sb.Append('\f');
                    i++;
                    break;
                case 'r':
                    sb.Append('\r');
                    i++;
                    break;
                case '"':
                    sb.Append('"');
                    i++;
                    break;
                case '\\':
                    sb.Append('\\');
                    i++;
                    break;
                case 'u':
                    if (i + 5 < encodedString.Length)
                    {
                        string hexCode = encodedString.Substring(i + 2, 4);
                        if (ushort.TryParse(hexCode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort charCode))
                        {
                            sb.Append((char)charCode);
                            i += 5;
                        }
                    }
                    break;
                default:
                    // If no known escape sequence, add the backslash as it is
                    sb.Append(c);
                    break;
            }
        }
        else
        {
            sb.Append(c);
        }
    }

    return sb.ToString();
}

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("value was not a string");
        }

        return reader.GetString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}