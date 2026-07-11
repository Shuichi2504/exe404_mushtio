namespace IoTAgriculture.Services.Interfaces
{
    public interface IFirebasePushNotificationService
    {
        Task SendDeviceAlertAsync(
            string deviceKey,
            string deviceName,
            string title,
            string body,
            string severity,
            CancellationToken cancellationToken = default);
    }
}
