using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTAgriculture.Serialization
{
    /// <summary>
    /// Keeps the API contract explicit when SQL Server returns UTC datetime2
    /// values with DateTimeKind.Unspecified.
    /// </summary>
    public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var value = DateTime.Parse(
                reader.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            return EnsureUtc(value);
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTime value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(EnsureUtc(value));
        }

        public static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
