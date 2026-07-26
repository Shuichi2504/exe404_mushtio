using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class PumpStateDto
    {
        [JsonPropertyName("relay1")]
        public bool? Relay1 { get; set; }

        [JsonPropertyName("relay2")]
        public bool? Relay2 { get; set; }

        [JsonPropertyName("timestamp")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string? Timestamp { get; set; }

        [JsonPropertyName("lastActionAt")]
        public string? LastActionAt { get; set; }

        [JsonPropertyName("lastActionLocal")]
        public string? LastActionLocal { get; set; }

        [JsonPropertyName("lastActionSource")]
        public string? LastActionSource { get; set; }

        [JsonPropertyName("lastActionReason")]
        public string? LastActionReason { get; set; }

        [JsonPropertyName("engineStatus")]
        public string? EngineStatus { get; set; }

        [JsonPropertyName("engineLastCheckedAt")]
        public string? EngineLastCheckedAt { get; set; }

        [JsonPropertyName("engineLastCheckedLocal")]
        public string? EngineLastCheckedLocal { get; set; }

        [JsonPropertyName("engineMessage")]
        public string? EngineMessage { get; set; }

        [JsonPropertyName("engineVersion")]
        public string? EngineVersion { get; set; }
    }
}
