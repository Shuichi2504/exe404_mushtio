using System.Globalization;
using System.Text;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IoTAgriculture.Controllers
{
    [ApiController]
    [Route("api/logbooks")]
    public class LogbookController : ControllerBase
    {
        private readonly ILogbookService _service;

        public LogbookController(ILogbookService service)
        {
            _service = service;
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetToday(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);

            return Ok(logbook);
        }

        [HttpPost("today/generate")]
        public async Task<IActionResult> GenerateToday(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);

            return Ok(logbook);
        }

        [HttpGet("today/csv")]
        public async Task<IActionResult> ExportTodayCsv(CancellationToken cancellationToken)
        {
            var today = TodayInVietnam();
            var logbook = await _service.GenerateDailyLogbookAsync(today, cancellationToken);

            return File(
                Encoding.UTF8.GetBytes(BuildCsv(logbook)),
                "text/csv; charset=utf-8",
                $"logbook-{logbook.Date}.csv");
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

            return Ok(logbook);
        }

        [HttpPost("{date}/generate")]
        public async Task<IActionResult> GenerateByDate(string date, CancellationToken cancellationToken)
        {
            if (!DateOnly.TryParse(date, out var parsed))
            {
                return BadRequest(new { message = "Date must be yyyy-MM-dd." });
            }

            var logbook = await _service.GenerateDailyLogbookAsync(parsed, cancellationToken);

            return Ok(logbook);
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

            return File(
                Encoding.UTF8.GetBytes(BuildCsv(logbook)),
                "text/csv; charset=utf-8",
                $"logbook-{logbook.Date}.csv");
        }

        private static string BuildCsv(DailyLogbookDto logbook)
        {
            var builder = new StringBuilder();
            builder.AppendLine("timestamp,local_time,device_key,device_name,temperature,humidity,air_quality,ground_temperature,top_temperature,ground_humidity,top_humidity,soil_moisture");

            foreach (var record in logbook.Records)
            {
                builder.Append(Escape(record.Timestamp)).Append(',')
                    .Append(Escape(record.LocalTime)).Append(',')
                    .Append(Escape(record.PeriodStartLocal)).Append(',')
                    .Append(Escape(record.PeriodEndLocal)).Append(',')
                    .Append(Escape(record.DeviceKey)).Append(',')
                    .Append(Escape(record.DeviceName)).Append(',')
                    .Append(Format(record.Temperature)).Append(',')
                    .Append(Format(record.MinTemperature)).Append(',')
                    .Append(Format(record.MaxTemperature)).Append(',')
                    .Append(Format(record.Humidity)).Append(',')
                    .Append(Format(record.AirQuality)).Append(',')
                    .Append(Format(record.GroundTemperature)).Append(',')
                    .Append(Format(record.TopTemperature)).Append(',')
                    .Append(Format(record.GroundHumidity)).Append(',')
                    .Append(Format(record.TopHumidity)).Append(',')
                    .Append(Format(record.SoilMoisture)).Append(',')
                    .Append(Format(record.MinSoilMoisture)).Append(',')
                    .Append(Format(record.MaxSoilMoisture))
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
