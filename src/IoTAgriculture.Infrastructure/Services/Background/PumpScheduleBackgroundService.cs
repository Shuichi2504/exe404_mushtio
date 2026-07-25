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

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Automation is intentionally isolated from slower history/alert work.
            // This keeps durationSeconds accurate without flooding sensor history.
            return Task.WhenAll(
                RunAutomationLoopAsync(stoppingToken),
                RunMonitoringLoopAsync(stoppingToken));
        }

        private async Task RunAutomationLoopAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                await RunScopedStepAsync(
                    "process pump automation",
                    provider => provider
                        .GetRequiredService<IDeviceService>()
                        .ProcessAutomationAsync(stoppingToken),
                    stoppingToken);
            }
            while (await WaitForNextTickAsync(timer, stoppingToken));
        }

        private async Task RunMonitoringLoopAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                await RunScopedStepAsync(
                    "capture sensor history",
                    provider => provider
                        .GetRequiredService<ILogbookService>()
                        .CaptureSensorSnapshotsAsync(stoppingToken),
                    stoppingToken);
                await RunScopedStepAsync(
                    "process alerts",
                    provider => provider
                        .GetRequiredService<IAlertService>()
                        .ProcessAlertsAsync(stoppingToken),
                    stoppingToken);
            }
            while (await WaitForNextTickAsync(timer, stoppingToken));
        }

        private async Task RunScopedStepAsync(
            string step,
            Func<IServiceProvider, Task> action,
            CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {BackgroundStep}.", step);
            }
        }

        private static async Task<bool> WaitForNextTickAsync(
            PeriodicTimer timer,
            CancellationToken stoppingToken)
        {
            try
            {
                return await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
