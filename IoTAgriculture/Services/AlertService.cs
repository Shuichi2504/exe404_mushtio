using System.Text.Json;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class AlertService : IAlertService
    {
        private const double HighTemperatureWarningCelsius = 30;
        private const double HighTemperatureCriticalCelsius = 35;
        private const double LowSoilMoisturePercent = 30;
        private const double PoorAirQualityThreshold = 1000;
        private static readonly TimeSpan SensorOfflineAfter = TimeSpan.FromMinutes(2);
        private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
        private readonly IFirebaseRtdbService _firebase;
        private readonly IFirebasePushNotificationService _pushNotifications;

        public AlertService(
            IFirebaseRtdbService firebase,
            IFirebasePushNotificationService pushNotifications)
        {
            _firebase = firebase;
            _pushNotifications = pushNotifications;
        }

        public async Task ProcessAlertsAsync(CancellationToken cancellationToken = default)
        {
            var devices = await _firebase.GetAsync<Dictionary<string, JsonElement>>("devices", cancellationToken)
                ?? new Dictionary<string, JsonElement>();

            foreach (var device in devices)
            {
                if (device.Value.ValueKind != JsonValueKind.Object || !IsSensorPayload(device.Value))
                {
                    continue;
                }

                var name = ReadString(device.Value, "device_name")
                    ?? ReadString(device.Value, "deviceName")
                    ?? device.Key;
                var temperature = ReadDouble(device.Value, "temperature");
                var airQuality = ReadDouble(device.Value, "air_quality")
                    ?? ReadDouble(device.Value, "airQuality")
                    ?? ReadDouble(device.Value, "air_quanlity");
                var soilMoisture = ReadDouble(device.Value, "soil_moisture")
                    ?? ReadDouble(device.Value, "soilMoisture");
                var lastSeen = ReadTimestamp(device.Value);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "high_temperature_warning",
                    temperature != null &&
                        temperature > HighTemperatureWarningCelsius &&
                        temperature <= HighTemperatureCriticalCelsius,
                    "temperature",
                    temperature,
                    HighTemperatureWarningCelsius,
                    $"Nhiet do cao tren {HighTemperatureWarningCelsius:0.#} C",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "high_temperature_critical",
                    temperature != null && temperature > HighTemperatureCriticalCelsius,
                    "temperature",
                    temperature,
                    HighTemperatureCriticalCelsius,
                    $"Nhiet do rat cao tren {HighTemperatureCriticalCelsius:0.#} C",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "poor_air_quality",
                    airQuality != null && airQuality > PoorAirQualityThreshold,
                    "air_quality",
                    airQuality,
                    PoorAirQualityThreshold,
                    $"Chat luong khong khi xau tren {PoorAirQualityThreshold:0.#}",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "low_soil_moisture",
                    soilMoisture != null && soilMoisture < LowSoilMoisturePercent,
                    "soil_moisture",
                    soilMoisture,
                    LowSoilMoisturePercent,
                    $"Do am dat thap duoi {LowSoilMoisturePercent:0.#}%",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "sensor_disconnected",
                    lastSeen == null || DateTimeOffset.UtcNow - lastSeen > SensorOfflineAfter,
                    "timestamp",
                    lastSeen == null ? null : (double)lastSeen.Value.ToUnixTimeMilliseconds(),
                    SensorOfflineAfter.TotalSeconds,
                    "Cam bien mat ket noi",
                    cancellationToken);
            }
        }

        private async Task UpsertAlertAsync(
            string deviceKey,
            string deviceName,
            string alertType,
            bool active,
            string metric,
            double? value,
            double? threshold,
            string message,
            CancellationToken cancellationToken)
        {
            var path = $"activeAlerts/{deviceKey}/{alertType}";
            var existing = await _firebase.GetAsync<AlertEntryDto>(path, cancellationToken);

            if (!active)
            {
                if (existing != null && !existing.Resolved)
                {
                    existing.Resolved = true;
                    await _firebase.SetAsync(path, existing, cancellationToken);
                }

                return;
            }

            if (existing != null && !existing.Resolved)
            {
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
            var alert = new AlertEntryDto
            {
                DeviceKey = deviceKey,
                DeviceName = deviceName,
                AlertType = alertType,
                Severity = alertType == "sensor_disconnected" ? "critical" : "warning",
                Message = message,
                Metric = metric,
                Value = value,
                Threshold = threshold,
                Timestamp = nowUtc.ToUnixTimeMilliseconds(),
                UtcTime = nowUtc.ToString("O"),
                LocalTime = nowLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                Resolved = false
            };

            await _firebase.SetAsync(path, alert, cancellationToken);
            await _firebase.PushAsync($"alerts/{deviceKey}", alert, cancellationToken);
            await _pushNotifications.SendDeviceAlertAsync(
                deviceKey,
                deviceName,
                $"Cảnh báo {deviceName}",
                message,
                alert.Severity,
                cancellationToken);
        }

        private static bool IsSensorPayload(JsonElement json)
        {
            return HasMetric(json, "temperature") ||
                HasMetric(json, "humidity") ||
                HasMetric(json, "air_quality") ||
                HasMetric(json, "airQuality") ||
                HasMetric(json, "air_quanlity") ||
                ReadString(json, "air_status") != null ||
                ReadString(json, "airStatus") != null ||
                HasMetric(json, "soil_moisture") ||
                HasMetric(json, "soilMoisture");
        }

        private static bool HasMetric(JsonElement json, string name)
        {
            return ReadDouble(json, name) != null;
        }

        private static double? ReadDouble(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), out var parsed)
                ? parsed
                : null;
        }

        private static string? ReadString(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();
        }

        private static DateTimeOffset? ReadTimestamp(JsonElement json)
        {
            var raw = ReadString(json, "timestamp");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (long.TryParse(raw, out var numeric))
            {
                try
                {
                    return numeric < 1_000_000_000_000
                        ? DateTimeOffset.FromUnixTimeSeconds(numeric)
                        : DateTimeOffset.FromUnixTimeMilliseconds(numeric);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return DateTimeOffset.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : null;
        }

        private static TimeZoneInfo ResolveVietnamTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
        }
    }
}
