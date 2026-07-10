using IoTAgriculture.DTOs.Firebase;

namespace IoTAgriculture.Services.Interfaces
{
    public interface ILogbookService
    {
        Task CaptureSensorSnapshotsAsync(CancellationToken cancellationToken = default);

        Task<DailyLogbookDto> GenerateDailyLogbookAsync(
            DateOnly date,
            CancellationToken cancellationToken = default);

        Task<DailyLogbookDto?> GetDailyLogbookAsync(
            DateOnly date,
            CancellationToken cancellationToken = default);

        Task GenerateTodayLogbookAsync(CancellationToken cancellationToken = default);

        Task<string?> ExportTodayLogbookAsync(CancellationToken cancellationToken = default);
    }
}
