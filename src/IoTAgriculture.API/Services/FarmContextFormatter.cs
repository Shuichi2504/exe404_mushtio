using System.Globalization;
using System.Text.Json;

namespace IoTAgriculture.API.Services;

public static class FarmContextFormatter
{
    public static string Format(IReadOnlyDictionary<string, JsonElement> devices)
    {
        var sensors = devices
            .Where(entry =>
                entry.Value.ValueKind == JsonValueKind.Object &&
                IsSensorPayload(entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sensors.Count == 0)
        {
            return "Chưa có dữ liệu cảm biến hiện tại từ hệ thống backend.";
        }

        var lines = new List<string>
        {
            "Các số dưới đây là giá trị đo hiện tại của từng thiết bị tại một thời điểm:",
            $"Số thiết bị cảm biến đang theo dõi: {sensors.Count}"
        };

        foreach (var sensor in sensors)
        {
            var json = sensor.Value;
            lines.Add($"Thiết bị {sensor.Key}:");
            lines.Add($"- Nhiệt độ hiện tại: {FormatMetric(ReadDouble(json, "temperature"), "°C")}");
            lines.Add($"- Độ ẩm không khí hiện tại: {FormatMetric(ReadDouble(json, "humidity"), "%")}");
            lines.Add(
                $"- Chất lượng không khí hiện tại: {FormatMetric(ReadFirstDouble(json, "air_quality", "airQuality", "air_quanlity"), " ppm")}");
        }

        return string.Join('\n', lines);
    }

    private static bool IsSensorPayload(JsonElement json)
    {
        return ReadDouble(json, "temperature") != null ||
            ReadDouble(json, "humidity") != null ||
            ReadFirstDouble(json, "air_quality", "airQuality", "air_quanlity") != null;
    }

    private static double? ReadFirstDouble(JsonElement json, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadDouble(json, name);
            if (value.HasValue)
            {
                return value;
            }
        }

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
            double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            ? parsed
            : null;
    }

    private static string FormatMetric(double? value, string unit)
    {
        return value == null
            ? "chưa có dữ liệu"
            : $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)}{unit}";
    }
}
