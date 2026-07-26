using System.Text.Json.Serialization;

namespace IoTAgriculture.DTOs.Firebase
{
    public class AutoIrrigationScheduleDto
    {
        [JsonPropertyName("pumpKey")]
        public string PumpKey { get; set; } = string.Empty;

        [JsonPropertyName("relayKey")]
        public string RelayKey { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("intervalMinutes")]
        public int IntervalMinutes { get; set; }

        [JsonPropertyName("durationSeconds")]
        public int DurationSeconds { get; set; }

        // Kept for compatibility with the ESP32/device schedule schema.
        // The backend uses DurationSeconds when it is present and otherwise
        // converts this value to seconds.
        [JsonPropertyName("durationMinutes")]
        public int? DurationMinutes { get; set; }

        [JsonPropertyName("startTime")]
        public string StartTime { get; set; } = "06:00";

        [JsonPropertyName("endTime")]
        public string EndTime { get; set; } = "18:00";

        [JsonPropertyName("startHour")]
        public int? StartHour { get; set; }

        [JsonPropertyName("endHour")]
        public int? EndHour { get; set; }

        [JsonPropertyName("smartEnabled")]
        public bool SmartEnabled { get; set; }

        [JsonPropertyName("sensorKey")]
        public string? SensorKey { get; set; }

        [JsonPropertyName("soilMoistureThresholdEnabled")]
        public bool SoilMoistureThresholdEnabled { get; set; }

        [JsonPropertyName("soilMoistureThreshold")]
        public int? SoilMoistureThreshold { get; set; }

        [JsonPropertyName("airTempThresholdEnabled")]
        public bool AirTempThresholdEnabled { get; set; }

        [JsonPropertyName("airTempMin")]
        public decimal? AirTempMin { get; set; }

        [JsonPropertyName("airTempMax")]
        public decimal? AirTempMax { get; set; }

        [JsonPropertyName("airHumidityThresholdEnabled")]
        public bool AirHumidityThresholdEnabled { get; set; }

        [JsonPropertyName("airHumidityThreshold")]
        public int? AirHumidityThreshold { get; set; }

        [JsonPropertyName("cooldownMinutes")]
        public int CooldownMinutes { get; set; } = 30;

        [JsonPropertyName("lastRunAt")]
        public string? LastRunAt { get; set; }

        [JsonPropertyName("lastRunLocal")]
        public string? LastRunLocal { get; set; }

        [JsonPropertyName("activeUntilAt")]
        public string? ActiveUntilAt { get; set; }

        [JsonPropertyName("activeUntilLocal")]
        public string? ActiveUntilLocal { get; set; }

        [JsonPropertyName("activeSource")]
        public string? ActiveSource { get; set; }

        [JsonPropertyName("manualOverrideUntilAt")]
        public string? ManualOverrideUntilAt { get; set; }

        [JsonPropertyName("manualOverrideUntilLocal")]
        public string? ManualOverrideUntilLocal { get; set; }

        [JsonPropertyName("nextRunAt")]
        public string? NextRunAt { get; set; }

        [JsonPropertyName("nextRunLocal")]
        public string? NextRunLocal { get; set; }

        [JsonPropertyName("nextWaterTime")]
        public long NextWaterTime { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("updatedLocal")]
        public string? UpdatedLocal { get; set; }

        [JsonPropertyName("lastSmartRunAt")]
        public string? LastSmartRunAt { get; set; }

        [JsonPropertyName("lastSmartRunLocal")]
        public string? LastSmartRunLocal { get; set; }

        [JsonPropertyName("lastTriggeredAt")]
        public string? LastTriggeredAt { get; set; }

        [JsonPropertyName("lastTriggeredLocal")]
        public string? LastTriggeredLocal { get; set; }

        [JsonPropertyName("lastWaterTime")]
        public long LastWaterTime { get; set; }

        [JsonPropertyName("thresholdConditionActive")]
        public bool ThresholdConditionActive { get; set; }

        [JsonPropertyName("thresholdStatus")]
        public string ThresholdStatus { get; set; } = "not-checked";

        [JsonPropertyName("thresholdReason")]
        public string? ThresholdReason { get; set; }

        [JsonPropertyName("automationLastCheckedAt")]
        public string? AutomationLastCheckedAt { get; set; }

        [JsonPropertyName("automationLastCheckedLocal")]
        public string? AutomationLastCheckedLocal { get; set; }
    }
}
