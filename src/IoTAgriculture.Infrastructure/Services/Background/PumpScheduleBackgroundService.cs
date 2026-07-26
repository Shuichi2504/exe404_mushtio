using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class PumpScheduleBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AutomationEngineHealth _health;
        private readonly ILogger<PumpScheduleBackgroundService> _logger;
        private DateTimeOffset _nextSuccessLogAt = DateTimeOffset.MinValue;

        public PumpScheduleBackgroundService(
            IServiceScopeFactory scopeFactory,
            AutomationEngineHealth health,
            ILogger<PumpScheduleBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _health = health;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _health.MarkStarted();
            _logger.LogInformation(
                "Pump automation engine started. Automation interval: 1 second; monitoring interval: 30 seconds.");
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
                    stoppingToken,
                    trackAutomationHealth: true);
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
            CancellationToken stoppingToken,
            bool trackAutomationHealth = false)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider);
                if (trackAutomationHealth)
                {
                    _health.MarkSuccess();
                    var now = DateTimeOffset.UtcNow;
                    if (now >= _nextSuccessLogAt)
                    {
                        _nextSuccessLogAt = now.AddSeconds(30);
                        _logger.LogInformation(
                            "Pump automation engine tick succeeded at {TickAt}.",
                            now);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
            catch (Exception ex)
            {
                if (trackAutomationHealth)
                {
                    _health.MarkFailure(ex);
                }
                _logger.LogError(ex, "Failed to {BackgroundStep}.", step);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _health.MarkStopped();
            _logger.LogInformation("Pump automation engine stopped.");
            await base.StopAsync(cancellationToken);
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
