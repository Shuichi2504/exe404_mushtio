namespace IoTAgriculture.Services
{
    public sealed class AutomationEngineHealth
    {
        private readonly object _sync = new();
        private string _status = "starting";
        private DateTimeOffset? _startedAt;
        private DateTimeOffset? _lastTickAt;
        private DateTimeOffset? _lastSuccessAt;
        private DateTimeOffset? _lastErrorAt;
        private string? _lastError;
        private long _tickCount;
        private int _consecutiveFailures;

        public void MarkStarted()
        {
            lock (_sync)
            {
                _status = "running";
                _startedAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkSuccess()
        {
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                _status = "running";
                _lastTickAt = now;
                _lastSuccessAt = now;
                _lastError = null;
                _tickCount++;
                _consecutiveFailures = 0;
            }
        }

        public void MarkFailure(Exception exception)
        {
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                _status = "error";
                _lastTickAt = now;
                _lastErrorAt = now;
                _lastError = exception.GetBaseException().Message;
                _tickCount++;
                _consecutiveFailures++;
            }
        }

        public void MarkStopped()
        {
            lock (_sync)
            {
                _status = "stopped";
            }
        }

        public AutomationEngineHealthSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new AutomationEngineHealthSnapshot(
                    _status,
                    _startedAt,
                    _lastTickAt,
                    _lastSuccessAt,
                    _lastErrorAt,
                    _lastError,
                    _tickCount,
                    _consecutiveFailures);
            }
        }
    }

    public sealed record AutomationEngineHealthSnapshot(
        string Status,
        DateTimeOffset? StartedAt,
        DateTimeOffset? LastTickAt,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastErrorAt,
        string? LastError,
        long TickCount,
        int ConsecutiveFailures);

    public sealed class DatabaseInitializationHealth
    {
        private readonly object _sync = new();
        private string _status = "starting";
        private DateTimeOffset? _lastAttemptAt;
        private DateTimeOffset? _initializedAt;
        private string? _lastError;

        public void MarkAttempt() 
        {
            lock (_sync)
            {
                _status = "initializing";
                _lastAttemptAt = DateTimeOffset.UtcNow;
            }
        }

        public void MarkSuccess()
        {
            lock (_sync)
            {
                _status = "ready";
                _initializedAt = DateTimeOffset.UtcNow;
                _lastError = null;
            }
        }

        public void MarkFailure(string error)
        {
            lock (_sync)
            {
                _status = "error";
                _lastError = error;
            }
        }

        public DatabaseInitializationHealthSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new DatabaseInitializationHealthSnapshot(
                    _status,
                    _lastAttemptAt,
                    _initializedAt,
                    _lastError);
            }
        }
    }

    public sealed record DatabaseInitializationHealthSnapshot(
        string Status,
        DateTimeOffset? LastAttemptAt,
        DateTimeOffset? InitializedAt,
        string? LastError);
}
