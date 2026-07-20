using IoTAgriculture.API.Contracts;
using IoTAgriculture.DTOs;
using IoTAgriculture.Services;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IoTAgriculture.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly GeminiService _geminiService;
    private readonly IFirebaseRtdbService _firebase;
    private readonly ILogger<AiController> _logger;

    public AiController(
        GeminiService geminiService,
        IFirebaseRtdbService firebase,
        ILogger<AiController> logger)
    {
        _geminiService = geminiService;
        _firebase = firebase;
        _logger = logger;
    }

    [HttpPost("chat")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Chat([FromForm] ChatRequestDto request)
    {
        if (request.UserId == Guid.Empty)
        {
            return BadRequest(new { message = "UserId is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Message) &&
            (request.Image == null || request.Image.Length == 0))
        {
            return BadRequest(new { message = "Message or image is required" });
        }

        var message = string.IsNullOrWhiteSpace(request.Message)
            ? "Hay xem hinh anh nay va dua ra nhan xet."
            : request.Message.Trim();

        string answer;
        try
        {
            var farmContext = await BuildFarmContextAsync(HttpContext.RequestAborted);
            answer = await _geminiService.AskAsync(
                message,
                request.Image,
                farmContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat request failed");
            var userMessage = ex is InvalidOperationException &&
                ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
                ? "Backend chua cau hinh Gemini API key. Hay them Gemini__ApiKey trong Azure App Service."
                : "AI khong phan hoi duoc. Vui long kiem tra Gemini API key, model hoac ket noi mang cua backend.";

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { message = userMessage });
        }

        return Ok(new ChatResponseDto { Answer = answer });
    }

    private async Task<string> BuildFarmContextAsync(CancellationToken cancellationToken)
    {
        var devices = await _firebase.GetAsync<Dictionary<string, JsonElement>>(
            "devices",
            cancellationToken) ?? new Dictionary<string, JsonElement>();
        var sensors = devices
            .Where(x => x.Value.ValueKind == JsonValueKind.Object && IsSensorPayload(x.Value))
            .Select(x => x.Value)
            .ToList();

        if (sensors.Count == 0)
        {
            return "Chua co du lieu cam bien hien tai tu he thong backend.";
        }

        var avgTemp = Average(sensors.Select(x => ReadDouble(x, "temperature")));
        var avgHumidity = Average(sensors.Select(x => ReadDouble(x, "humidity")));
        var avgAirQuality = Average(sensors.Select(x =>
            ReadDouble(x, "air_quality") ?? ReadDouble(x, "airQuality") ?? ReadDouble(x, "air_quanlity")));
        var avgGroundHumidity = Average(sensors.Select(x =>
            ReadDouble(x, "ground_humidity") ?? ReadDouble(x, "groundHumidity")));
        var avgTopHumidity = Average(sensors.Select(x =>
            ReadDouble(x, "top_humidity") ?? ReadDouble(x, "topHumidity")));

        return string.Join('\n', new[]
        {
            $"So cam bien dang theo doi: {sensors.Count}",
            $"Nhiet do trung binh: {FormatMetric(avgTemp, "C")}",
            $"Do am khong khi trung binh: {FormatMetric(avgHumidity, "%")}",
            $"Chat luong khong khi trung binh: {FormatMetric(avgAirQuality, "")}",
            $"Do am tang thap: {FormatMetric(avgGroundHumidity, "%")}",
            $"Do am tang cao: {FormatMetric(avgTopHumidity, "%")}"
        });
    }

    private static bool IsSensorPayload(JsonElement json)
    {
        return ReadDouble(json, "temperature") != null ||
            ReadDouble(json, "humidity") != null ||
            ReadDouble(json, "air_quality") != null ||
            ReadDouble(json, "airQuality") != null ||
            ReadDouble(json, "air_quanlity") != null;
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var numbers = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return numbers.Count == 0 ? null : Math.Round(numbers.Average(), 1);
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

    private static string FormatMetric(double? value, string unit)
    {
        return value == null ? "chua co du lieu" : $"{value:0.0}{unit}";
    }
}
