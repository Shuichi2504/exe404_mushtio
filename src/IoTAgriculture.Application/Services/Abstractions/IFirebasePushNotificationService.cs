namespace IoTAgriculture.Services.Interfaces
{
    public interface IFirebasePushNotificationService
    {
        Task SendDeviceAlertAsync(
            string deviceKey,
            string deviceName,
            string alertType,
            string metric,
            string title,
            string body,
            string severity,
            double? value,
            double? threshold,
            CancellationToken cancellationToken = default);

        Task SendPumpStateChangedAsync(
            string deviceKey,
            string deviceName,
            bool isOn,
            string source,
            string actorName,
            string? reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
