using IoTAgriculture.API.Contracts;
using IoTAgriculture.API.Services;
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
            ? "Hãy kiểm tra dấu hiệu bất thường trong ảnh và hướng dẫn tôi xử lý."
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
        return FarmContextFormatter.Format(devices);
    }
}
