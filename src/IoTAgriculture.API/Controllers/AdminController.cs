using System.Text.Json;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs.Admin;
using IoTAgriculture.Models;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IoTDbContext _db;
        private readonly IFirebaseRtdbService _firebase;
        private readonly IAuthService _authService;
        private readonly IAdminService _adminService;

        public AdminController(
            IoTDbContext db,
            IFirebaseRtdbService firebase,
            IAuthService authService,
            IAdminService adminService)
        {
            _db = db;
            _firebase = firebase;
            _authService = authService;
            _adminService = adminService;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            return Ok(await _adminService.GetDashboardStatsAsync(cancellationToken));
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var users = await ProjectAdminUsers()
                .OrderBy(u => u.FullName)
                .ToListAsync();

            return Ok(users);
        }

        [HttpGet("users/{userId:guid}")]
        public async Task<IActionResult> GetUser(Guid userId)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var user = await ProjectAdminUsers()
                .FirstOrDefaultAsync(u => u.UserId == userId);
            return user == null
                ? NotFound(new { message = "Không tìm thấy tài khoản" })
                : Ok(user);
        }

        [HttpPatch("users/{userId:guid}/account-type")]
        public async Task<IActionResult> UpdateAccountType(
            Guid userId,
            [FromBody] UpdateAccountTypeRequestDto dto,
            CancellationToken cancellationToken)
        {
            var adminProfile = await _authService.GetProfileAsync(ReadBearerToken());
            if (adminProfile?.Role != "admin")
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            if (adminProfile.UserId == userId)
            {
                return BadRequest(new
                {
                    message = "Không thể tự đổi loại tài khoản của chính bạn"
                });
            }

            var requestedType = dto.AccountType?.Trim();
            if (!AccountTypes.IsValid(requestedType))
            {
                return BadRequest(new
                {
                    message = "Loại tài khoản chỉ được là 'standard' hoặc 'premium'"
                });
            }

            var user = await _db.Users.FirstOrDefaultAsync(
                u => u.UserId == userId,
                cancellationToken);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy tài khoản" });
            }

            if (user.DeactivatedAt != null)
            {
                return BadRequest(new
                {
                    message = "Không thể đổi loại tài khoản của tài khoản đã vô hiệu hóa"
                });
            }

            user.AccountType = AccountTypes.Normalize(requestedType);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(ToAdminUserDto(user));
        }

        [HttpPost("users/{userId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateUser(
            Guid userId,
            CancellationToken cancellationToken)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var changed = await _adminService.DeactivateUserAsync(
                    userId,
                    cancellationToken);
                return changed
                    ? Ok(new { message = "Đã vô hiệu hoá tài khoản" })
                    : NotFound(new { message = "Không tìm thấy tài khoản" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("users/{userId:guid}/reactivate")]
        public async Task<IActionResult> ReactivateUser(
            Guid userId,
            CancellationToken cancellationToken)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var changed = await _adminService.ReactivateUserAsync(
                userId,
                cancellationToken);
            return changed
                ? Ok(new { message = "Đã khôi phục tài khoản" })
                : NotFound(new { message = "Không tìm thấy tài khoản" });
        }

        [HttpGet("firebase-devices")]
        public async Task<IActionResult> GetFirebaseDevices()
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            return Ok(await _adminService.ReadFirebaseDevicesAsync());
        }

        [HttpPut("firebase-devices/{deviceKey}/name")]
        public async Task<IActionResult> UpdateDeviceName(
            string deviceKey,
            [FromBody] UpdateDeviceNameRequestDto dto)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var cleanDeviceKey = deviceKey.Trim();
            var cleanDeviceName = dto.DeviceName.Trim();
            if (string.IsNullOrWhiteSpace(cleanDeviceKey) ||
                string.IsNullOrWhiteSpace(cleanDeviceName))
            {
                return BadRequest(new { message = "Device name is required" });
            }

            var device = await _firebase.GetAsync<JsonElement?>($"devices/{cleanDeviceKey}");
            if (device == null || device.Value.ValueKind == JsonValueKind.Null)
            {
                return NotFound(new { message = "Device not found" });
            }

            await _firebase.PatchAsync(
                $"devices/{cleanDeviceKey}",
                new Dictionary<string, string>
                {
                    ["device_name"] = cleanDeviceName,
                    ["deviceName"] = cleanDeviceName
                });

            var assignments = await _db.UserDevices
                .Where(x => x.DeviceKey == cleanDeviceKey)
                .ToListAsync();
            foreach (var assignment in assignments)
            {
                assignment.DeviceName = cleanDeviceName;
            }

            await _db.SaveChangesAsync();

            return Ok(await _adminService.ReadFirebaseDevicesAsync());
        }

        [HttpGet("users/{userId:guid}/devices")]
        public async Task<IActionResult> GetUserDevices(Guid userId)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            return Ok(await GetUserDevicesAsync(userId));
        }

        [HttpPost("users/{userId:guid}/devices")]
        public async Task<IActionResult> AssignDevice(
            Guid userId,
            [FromBody] AssignDeviceRequestDto dto)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var userExists = await _db.Users.AnyAsync(u => u.UserId == userId);
            if (!userExists)
            {
                return NotFound(new { message = "User not found" });
            }

            var deviceKey = dto.DeviceKey.Trim();
            var exists = await _db.UserDevices.AnyAsync(x =>
                x.UserId == userId && x.DeviceKey == deviceKey);
            if (!exists)
            {
                _db.UserDevices.Add(new UserDevice
                {
                    UserDeviceId = Guid.NewGuid(),
                    UserId = userId,
                    DeviceKey = deviceKey,
                    DeviceName = string.IsNullOrWhiteSpace(dto.DeviceName)
                        ? deviceKey
                        : dto.DeviceName.Trim(),
                    AssignedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            return Ok(await GetUserDevicesAsync(userId));
        }

        [HttpDelete("users/{userId:guid}/devices/{deviceKey}")]
        public async Task<IActionResult> UnassignDevice(Guid userId, string deviceKey)
        {
            if (!await IsAdminAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var assignment = await _db.UserDevices.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.DeviceKey == deviceKey);
            if (assignment != null)
            {
                _db.UserDevices.Remove(assignment);
                await _db.SaveChangesAsync();
            }

            return Ok(await GetUserDevicesAsync(userId));
        }

        [HttpGet("/api/me/devices")]
        public async Task<IActionResult> GetMyDevices()
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null)
            {
                return Unauthorized();
            }

            return Ok(await GetUserDevicesAsync(profile.UserId));
        }

        private async Task<List<UserDeviceDto>> GetUserDevicesAsync(Guid userId)
        {
            var firebaseDevices = await _adminService.ReadFirebaseDevicesAsync();
            var firebaseDeviceByKey = firebaseDevices.ToDictionary(x => x.DeviceKey, StringComparer.OrdinalIgnoreCase);

            var assignments = await _db.UserDevices
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.DeviceName)
                .ToListAsync();

            return assignments
                .Select(x =>
                {
                    firebaseDeviceByKey.TryGetValue(x.DeviceKey, out var firebaseDevice);
                    return new UserDeviceDto
                    {
                        UserDeviceId = x.UserDeviceId,
                        UserId = x.UserId,
                        DeviceKey = x.DeviceKey,
                        DeviceName = firebaseDevice?.DeviceName ?? x.DeviceName ?? x.DeviceKey,
                        DeviceType = firebaseDevice?.DeviceType ?? "device",
                        AssignedAt = x.AssignedAt
                    };
                })
                .OrderBy(x => x.DeviceType)
                .ThenBy(x => x.DeviceName)
                .ToList();
        }

        private IQueryable<AdminUserDto> ProjectAdminUsers()
        {
            return _db.Users.Select(u => new AdminUserDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                Address = u.Address,
                Role = u.Role == 1 ? "admin" : "user",
                AccountType = u.AccountType == AccountTypes.Premium
                    ? AccountTypes.Premium
                    : AccountTypes.Standard,
                LastActiveAt = u.LastActiveAt,
                DeactivatedAt = u.DeactivatedAt
            });
        }

        private static AdminUserDto ToAdminUserDto(AppUser user)
        {
            return new AdminUserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                Role = user.Role == 1 ? "admin" : "user",
                AccountType = AccountTypes.Normalize(user.AccountType),
                LastActiveAt = user.LastActiveAt,
                DeactivatedAt = user.DeactivatedAt
            };
        }

        private async Task<bool> IsAdminAsync()
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            return profile?.Role == "admin";
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
