using System.ComponentModel.DataAnnotations;

namespace IoTAgriculture.DTOs.Firebase
{
    public class UpsertAutoIrrigationScheduleDto
    {
        public bool Enabled { get; set; }

        [Range(1, 1440)]
        public int IntervalMinutes { get; set; }

        [Range(1, 86400)]
        public int DurationSeconds { get; set; }

        [RegularExpression(@"^([01]\d|2[0-3]):([0-5]\d)$")]
        public string StartTime { get; set; } = "06:00";

        [RegularExpression(@"^([01]\d|2[0-3]):([0-5]\d)$")]
        public string EndTime { get; set; } = "18:00";

        public bool SmartEnabled { get; set; }

        public string? SensorKey { get; set; }

        public bool AirTempThresholdEnabled { get; set; }

        [Range(-20, 100)]
        public decimal? AirTempMin { get; set; }

        [Range(-20, 100)]
        public decimal? AirTempMax { get; set; }

        public bool AirHumidityThresholdEnabled { get; set; }

        [Range(0, 100)]
        public int? AirHumidityThreshold { get; set; }

        [Range(0, 100)]
        public int? AirHumidityOnThreshold { get; set; }

        [Range(0, 100)]
        public int? AirHumidityOffThreshold { get; set; }

        [Range(1, 1440)]
        public int CooldownMinutes { get; set; } = 30;
    }
}
