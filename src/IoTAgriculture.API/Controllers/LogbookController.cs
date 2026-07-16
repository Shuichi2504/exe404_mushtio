using System.Globalization;
using System.Text;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/logbooks")]
    public class LogbookController : ControllerBase
    {
        private readonly ILogbookService _service;
        private readonly IAuthService _authService;
        private readonly IoTDbContext _db;

        public LogbookController(
            ILogbookService service,
            IAuthService authService,
            IoTDbContext db)
        {
            _service = service;
            _authService = authService;
            _db = db;
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetToday(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);

            return filtered == null ? Unauthorized() : Ok(filtered);
        }

        [HttpPost("today/generate")]
        public async Task<IActionResult> GenerateToday(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);

            return filtered == null ? Unauthorized() : Ok(filtered);
        }

        [HttpGet("today/csv")]
        public async Task<IActionResult> ExportTodayCsv(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);
            if (filtered == null) return Unauthorized();

            return File(
                Encoding.UTF8.GetBytes(BuildCsv(filtered)),
                "text/csv; charset=utf-8",
                $"logbook-{filtered.Date}.csv");
        }

        [HttpGet("{date}")]
        public async Task<IActionResult> GetByDate(string date, CancellationToken cancellationToken)
        {
            if (!DateOnly.TryParse(date, out var parsed))
            {
                return BadRequest(new { message = "Date must be yyyy-MM-dd." });
            }

            var logbook = parsed == TodayInVietnam()
                ? await _service.GenerateDailyLogbookAsync(parsed, cancellationToken)
                : await _service.GetDailyLogbookAsync(parsed, cancellationToken)
                    ?? await _service.GenerateDailyLogbookAsync(parsed, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);

            return filtered == null ? Unauthorized() : Ok(filtered);
        }

        [HttpPost("{date}/generate")]
        public async Task<IActionResult> GenerateByDate(string date, CancellationToken cancellationToken)
        {
            if (!DateOnly.TryParse(date, out var parsed))
            {
                return BadRequest(new { message = "Date must be yyyy-MM-dd." });
            }

            var logbook = await _service.GenerateDailyLogbookAsync(parsed, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);

            return filtered == null ? Unauthorized() : Ok(filtered);
        }

        [HttpGet("{date}/csv")]
        public async Task<IActionResult> ExportCsv(string date, CancellationToken cancellationToken)
        {
            if (!DateOnly.TryParse(date, out var parsed))
            {
                return BadRequest(new { message = "Date must be yyyy-MM-dd." });
            }

            var logbook = parsed == TodayInVietnam()
                ? await _service.GenerateDailyLogbookAsync(parsed, cancellationToken)
                : await _service.GetDailyLogbookAsync(parsed, cancellationToken)
                    ?? await _service.GenerateDailyLogbookAsync(parsed, cancellationToken);
            var filtered = await FilterForCurrentUserAsync(logbook, cancellationToken);
            if (filtered == null) return Unauthorized();

            return File(
                Encoding.UTF8.GetBytes(BuildCsv(filtered)),
                "text/csv; charset=utf-8",
                $"logbook-{filtered.Date}.csv");
        }

        private async Task<DailyLogbookDto?> FilterForCurrentUserAsync(
            DailyLogbookDto logbook,
            CancellationToken cancellationToken)
        {
            var profile = await _authService.GetProfileAsync(ReadBearerToken());
            if (profile == null) return null;
            if (string.Equals(profile.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return logbook;
            }

            var keys = await _db.UserDevices
                .Where(x => x.UserId == profile.UserId)
                .Select(x => x.DeviceKey)
                .ToListAsync(cancellationToken);
            var allowed = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);

            return new DailyLogbookDto
            {
                Date = logbook.Date,
                GeneratedAt = logbook.GeneratedAt,
                GeneratedLocal = logbook.GeneratedLocal,
                Records = logbook.Records
                    .Where(x => allowed.Contains(x.DeviceKey))
                    .ToList()
            };
        }

        private string ReadBearerToken()
        {
            var header = Request.Headers.Authorization.ToString();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : string.Empty;
        }

        private static string BuildCsv(DailyLogbookDto logbook)
        {
            var builder = new StringBuilder();
            builder.AppendLine("timestamp,local_time,period_start_local,period_end_local,device_key,device_name,min_temperature,max_temperature,min_humidity,max_humidity,min_air_quality,max_air_quality,min_soil_moisture,max_soil_moisture,ground_temperature,top_temperature,ground_humidity,top_humidity");

            foreach (var record in logbook.Records)
            {
                builder.Append(Escape(record.Timestamp)).Append(',')
                    .Append(Escape(record.LocalTime)).Append(',')
                    .Append(Escape(record.PeriodStartLocal)).Append(',')
                    .Append(Escape(record.PeriodEndLocal)).Append(',')
                    .Append(Escape(record.DeviceKey)).Append(',')
                    .Append(Escape(record.DeviceName)).Append(',')
                    .Append(Format(record.MinTemperature)).Append(',')
                    .Append(Format(record.MaxTemperature)).Append(',')
                    .Append(Format(record.MinHumidity)).Append(',')
                    .Append(Format(record.MaxHumidity)).Append(',')
                    .Append(Format(record.MinAirQuality)).Append(',')
                    .Append(Format(record.MaxAirQuality)).Append(',')
                    .Append(Format(record.MinSoilMoisture)).Append(',')
                    .Append(Format(record.MaxSoilMoisture)).Append(',')
                    .Append(Format(record.GroundTemperature)).Append(',')
                    .Append(Format(record.TopTemperature)).Append(',')
                    .Append(Format(record.GroundHumidity)).Append(',')
                    .Append(Format(record.TopHumidity))
                    .AppendLine();
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!needsQuotes)
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private static string Format(double? value)
        {
            return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static DateOnly TodayInVietnam()
        {
            return DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        }
    }
}
