using IoTAgriculture.API.Contracts;
using IoTAgriculture.API.Services;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs;
using IoTAgriculture.Models;
using IoTAgriculture.Services;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTAgriculture.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly IGeminiService _geminiService;
    private readonly IFirebaseRtdbService _firebase;
    private readonly IAuthService _authService;
    private readonly IoTDbContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IGeminiService geminiService,
        IFirebaseRtdbService firebase,
        IAuthService authService,
        IoTDbContext db,
        ILogger<AiController> logger)
    {
        _geminiService = geminiService;
        _firebase = firebase;
        _authService = authService;
        _db = db;
        _logger = logger;
    }

    [HttpPost("chat")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Chat([FromForm] ChatRequestDto request)
    {
        var profile = await _authService.GetProfileAsync(ReadBearerToken());
        if (profile == null)
        {
            return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ" });
        }

        if (request.UserId != Guid.Empty && request.UserId != profile.UserId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "FORBIDDEN", message = "Không thể gửi yêu cầu cho tài khoản khác." });
        }

        if (!string.Equals(
                profile.AccountType,
                AccountTypes.Premium,
                StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "FEATURE_LOCKED",
                    message = "Chatbot AI chỉ dành cho tài khoản Premium."
                });
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
            var farmContext = await BuildFarmContextAsync(
                profile.UserId,
                HttpContext.RequestAborted);
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

    private string ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }

    private async Task<string> BuildFarmContextAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var assignedKeys = await _db.UserDevices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DeviceKey)
            .ToListAsync(cancellationToken);
        if (assignedKeys.Count == 0)
        {
            return FarmContextFormatter.Format(
                new Dictionary<string, JsonElement>());
        }

        var allowedKeys = assignedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allDevices = await _firebase.GetAsync<Dictionary<string, JsonElement>>(
            "devices",
            cancellationToken) ?? new Dictionary<string, JsonElement>();
        var assignedDevices = allDevices
            .Where(entry => allowedKeys.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        return FarmContextFormatter.Format(assignedDevices);
    }
}
