using IoTAgriculture.Data;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceService _service;
        private readonly IAuthService _authService;
        private readonly IoTDbContext _db;

        public DeviceController(IDeviceService service, IAuthService authService, IoTDbContext db)
        {
            _service = service;
            _authService = authService;
            _db = db;
        }

        [HttpGet("{deviceKey}")]
        public async Task<IActionResult> GetPumpState(string deviceKey)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();
            if (!await CanReadDeviceAsync(deviceKey, profile.UserId, profile.Role)) return StatusCode(StatusCodes.Status403Forbidden);

            var state = await _service.GetPumpStateAsync(deviceKey);
            if (state == null)
            {
                return NotFound();
            }

            return Ok(state);
        }

        [HttpPut("{deviceKey}/relay/{relayKey}")]
        public async Task<IActionResult> SetRelay(
            string deviceKey,
            string relayKey,
            [FromBody] RelayUpdateDto dto)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();
            if (!await CanReadDeviceAsync(deviceKey, profile.UserId, profile.Role)) return StatusCode(StatusCodes.Status403Forbidden);

            await _service.SetRelayAsync(
                deviceKey,
                relayKey,
                dto.Value,
                "manual",
                profile?.UserId.ToString(),
                profile?.FullName ?? "Manual user");
            return Ok(new { message = "Relay updated" });
        }

        [HttpGet("{deviceKey}/logs")]
        public async Task<IActionResult> GetLogs(string deviceKey, [FromQuery] int limit = 50)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();
            if (!await CanReadDeviceAsync(deviceKey, profile.UserId, profile.Role)) return StatusCode(StatusCodes.Status403Forbidden);

            var logs = await _service.GetPumpLogsAsync(deviceKey, limit);
            return Ok(logs);
        }

        [HttpGet("{deviceKey}/schedule/{relayKey}")]
        public async Task<IActionResult> GetSchedule(string deviceKey, string relayKey)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();
            if (!await CanReadDeviceAsync(deviceKey, profile.UserId, profile.Role)) return StatusCode(StatusCodes.Status403Forbidden);

            var schedule = await _service.GetScheduleAsync(deviceKey, relayKey);
            if (schedule == null)
            {
                return NotFound();
            }

            return Ok(schedule);
        }

        [HttpPut("{deviceKey}/schedule/{relayKey}")]
        public async Task<IActionResult> SaveSchedule(
            string deviceKey,
            string relayKey,
            [FromBody] UpsertAutoIrrigationScheduleDto dto)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return Unauthorized();
            if (!await CanReadDeviceAsync(deviceKey, profile.UserId, profile.Role)) return StatusCode(StatusCodes.Status403Forbidden);

            var schedule = await _service.SaveScheduleAsync(deviceKey, relayKey, dto);
            return Ok(schedule);
        }

        private async Task<bool> CanReadDeviceAsync(string deviceKey, Guid userId, string role)
        {
            if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return await _db.UserDevices.AnyAsync(x =>
                x.UserId == userId && x.DeviceKey == deviceKey);
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
