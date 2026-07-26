using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    /// <summary>
    /// Firebase firmware payloads may publish timestamp as either a JSON number
    /// or a JSON string. Normalize both representations without rejecting the
    /// entire devices snapshot.
    /// </summary>
    public sealed class FlexibleStringJsonConverter : JsonConverter<string?>
    {
        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => ReadNumber(ref reader),
                JsonTokenType.True => bool.TrueString,
                JsonTokenType.False => bool.FalseString,
                _ => throw new JsonException(
                    $"Cannot convert JSON token {reader.TokenType} to string.")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            string? value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStringValue(value);
        }

        private static string ReadNumber(ref Utf8JsonReader reader)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.GetRawText();
        }
    }
}
