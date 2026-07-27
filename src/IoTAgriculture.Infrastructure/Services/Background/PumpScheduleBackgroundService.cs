using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class PumpScheduleBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AutomationEngineHealth _health;
        private readonly ILogger<PumpScheduleBackgroundService> _logger;
        private readonly TimeSpan _automationInterval;
        private readonly TimeSpan _thresholdPollingInterval;

        public PumpScheduleBackgroundService(
            IServiceScopeFactory scopeFactory,
            AutomationEngineHealth health,
            ILogger<PumpScheduleBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _health = health;
            _logger = logger;
            _automationInterval = TimeSpan.FromSeconds(Math.Clamp(
                configuration.GetValue("Automation:TickSeconds", 10),
                1,
                30));
            _thresholdPollingInterval = TimeSpan.FromSeconds(Math.Clamp(
                configuration.GetValue("Automation:ThresholdPollSeconds", 5),
                1,
                60));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _health.MarkStarted();
            _logger.LogInformation(
                "Pump automation engine started. Fixed schedule interval: {AutomationIntervalSeconds} seconds; smart threshold polling interval: {ThresholdPollingIntervalSeconds} seconds; monitoring interval: 30 seconds.",
                _automationInterval.TotalSeconds,
                _thresholdPollingInterval.TotalSeconds);
            // Automation is intentionally isolated from slower history/alert work.
            // This keeps durationSeconds accurate without flooding sensor history.
            return Task.WhenAll(
                RunAutomationLoopAsync(stoppingToken),
                RunThresholdPollingLoopAsync(stoppingToken),
                RunMonitoringLoopAsync(stoppingToken));
        }

        private async Task RunThresholdPollingLoopAsync(
            CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_thresholdPollingInterval);
            do
            {
                await RunScopedStepAsync(
                    "process smart irrigation thresholds",
                    provider => provider
                        .GetRequiredService<IDeviceService>()
                        .ProcessThresholdAutomationAsync(stoppingToken),
                    stoppingToken);
            }
            while (await WaitForNextTickAsync(timer, stoppingToken));
        }

        private async Task RunAutomationLoopAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_automationInterval);
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
                    _logger.LogInformation(
                        "Pump automation engine tick succeeded at {TickAt}.",
                        now);
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
