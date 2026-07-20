using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class LogbookAutoGenerateBackgroundService : BackgroundService
    {
        private const int VietnamUtcOffsetHours = 7;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LogbookAutoGenerateBackgroundService> _logger;
        private readonly IConfiguration _configuration;

        public LogbookAutoGenerateBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<LogbookAutoGenerateBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var nextRunUtc = GetNextRunUtc(DateTimeOffset.UtcNow);
                var delay = nextRunUtc - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                _logger.LogInformation(
                    "Next automatic logbook generation scheduled at {RunUtc} UTC ({RunVn} VN).",
                    nextRunUtc,
                    ToVietnamTime(nextRunUtc));

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var runUtc = DateTimeOffset.UtcNow;
                    var runVn = ToVietnamTime(runUtc);
                    var date = DateOnly.FromDateTime(runVn.Date);

                    _logger.LogInformation(
                        "Starting automatic logbook generation at {RunUtc} UTC ({RunVn} VN) for {Date}.",
                        runUtc,
                        runVn,
                        date);

                    using var scope = _scopeFactory.CreateScope();
                    var logbookService = scope.ServiceProvider.GetRequiredService<ILogbookService>();
                    var logbook = await logbookService.GenerateDailyLogbookAsync(date, stoppingToken);

                    var recordCount = logbook.Records.Count;
                    var deviceCount = logbook.Records
                        .Select(x => x.DeviceKey)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    var completedUtc = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "Completed automatic logbook generation at {RunUtc} UTC ({RunVn} VN) for {Date}. Records: {RecordCount}. Devices: {DeviceCount}.",
                        completedUtc,
                        ToVietnamTime(completedUtc),
                        logbook.Date,
                        recordCount,
                        deviceCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to automatically generate logbook.");
                }
            }
        }

        private DateTimeOffset GetNextRunUtc(DateTimeOffset nowUtc)
        {
            var hourVn = _configuration.GetValue("LogbookAutoGenerate:HourVn", 17);
            var minuteVn = _configuration.GetValue("LogbookAutoGenerate:MinuteVn", 0);
            hourVn = Math.Clamp(hourVn, 0, 23);
            minuteVn = Math.Clamp(minuteVn, 0, 59);

            var vietnamOffset = TimeSpan.FromHours(VietnamUtcOffsetHours);
            var nowVn = nowUtc.ToOffset(vietnamOffset);
            var nextRunVn = new DateTimeOffset(
                nowVn.Year,
                nowVn.Month,
                nowVn.Day,
                hourVn,
                minuteVn,
                0,
                vietnamOffset);

            if (nowVn >= nextRunVn)
            {
                nextRunVn = nextRunVn.AddDays(1);
            }

            return nextRunVn.ToUniversalTime();
        }

        private static DateTimeOffset ToVietnamTime(DateTimeOffset dateTime)
        {
            return dateTime.ToOffset(TimeSpan.FromHours(VietnamUtcOffsetHours));
        }
    }
}
