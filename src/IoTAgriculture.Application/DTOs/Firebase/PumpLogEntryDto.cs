using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class PumpStateDto
    {
        [JsonPropertyName("relay1")]
        public bool? Relay1 { get; set; }

        [JsonPropertyName("relay2")]
        public bool? Relay2 { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

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
        public string? Timestamp { get; set; }

        [JsonPropertyName("lastActionAt")]
        public string? LastActionAt { get; set; }

        [JsonPropertyName("lastActionLocal")]
        public string? LastActionLocal { get; set; }

        [JsonPropertyName("lastActionSource")]
        public string? LastActionSource { get; set; }

        [JsonPropertyName("lastActionReason")]
        public string? LastActionReason { get; set; }
    }
}
