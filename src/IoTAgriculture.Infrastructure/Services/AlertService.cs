using System.Text.Json;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class AlertService : IAlertService
    {
        private const double HighTemperatureWarningCelsius = 30;
        private const double HighTemperatureCriticalCelsius = 35;
        private const double LowHumidityPercent = 70;
        private const double HighHumidityPercent = 96;
        private const double PoorAirQualityThreshold = 300;
        private static readonly TimeSpan AlertResendCooldown = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan SensorOfflineAfter = TimeSpan.FromMinutes(2);
        private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
        private readonly IFirebaseRtdbService _firebase;
        private readonly IFirebasePushNotificationService _pushNotifications;
        private readonly ILogger<AlertService> _logger;

        public AlertService(
            IFirebaseRtdbService firebase,
            IFirebasePushNotificationService pushNotifications,
            ILogger<AlertService> logger)
        {
            _firebase = firebase;
            _pushNotifications = pushNotifications;
            _logger = logger;
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
                var humidity = ReadDouble(device.Value, "humidity");
                var airQuality = ReadDouble(device.Value, "air_quality")
                    ?? ReadDouble(device.Value, "airQuality")
                    ?? ReadDouble(device.Value, "air_quanlity");
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
                    $"Nhiệt độ {FormatValue(temperature)}°C vượt ngưỡng {HighTemperatureWarningCelsius:0.#}°C",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "high_temperature_critical",
                    temperature != null && temperature > HighTemperatureCriticalCelsius,
                    "temperature",
                    temperature,
                    HighTemperatureCriticalCelsius,
                    $"Nhiệt độ {FormatValue(temperature)}°C vượt ngưỡng khẩn cấp {HighTemperatureCriticalCelsius:0.#}°C",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "low_humidity",
                    humidity != null && humidity < LowHumidityPercent,
                    "humidity",
                    humidity,
                    LowHumidityPercent,
                    $"Độ ẩm không khí {FormatValue(humidity)}% thấp hơn ngưỡng {LowHumidityPercent:0.#}%",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "high_humidity",
                    humidity != null && humidity > HighHumidityPercent,
                    "humidity",
                    humidity,
                    HighHumidityPercent,
                    $"Độ ẩm không khí {FormatValue(humidity)}% vượt ngưỡng {HighHumidityPercent:0.#}%",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "poor_air_quality",
                    airQuality != null && airQuality > PoorAirQualityThreshold,
                    "air_quality",
                    airQuality,
                    PoorAirQualityThreshold,
                    $"Chất lượng không khí {FormatValue(airQuality)} pts vượt ngưỡng {PoorAirQualityThreshold:0.#} pts",
                    cancellationToken);

                await UpsertAlertAsync(
                    device.Key,
                    name,
                    "sensor_disconnected",
                    lastSeen == null || DateTimeOffset.UtcNow - lastSeen > SensorOfflineAfter,
                    "timestamp",
                    lastSeen == null ? null : (double)lastSeen.Value.ToUnixTimeMilliseconds(),
                    SensorOfflineAfter.TotalSeconds,
                    $"Cảm biến {name} đã mất kết nối",
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
                    var resolvedUtc = DateTimeOffset.UtcNow;
                    existing.Resolved = true;
                    existing.UtcTime = resolvedUtc.ToString("O");
                    existing.LocalTime = TimeZoneInfo
                        .ConvertTime(resolvedUtc, VietnamTimeZone)
                        .ToString("yyyy-MM-dd HH:mm:ss");
                    await _firebase.SetAsync(path, existing, cancellationToken);
                    _logger.LogInformation(
                        "Calling SendDeviceAlertAsync for resolved alert {AlertType} on device {DeviceKey}.",
                        alertType,
                        deviceKey);
                    await _pushNotifications.SendDeviceAlertAsync(
                        deviceKey,
                        deviceName,
                        alertType,
                        metric,
                        $"Đã ổn định: {deviceName}",
                        $"{MetricLabel(metric)} của {deviceName} đã trở lại bình thường.",
                        "info",
                        value,
                        threshold,
                        cancellationToken);
                }

                return;
            }

            if (existing != null && !existing.Resolved)
            {
                var lastSent = existing.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(existing.Timestamp)
                    : DateTimeOffset.MinValue;
                if (DateTimeOffset.UtcNow - lastSent < AlertResendCooldown)
                {
                    _logger.LogInformation(
                        "Alert {AlertType} on device {DeviceKey} is active but still in the {CooldownMinutes}-minute resend cooldown.",
                        alertType,
                        deviceKey,
                        AlertResendCooldown.TotalMinutes);
                    return;
                }
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
            _logger.LogInformation(
                "Calling SendDeviceAlertAsync for active alert {AlertType} on device {DeviceKey}: value {Value}, threshold {Threshold}.",
                alertType,
                deviceKey,
                value,
                threshold);
            await _pushNotifications.SendDeviceAlertAsync(
                deviceKey,
                deviceName,
                alertType,
                metric,
                AlertTitle(alertType),
                message,
                alert.Severity,
                value,
                threshold,
                cancellationToken);
        }

        private static string AlertTitle(string alertType)
        {
            return alertType switch
            {
                "high_temperature_warning" => "Cảnh báo nhiệt độ!",
                "high_temperature_critical" => "Cảnh báo nhiệt độ khẩn cấp!",
                "low_humidity" or "high_humidity" => "Cảnh báo độ ẩm không khí!",
                "poor_air_quality" => "Cảnh báo chất lượng không khí!",
                "sensor_disconnected" => "Cảnh báo mất kết nối!",
                _ => "Cảnh báo cảm biến!"
            };
        }

        private static string MetricLabel(string metric)
        {
            return metric switch
            {
                "temperature" => "Nhiệt độ",
                "humidity" => "Độ ẩm không khí",
                "air_quality" => "Chất lượng không khí",
                "timestamp" => "Kết nối cảm biến",
                _ => "Chỉ số cảm biến"
            };
        }

        private static string FormatValue(double? value)
        {
            return value?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "--";
        }

        private static bool IsSensorPayload(JsonElement json)
        {
            return HasMetric(json, "temperature") ||
                HasMetric(json, "humidity") ||
                HasMetric(json, "air_quality") ||
                HasMetric(json, "airQuality") ||
                HasMetric(json, "air_quanlity") ||
                ReadString(json, "air_status") != null ||
                ReadString(json, "airStatus") != null;
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
