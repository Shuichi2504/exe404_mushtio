using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class SensorStateDto
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("humidity")]
        public double? Humidity { get; set; }

        [JsonPropertyName("air_quality")]
        public double? AirQuality { get; set; }

        [JsonPropertyName("air_quality_status")]
        public string AirQualityStatus { get; set; } = "Chưa có dữ liệu";

        [JsonPropertyName("air_quality_level")]
        public string AirQualityLevel { get; set; } = "muted";

        [JsonPropertyName("air_quality_should_alert")]
        public bool AirQualityShouldAlert { get; set; }

        [JsonPropertyName("air_status")]
        public string? AirStatus { get; set; }

        [JsonPropertyName("ground_temperature")]
        public double? GroundTemperature { get; set; }

        [JsonPropertyName("top_temperature")]
        public double? TopTemperature { get; set; }

        [JsonPropertyName("timestamp")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string? Timestamp { get; set; }

        [JsonPropertyName("device_name")]
        public string? DeviceName { get; set; }
    }
}
