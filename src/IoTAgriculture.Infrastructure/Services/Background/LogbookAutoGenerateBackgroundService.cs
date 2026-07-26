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
            var startupUtc = DateTimeOffset.UtcNow;
            var startupVn = ToVietnamTime(startupUtc);
            var scheduledTodayVn = GetScheduledRunVn(startupVn);
            if (startupVn >= scheduledTodayVn)
            {
                await ExportForDateAsync(
                    DateOnly.FromDateTime(startupVn.Date),
                    scheduledTodayVn,
                    "startup catch-up",
                    stoppingToken);
            }

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
                    await ExportForDateAsync(
                        date,
                        GetScheduledRunVn(runVn),
                        "scheduled run",
                        stoppingToken);
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

        private async Task ExportForDateAsync(
            DateOnly date,
            DateTimeOffset fileTimestampVn,
            string trigger,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    "Starting automatic logbook Excel export. Trigger: {Trigger}; RunUtc: {RunUtc}; RunVn: {RunVn}; Date: {Date}.",
                    trigger,
                    DateTimeOffset.UtcNow,
                    ToVietnamTime(DateTimeOffset.UtcNow),
                    date);

                using var scope = _scopeFactory.CreateScope();
                var logbookService = scope.ServiceProvider.GetRequiredService<ILogbookService>();
                var filePath = await logbookService.ExportDailyLogbookAsync(
                    date,
                    fileTimestampVn,
                    cancellationToken);

                _logger.LogInformation(
                    "Completed automatic logbook Excel export. Date: {Date}; File: {FilePath}.",
                    date,
                    filePath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to automatically export logbook Excel for {Date}.",
                    date);
            }
        }

        private DateTimeOffset GetNextRunUtc(DateTimeOffset nowUtc)
        {
            var vietnamOffset = TimeSpan.FromHours(VietnamUtcOffsetHours);
            var nowVn = nowUtc.ToOffset(vietnamOffset);
            var nextRunVn = GetScheduledRunVn(nowVn);

            if (nowVn >= nextRunVn)
            {
                nextRunVn = nextRunVn.AddDays(1);
            }

            return nextRunVn.ToUniversalTime();
        }

        private DateTimeOffset GetScheduledRunVn(DateTimeOffset localDate)
        {
            var hourVn = Math.Clamp(
                _configuration.GetValue("LogbookAutoGenerate:HourVn", 17),
                0,
                23);
            var minuteVn = Math.Clamp(
                _configuration.GetValue("LogbookAutoGenerate:MinuteVn", 0),
                0,
                59);
            return new DateTimeOffset(
                localDate.Year,
                localDate.Month,
                localDate.Day,
                hourVn,
                minuteVn,
                0,
                TimeSpan.FromHours(VietnamUtcOffsetHours));
        }

        private static DateTimeOffset ToVietnamTime(DateTimeOffset dateTime)
        {
            return dateTime.ToOffset(TimeSpan.FromHours(VietnamUtcOffsetHours));
        }
    }
}
