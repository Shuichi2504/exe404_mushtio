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
    }
}
