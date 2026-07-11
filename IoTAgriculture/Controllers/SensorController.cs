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
