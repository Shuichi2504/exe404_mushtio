using System.Text.Json;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs.Admin;
using IoTAgriculture.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Services
{
    public class AdminService : IAdminService
    {
        private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(2);
        private readonly IoTDbContext _db;
        private readonly IFirebaseRtdbService _firebase;

        public AdminService(IoTDbContext db, IFirebaseRtdbService firebase)
        {
            _db = db;
            _firebase = firebase;
        }

        public async Task<AdminDashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
        {
            var devices = await ReadFirebaseDevicesAsync(cancellationToken);
            return new AdminDashboardStatsDto
            {
                TotalUsers = await _db.Users.CountAsync(cancellationToken),
                TotalDevices = devices.Count,
                OnlineDevices = devices.Count(x => x.IsOnline),
                OfflineDevices = devices.Count(x => !x.IsOnline)
            };
        }

        public async Task<List<FirebaseDeviceDto>> ReadFirebaseDevicesAsync(CancellationToken cancellationToken = default)
        {
            var raw = await _firebase.GetAsync<Dictionary<string, JsonElement>>("devices", cancellationToken)
                ?? new Dictionary<string, JsonElement>();

            return raw
                .Where(kvp => kvp.Value.ValueKind == JsonValueKind.Object)
                .Select(kvp =>
                {
                    var json = kvp.Value;
                    var lastSeen = ReadTimestamp(json);
                    return new FirebaseDeviceDto
                    {
                        DeviceKey = kvp.Key,
                        DeviceName = ReadString(json, "device_name")
                            ?? ReadString(json, "deviceName")
                            ?? kvp.Key,
                        DeviceType = ResolveDeviceType(json),
                        LastSeenAt = lastSeen?.ToString("O"),
                        IsOnline = lastSeen != null && DateTimeOffset.UtcNow - lastSeen <= OfflineAfter
                    };
                })
                .OrderBy(x => x.DeviceType)
                .ThenBy(x => x.DeviceName)
                .ToList();
        }

        private static string ResolveDeviceType(JsonElement json)
        {
            if (HasProperty(json, "relay1") || HasProperty(json, "relay2"))
            {
                return "pump";
            }

            if (HasProperty(json, "temperature") ||
                HasProperty(json, "humidity") ||
                HasProperty(json, "air_quality") ||
                HasProperty(json, "airQuality") ||
                HasProperty(json, "air_quanlity") ||
                HasProperty(json, "air_status") ||
                HasProperty(json, "airStatus"))
            {
                return "sensor";
            }

            return "device";
        }

        private static bool HasProperty(JsonElement json, string name)
        {
            return json.TryGetProperty(name, out _);
        }

        private static string? ReadString(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();
        }

        private static DateTimeOffset? ReadTimestamp(JsonElement json)
        {
            var raw = ReadString(json, "timestamp")
                ?? ReadString(json, "lastActionAt")
                ?? ReadString(json, "lastSeenAt");

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (long.TryParse(raw, out var numeric))
            {
                try
                {
                    return numeric < 1_000_000_000_000
                        ? DateTimeOffset.FromUnixTimeSeconds(numeric)
                        : DateTimeOffset.FromUnixTimeMilliseconds(numeric);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return DateTimeOffset.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : null;
        }
    }
}
