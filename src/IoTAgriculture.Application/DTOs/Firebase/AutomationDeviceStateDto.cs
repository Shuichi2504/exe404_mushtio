using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class AutomationDeviceStateDto : SensorStateDto
    {
        [JsonPropertyName("relay1")]
        public bool? Relay1 { get; set; }

        [JsonPropertyName("relay2")]
        public bool? Relay2 { get; set; }

        [JsonPropertyName("schedule")]
        public AutoIrrigationScheduleDto? Schedule { get; set; }
    }
}
