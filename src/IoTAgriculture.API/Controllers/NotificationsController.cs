using IoTAgriculture.Data;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly IoTDbContext _db;
        private readonly IAuthService _authService;

        public NotificationsController(IoTDbContext db, IAuthService authService)
        {
            _db = db;
            _authService = authService;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.UserNotifications
                .Where(x => x.UserId == profile.UserId)
                .OrderByDescending(x => x.CreatedAt);

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    id = x.UserNotificationId,
                    x.DeviceKey,
                    x.DeviceName,
                    x.AlertType,
                    x.MetricType,
                    x.Severity,
                    x.Title,
                    x.Body,
                    x.Value,
                    x.Threshold,
                    x.IsRead,
                    x.CreatedAt,
                    x.ReadAt
                })
                .ToListAsync();
            var unreadCount = await _db.UserNotifications.CountAsync(x =>
                x.UserId == profile.UserId && !x.IsRead);

            return Ok(new { page, pageSize, total, unreadCount, items });
        }

        [HttpPut("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();

            var notification = await _db.UserNotifications.FirstOrDefaultAsync(x =>
                x.UserNotificationId == id && x.UserId == profile.UserId);
            if (notification == null) return NotFound();

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Notification marked as read" });
        }

        private string ReadBearerToken()
        {
            var header = Request.Headers.Authorization.ToString();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : string.Empty;
        }
    }
}
