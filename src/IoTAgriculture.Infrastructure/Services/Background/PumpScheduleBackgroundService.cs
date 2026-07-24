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
                    var logbookService = scope.ServiceProvider.GetRequiredService<ILogbookService>();
                    await RunStepAsync(
                        "capture sensor history",
                        () => logbookService.CaptureSensorSnapshotsAsync(stoppingToken),
                        stoppingToken);

                    var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();
                    await RunStepAsync(
                        "process pump schedules",
                        () => deviceService.ProcessSchedulesAsync(stoppingToken),
                        stoppingToken);
                    await RunStepAsync(
                        "process smart irrigation",
                        () => deviceService.ProcessSmartIrrigationAsync(stoppingToken),
                        stoppingToken);

                    var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
                    await RunStepAsync(
                        "process alerts",
                        () => alertService.ProcessAlertsAsync(stoppingToken),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process the sensor and pump background cycle.");
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

        private async Task RunStepAsync(
            string step,
            Func<Task> action,
            CancellationToken stoppingToken)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {BackgroundStep}.", step);
            }
        }
    }
}
