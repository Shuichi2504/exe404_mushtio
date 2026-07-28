using IoTAgriculture.API.Contracts;
using IoTAgriculture.API.Services;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs;
using IoTAgriculture.Models;
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
    private readonly IAuthService _authService;
    private readonly IoTDbContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(
        GeminiService geminiService,
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

    private static readonly HashSet<string> AllowedReportReasons =
    [
        "inappropriate",
        "possibly_incorrect_or_dangerous",
        "not_relevant",
        "other"
    ];

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

    [HttpPost("reports")]
    public async Task<IActionResult> ReportResponse(
        [FromBody] AiReportRequestDto request,
        CancellationToken cancellationToken)
    {
        var profile = await _authService.GetProfileAsync(ReadBearerToken());
        if (profile == null)
        {
            return Unauthorized();
        }

        if (request.UserId == Guid.Empty || request.UserId != profile.UserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var reason = request.Reason.Trim().ToLowerInvariant();
        var note = request.Note?.Trim();
        var prompt = request.Prompt.Trim();
        var response = request.Response.Trim();

        if (!AllowedReportReasons.Contains(reason))
        {
            return BadRequest(new { message = "Invalid report reason" });
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            return BadRequest(new { message = "AI response is required" });
        }

        if (note?.Length > 1000 || prompt.Length > 4000 || response.Length > 12000)
        {
            return BadRequest(new { message = "Report content is too long" });
        }

        var report = new AiResponseReport
        {
            AiResponseReportId = Guid.NewGuid(),
            UserId = profile.UserId,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            Prompt = prompt,
            Response = response,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.AiResponseReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            reportId = report.AiResponseReportId,
            message = "AI response report submitted"
        });
    }

    private async Task<string> BuildFarmContextAsync(CancellationToken cancellationToken)
    {
        var devices = await _firebase.GetAsync<Dictionary<string, JsonElement>>(
            "devices",
            cancellationToken) ?? new Dictionary<string, JsonElement>();
        return FarmContextFormatter.Format(devices);
    }

    private string ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }
}
