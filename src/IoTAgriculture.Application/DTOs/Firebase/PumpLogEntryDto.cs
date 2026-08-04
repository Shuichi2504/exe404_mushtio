using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class PumpLogEntryDto
    {
        [JsonPropertyName("pumpKey")]
        public string PumpKey { get; set; } = string.Empty;

        [JsonPropertyName("relayKey")]
        public string RelayKey { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public bool Value { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "manual";

        [JsonPropertyName("actorUserId")]
        public string? ActorUserId { get; set; }

        [JsonPropertyName("actorName")]
        public string ActorName { get; set; } = "System";

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("intervalMinutes")]
        public int? IntervalMinutes { get; set; }

        [JsonPropertyName("sensorKey")]
        public string? SensorKey { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("humidity")]
        public double? Humidity { get; set; }

        [JsonPropertyName("temperatureThreshold")]
        public decimal? TemperatureThreshold { get; set; }

        [JsonPropertyName("humidityThreshold")]
        public int? HumidityThreshold { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("utcTime")]
        public string UtcTime { get; set; } = string.Empty;

        [JsonPropertyName("localTime")]
        public string LocalTime { get; set; } = string.Empty;
    }
}
