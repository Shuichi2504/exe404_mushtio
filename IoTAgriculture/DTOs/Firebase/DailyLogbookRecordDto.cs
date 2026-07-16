using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class DailyLogbookRecordDto
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("localTime")]
        public string LocalTime { get; set; } = string.Empty;

        [JsonPropertyName("periodStartLocal")]
        public string PeriodStartLocal { get; set; } = string.Empty;

        [JsonPropertyName("periodEndLocal")]
        public string PeriodEndLocal { get; set; } = string.Empty;

        [JsonPropertyName("deviceKey")]
        public string DeviceKey { get; set; } = string.Empty;

        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("minTemperature")]
        public double? MinTemperature { get; set; }

        [JsonPropertyName("maxTemperature")]
        public double? MaxTemperature { get; set; }

        [JsonPropertyName("humidity")]
        public double? Humidity { get; set; }
        
        [JsonPropertyName("minHumidity")]
        public double? MinHumidity { get; set; }
        
        [JsonPropertyName("maxHumidity")]
        public double? MaxHumidity { get; set; }
        
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

        [JsonPropertyName("minSoilMoisture")]
        public double? MinSoilMoisture { get; set; }

        [JsonPropertyName("maxSoilMoisture")]
        public double? MaxSoilMoisture { get; set; }

        [JsonIgnore]
public bool HasValue =>
    Temperature != null ||
    MinTemperature != null ||
    MaxTemperature != null ||
    Humidity != null ||
    MinHumidity != null ||
    MaxHumidity != null ||
    AirQuality != null ||
    GroundTemperature != null ||
    TopTemperature != null ||
    GroundHumidity != null ||
    TopHumidity != null ||
    SoilMoisture != null ||
    MinSoilMoisture != null ||
    MaxSoilMoisture != null;
            MaxSoilMoisture != null;
    }
}
