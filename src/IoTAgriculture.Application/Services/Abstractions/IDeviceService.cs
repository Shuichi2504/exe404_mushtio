using IoTAgriculture.DTOs.Firebase;

namespace IoTAgriculture.Services.Interfaces
{
    public interface IDeviceService
    {
        Task<PumpStateDto?> GetPumpStateAsync(string pumpKey);
        Task SetRelayAsync(
            string pumpKey,
            string relayKey,
            bool value,
            string source = "manual",
            string? actorUserId = null,
            string? actorName = null,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PumpLogEntryDto>> GetPumpLogsAsync(string pumpKey, int limit = 50);
        Task<AutoIrrigationScheduleDto?> GetScheduleAsync(string pumpKey, string relayKey);
        Task<AutoIrrigationScheduleDto> SaveScheduleAsync(
            string pumpKey,
            string relayKey,
            UpsertAutoIrrigationScheduleDto dto);
        Task ProcessAutomationAsync(CancellationToken cancellationToken = default);
    }
}
