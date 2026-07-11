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

        [JsonPropertyName("air_status")]
        public string? AirStatus { get; set; }

        [JsonPropertyName("ground_temperature")]
        public double? GroundTemperature { get; set; }

        [JsonPropertyName("top_temperature")]
        public double? TopTemperature { get; set; }

        [JsonPropertyName("ground_humidity")]
        public double? GroundHumidity { get; set; }

        [JsonPropertyName("top_humidity")]
        public double? TopHumidity { get; set; }

        [JsonPropertyName("soil_moisture")]
        public double? SoilMoisture { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("device_name")]
        public string? DeviceName { get; set; }
    }
}
