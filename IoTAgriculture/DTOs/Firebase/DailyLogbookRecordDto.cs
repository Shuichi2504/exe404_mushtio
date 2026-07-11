using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class DailyLogbookRecordDto
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("localTime")]
        public string LocalTime { get; set; } = string.Empty;

        [JsonPropertyName("deviceKey")]
        public string DeviceKey { get; set; } = string.Empty;

        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("humidity")]
        public double? Humidity { get; set; }

        [JsonPropertyName("air_quality")]
        public double? AirQuality { get; set; }

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

        [JsonIgnore]
        public bool HasValue =>
            Temperature != null ||
            Humidity != null ||
            AirQuality != null ||
            GroundTemperature != null ||
            TopTemperature != null ||
            GroundHumidity != null ||
            TopHumidity != null ||
            SoilMoisture != null;
    }
}
