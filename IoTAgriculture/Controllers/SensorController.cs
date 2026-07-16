using System.Text.Json;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/sensors")]
    public class SensorController : ControllerBase
    {
        private readonly IFirebaseRtdbService _firebase;

        public SensorController(IFirebaseRtdbService firebase)
        {
            _firebase = firebase;
        }

        [HttpGet]
        public async Task<IActionResult> GetSensorKeys(CancellationToken cancellationToken)
        {
            var devices = await _firebase.GetAsync<Dictionary<string, JsonElement>>("devices", cancellationToken)
                ?? new Dictionary<string, JsonElement>();

            var keys = devices
                .Where(x => x.Value.ValueKind == JsonValueKind.Object && IsSensorPayload(x.Value))
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();

            return Ok(keys);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
        {
            var devices = await _firebase.GetAsync<Dictionary<string, JsonElement>>("devices", cancellationToken)
                ?? new Dictionary<string, JsonElement>();

            var sensors = devices
                .Where(x => x.Value.ValueKind == JsonValueKind.Object && IsSensorPayload(x.Value))
                .Select(x => new
                {
                    Key = x.Key,
                    Json = x.Value,
                    Timestamp = ReadTimestampValue(x.Value)
                })
                .ToList();

            var avgTemp = Average(sensors.Select(x => ReadDouble(x.Json, "temperature")));
            var avgHumidity = Average(sensors.Select(x => ReadDouble(x.Json, "humidity")));
            var avgAirQuality = Average(sensors.Select(x =>
                ReadDouble(x.Json, "air_quality") ?? ReadDouble(x.Json, "airQuality") ?? ReadDouble(x.Json, "air_quanlity")));
            var avgSoil = Average(sensors.Select(x =>
                ReadDouble(x.Json, "soil_moisture") ?? ReadDouble(x.Json, "soilMoisture")));
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var latestMs = sensors
                .Select(x => NormalizeTimestamp(x.Timestamp, nowMs))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty()
                .Max();
            var onlineCount = sensors.Count(x =>
            {
                var timestamp = NormalizeTimestamp(x.Timestamp, nowMs);
                return timestamp.HasValue && Math.Abs(nowMs - timestamp.Value) <= TimeSpan.FromMinutes(2).TotalMilliseconds;
            });

            return Ok(new
            {
                sensorCount = sensors.Count,
                onlineCount,
                latestUpdate = latestMs == 0 ? "Chưa có dữ liệu" : RelativeTime(latestMs, nowMs),
                criticalMessage = CriticalMessage(avgTemp, avgHumidity, avgAirQuality, avgSoil),
                temperature = Metric(avgTemp, TemperatureStatusText(avgTemp), TemperatureStatusLevel(avgTemp)),
                humidity = Metric(avgHumidity, SensorStatus(avgHumidity, 75, 92), StatusLevel(avgHumidity, 75, 92)),
                airQuality = Metric(avgAirQuality, AirQualityStatus(avgAirQuality), AirQualityLevel(avgAirQuality)),
                soilMoisture = Metric(avgSoil, SensorStatus(avgSoil, 35, 75), StatusLevel(avgSoil, 35, 75))
            });
        }

        [HttpGet("{sensorKey}")]
        public async Task<IActionResult> GetSensorState(string sensorKey, CancellationToken cancellationToken)
        {
            var json = await _firebase.GetAsync<JsonElement?>($"devices/{sensorKey}", cancellationToken);
            if (json == null || json.Value.ValueKind != JsonValueKind.Object || !IsSensorPayload(json.Value))
            {
                return NotFound();
            }

            return Ok(new SensorStateDto
            {
                Temperature = ReadDouble(json.Value, "temperature"),
                Humidity = ReadDouble(json.Value, "humidity"),
                AirQuality = ReadDouble(json.Value, "air_quality")
                    ?? ReadDouble(json.Value, "airQuality")
                    ?? ReadDouble(json.Value, "air_quanlity"),
                AirStatus = ReadString(json.Value, "air_status")
                    ?? ReadString(json.Value, "airStatus"),
                GroundTemperature = ReadDouble(json.Value, "ground_temperature")
                    ?? ReadDouble(json.Value, "groundTemperature")
                    ?? ReadDouble(json.Value, "lower_temperature")
                    ?? ReadDouble(json.Value, "lowerTemperature"),
                TopTemperature = ReadDouble(json.Value, "top_temperature")
                    ?? ReadDouble(json.Value, "topTemperature")
                    ?? ReadDouble(json.Value, "upper_temperature")
                    ?? ReadDouble(json.Value, "upperTemperature"),
                GroundHumidity = ReadDouble(json.Value, "ground_humidity")
                    ?? ReadDouble(json.Value, "groundHumidity")
                    ?? ReadDouble(json.Value, "lower_humidity")
                    ?? ReadDouble(json.Value, "lowerHumidity"),
                TopHumidity = ReadDouble(json.Value, "top_humidity")
                    ?? ReadDouble(json.Value, "topHumidity")
                    ?? ReadDouble(json.Value, "upper_humidity")
                    ?? ReadDouble(json.Value, "upperHumidity"),
                SoilMoisture = ReadDouble(json.Value, "soil_moisture")
                    ?? ReadDouble(json.Value, "soilMoisture"),
                Timestamp = ReadString(json.Value, "timestamp"),
                DeviceName = ReadString(json.Value, "device_name")
                    ?? ReadString(json.Value, "deviceName")
            });
        }

        [HttpGet("{sensorKey}/history")]
        public async Task<IActionResult> GetSensorHistory(
            string sensorKey,
            [FromQuery] long? from,
            [FromQuery] long? to,
            CancellationToken cancellationToken)
        {
            var raw = await _firebase.GetAsync<Dictionary<string, JsonElement>>(
                $"history/{sensorKey}",
                cancellationToken) ?? new Dictionary<string, JsonElement>();

            var items = raw
                .Where(x => x.Value.ValueKind == JsonValueKind.Object)
                .Select(x =>
                {
                    var timestamp = ReadTimestampValue(x.Value) ?? ParseTimestamp(x.Key);
                    return new
                    {
                        timestamp = timestamp?.ToString() ?? x.Key,
                        temperature = ReadDouble(x.Value, "temperature"),
                        humidity = ReadDouble(x.Value, "humidity"),
                        air_quality = ReadDouble(x.Value, "air_quality")
                            ?? ReadDouble(x.Value, "airQuality")
                            ?? ReadDouble(x.Value, "air_quanlity"),
                        ground_temperature = ReadDouble(x.Value, "ground_temperature")
                            ?? ReadDouble(x.Value, "groundTemperature")
                            ?? ReadDouble(x.Value, "lower_temperature")
                            ?? ReadDouble(x.Value, "lowerTemperature"),
                        top_temperature = ReadDouble(x.Value, "top_temperature")
                            ?? ReadDouble(x.Value, "topTemperature")
                            ?? ReadDouble(x.Value, "upper_temperature")
                            ?? ReadDouble(x.Value, "upperTemperature"),
                        ground_humidity = ReadDouble(x.Value, "ground_humidity")
                            ?? ReadDouble(x.Value, "groundHumidity")
                            ?? ReadDouble(x.Value, "lower_humidity")
                            ?? ReadDouble(x.Value, "lowerHumidity"),
                        top_humidity = ReadDouble(x.Value, "top_humidity")
                            ?? ReadDouble(x.Value, "topHumidity")
                            ?? ReadDouble(x.Value, "upper_humidity")
                            ?? ReadDouble(x.Value, "upperHumidity"),
                        soil_moisture = ReadDouble(x.Value, "soil_moisture")
                            ?? ReadDouble(x.Value, "soilMoisture")
                    };
                })
                .Where(x =>
                {
                    var timestamp = ParseTimestamp(x.timestamp);
                    return timestamp == null ||
                        ((from == null || timestamp >= from) && (to == null || timestamp <= to));
                })
                .OrderBy(x => ParseTimestamp(x.timestamp) ?? 0)
                .ToList();

            return Ok(items);
        }

        private static bool IsSensorPayload(JsonElement json)
        {
            return ReadDouble(json, "temperature") != null ||
                ReadDouble(json, "humidity") != null ||
                ReadDouble(json, "air_quality") != null ||
                ReadDouble(json, "airQuality") != null ||
                ReadDouble(json, "air_quanlity") != null ||
                ReadString(json, "air_status") != null ||
                ReadString(json, "airStatus") != null ||
                ReadDouble(json, "ground_humidity") != null ||
                ReadDouble(json, "groundHumidity") != null ||
                ReadDouble(json, "top_humidity") != null ||
                ReadDouble(json, "topHumidity") != null ||
                ReadDouble(json, "soil_moisture") != null ||
                ReadDouble(json, "soilMoisture") != null;
        }

        private static object Metric(double? value, string status, string level)
        {
            return new
            {
                value,
                displayValue = value == null ? "--" : value.Value.ToString("0.0"),
                status,
                level
            };
        }

        private static double? Average(IEnumerable<double?> values)
        {
            var numbers = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            return numbers.Count == 0 ? null : Math.Round(numbers.Average(), 1);
        }

        private static long? NormalizeTimestamp(long? timestamp, long nowMs)
        {
            const long minValidEpochMs = 946684800000;
            var maxFutureMs = nowMs + (long)TimeSpan.FromDays(1).TotalMilliseconds;
            return timestamp is >= minValidEpochMs && timestamp <= maxFutureMs ? timestamp : null;
        }

        private static string RelativeTime(long timestampMs, long nowMs)
        {
            var diff = TimeSpan.FromMilliseconds(Math.Max(0, nowMs - timestampMs));
            if (diff.TotalSeconds < 45) return "vừa xong";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} phút trước";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} giờ trước";
            var local = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).ToOffset(TimeSpan.FromHours(7));
            return local.ToString("dd/MM HH:mm");
        }

        private static string SensorStatus(double? value, double min, double max)
        {
            if (value == null) return "Chưa có dữ liệu";
            if (value < min) return "Thấp hơn ngưỡng";
            if (value > max) return "Vượt ngưỡng";
            return "Trong ngưỡng tốt";
        }

        private static string StatusLevel(double? value, double min, double max)
        {
            if (value == null) return "muted";
            if (value < min || value > max) return "danger";
            return value < min + 2 || value > max - 2 ? "warning" : "normal";
        }

        private static string TemperatureStatusText(double? temp)
        {
            if (temp == null) return "Chưa có dữ liệu";
            if (temp > 35) return "Khẩn cấp: vượt 35°C";
            if (temp > 30) return "Cảnh báo: vượt 30°C";
            if (temp < 16) return "Nhiệt độ quá thấp";
            return "Trong ngưỡng tốt";
        }

        private static string TemperatureStatusLevel(double? temp)
        {
            if (temp == null) return "muted";
            if (temp > 35 || temp < 16) return "danger";
            if (temp > 30) return "warning";
            return "normal";
        }

        private static string AirQualityStatus(double? value)
        {
            if (value == null) return "Chưa có dữ liệu";
            if (value <= 150) return "Tốt";
            if (value <= 300) return "Trung bình";
            if (value <= 1000) return "Kém";
            return "Rất kém";
        }

        private static string AirQualityLevel(double? value)
        {
            if (value == null) return "muted";
            if (value <= 150) return "normal";
            if (value <= 300) return "warning";
            return value <= 1000 ? "warning" : "danger";
        }

        private static string? CriticalMessage(double? temp, double? humidity, double? airQuality, double? soil)
        {
            if (temp > 35) return "Nhiệt độ rất cao, cần kiểm tra nhà nấm ngay.";
            if (temp > 30) return "Nhiệt độ cao, cần kiểm tra nhà nấm.";
            if (temp < 16) return "Nhiệt độ quá thấp, cần kiểm tra hệ thống.";
            if (humidity < 70 || humidity > 96) return "Độ ẩm không khí bất thường, cần kiểm tra thông gió.";
            if (airQuality > 1000) return "Chất lượng không khí rất xấu, cần kiểm tra thông gió ngay.";
            if (soil < 30) return "Độ ẩm đất thấp, nên kiểm tra tưới nước.";
            return null;
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

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static long? ReadTimestampValue(JsonElement json)
        {
            return ParseTimestamp(ReadString(json, "timestamp"));
        }

        private static long? ParseTimestamp(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (long.TryParse(raw, out var parsed))
            {
                return parsed < 1_000_000_000_000 ? parsed * 1000 : parsed;
            }

            return DateTimeOffset.TryParse(raw, out var date)
                ? date.ToUniversalTime().ToUnixTimeMilliseconds()
                : null;
        }
    }
}
