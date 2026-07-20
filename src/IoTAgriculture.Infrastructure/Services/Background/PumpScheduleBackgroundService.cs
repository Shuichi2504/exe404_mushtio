using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class PumpScheduleBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PumpScheduleBackgroundService> _logger;

        public PumpScheduleBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<PumpScheduleBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();
                    await deviceService.ProcessSchedulesAsync(stoppingToken);
                    await deviceService.ProcessSmartIrrigationAsync(stoppingToken);

                    var logbookService = scope.ServiceProvider.GetRequiredService<ILogbookService>();
                    await logbookService.CaptureSensorSnapshotsAsync(stoppingToken);

                    var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
                    await alertService.ProcessAlertsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process pump schedules.");
                }

                try
                {
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
