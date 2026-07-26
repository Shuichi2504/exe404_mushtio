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

        Task<string> ExportDailyLogbookAsync(
            DateOnly date,
            DateTimeOffset fileTimestampLocal,
            CancellationToken cancellationToken = default);

        byte[] CreateExcelWorkbook(DailyLogbookDto logbook);

        IReadOnlyList<string> GetAutoExportFileNames();
    }
}
