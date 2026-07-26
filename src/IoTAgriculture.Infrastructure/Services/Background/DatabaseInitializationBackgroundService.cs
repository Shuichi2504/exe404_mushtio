namespace IoTAgriculture.Services
{
    public sealed class DatabaseInitializationBackgroundService : BackgroundService
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly DatabaseInitializationHealth _health;
        private readonly ILogger<DatabaseInitializationBackgroundService> _logger;

        public DatabaseInitializationBackgroundService(
            IServiceProvider services,
            IConfiguration configuration,
            DatabaseInitializationHealth health,
            ILogger<DatabaseInitializationBackgroundService> logger)
        {
            _services = services;
            _configuration = configuration;
            _health = health;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var connectionString =
                    _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    const string message =
                        "Missing ConnectionStrings:DefaultConnection. Authentication is unavailable, but Firebase automation remains active.";
                    _health.MarkFailure(message);
                    _logger.LogError(message);
                }
                else
                {
                    try
                    {
                        _health.MarkAttempt();
                        await AuthSchemaInitializer.InitializeAsync(_services);
                        _health.MarkSuccess();
                        _logger.LogInformation(
                            "Authentication database schema is ready.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _health.MarkFailure(ex.GetBaseException().Message);
                        _logger.LogError(
                            ex,
                            "Authentication database initialization failed. Retrying in {RetrySeconds} seconds; Firebase automation continues running.",
                            RetryDelay.TotalSeconds);
                    }
                }

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
