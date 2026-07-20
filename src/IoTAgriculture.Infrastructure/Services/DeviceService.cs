using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class DeviceService : IDeviceService
    {
        private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
        private readonly IFirebaseRtdbService _firebase;

        public DeviceService(IFirebaseRtdbService firebase)
        {
            _firebase = firebase;
        }

        public Task<PumpStateDto?> GetPumpStateAsync(string pumpKey)
        {
            return _firebase.GetAsync<PumpStateDto>($"devices/{pumpKey}");
        }

        public async Task SetRelayAsync(
            string pumpKey,
            string relayKey,
            bool value,
            string source = "manual",
            string? actorUserId = null,
            string? actorName = null,
            CancellationToken cancellationToken = default)
        {
            var cleanRelay = relayKey.Trim();
            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);

            await _firebase.SetAsync($"devices/{pumpKey}/{cleanRelay}", value, cancellationToken);
            await _firebase.PatchAsync(
                $"devices/{pumpKey}",
                new
                {
                    timestamp = nowUtc.ToUnixTimeSeconds().ToString(),
                    lastActionAt = nowUtc.ToString("O"),
                    lastActionLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                    lastActionSource = source,
                    lastActionBy = actorName ?? "System"
                },
                cancellationToken);

            await _firebase.PushAsync(
                $"pumpLogs/{pumpKey}",
                new PumpLogEntryDto
                {
                    PumpKey = pumpKey,
                    RelayKey = cleanRelay,
                    Value = value,
                    Action = value ? "ON" : "OFF",
                    Source = source,
                    ActorUserId = actorUserId,
                    ActorName = actorName ?? "System",
                    Timestamp = nowUtc.ToUnixTimeMilliseconds(),
                    UtcTime = nowUtc.ToString("O"),
                    LocalTime = nowLocal.ToString("yyyy-MM-dd HH:mm:ss")
                },
                cancellationToken);
        }

        public async Task<IReadOnlyList<PumpLogEntryDto>> GetPumpLogsAsync(string pumpKey, int limit = 50)
        {
            var raw = await _firebase.GetAsync<Dictionary<string, PumpLogEntryDto>>($"pumpLogs/{pumpKey}")
                ?? new Dictionary<string, PumpLogEntryDto>();

            return raw
                .Select(kvp =>
                {
                    var item = kvp.Value ?? new PumpLogEntryDto();
                    item.PumpKey = string.IsNullOrWhiteSpace(item.PumpKey) ? pumpKey : item.PumpKey;
                    return item;
                })
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Clamp(limit, 1, 200))
                .ToList();
        }

        public Task<AutoIrrigationScheduleDto?> GetScheduleAsync(string pumpKey, string relayKey)
        {
            return _firebase.GetAsync<AutoIrrigationScheduleDto>($"pumpSchedules/{pumpKey}/{relayKey.Trim()}");
        }

        public async Task<AutoIrrigationScheduleDto> SaveScheduleAsync(
            string pumpKey,
            string relayKey,
            UpsertAutoIrrigationScheduleDto dto)
        {
            var cleanRelay = relayKey.Trim();
            var existing = await GetScheduleAsync(pumpKey, cleanRelay);
            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
            var schedule = new AutoIrrigationScheduleDto
            {
                PumpKey = pumpKey,
                RelayKey = cleanRelay,
                Enabled = dto.Enabled,
                IntervalMinutes = dto.IntervalMinutes,
                DurationMinutes = dto.DurationMinutes,
                StartTime = dto.StartTime,
                SmartEnabled = dto.SmartEnabled,
                SensorKey = string.IsNullOrWhiteSpace(dto.SensorKey) ? existing?.SensorKey : dto.SensorKey.Trim(),
                MaxDurationMinutes = dto.MaxDurationMinutes,
                CooldownMinutes = dto.CooldownMinutes,
                LastRunAt = existing?.LastRunAt,
                LastRunLocal = existing?.LastRunLocal,
                ActiveUntilAt = existing?.ActiveUntilAt,
                ActiveUntilLocal = existing?.ActiveUntilLocal,
                LastSmartRunAt = existing?.LastSmartRunAt,
                LastSmartRunLocal = existing?.LastSmartRunLocal,
                UpdatedAt = nowUtc.ToString("O"),
                UpdatedLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss")
            };

            if (schedule.Enabled)
            {
                var nextRun = CalculateNextRun(schedule, nowLocal.DateTime);
                schedule.NextRunAt = ToUtcOffset(nextRun).ToString("O");
                schedule.NextRunLocal = nextRun.ToString("yyyy-MM-dd HH:mm:ss");
            }

            await _firebase.SetAsync($"pumpSchedules/{pumpKey}/{schedule.RelayKey}", schedule);
            return schedule;
        }

        public async Task ProcessSchedulesAsync(CancellationToken cancellationToken = default)
        {
            var schedules = await _firebase.GetAsync<Dictionary<string, Dictionary<string, AutoIrrigationScheduleDto>>>(
                "pumpSchedules",
                cancellationToken);

            if (schedules == null || schedules.Count == 0)
            {
                return;
            }

            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, VietnamTimeZone).DateTime;
            foreach (var pumpEntry in schedules)
            {
                var pumpKey = pumpEntry.Key;
                if (pumpEntry.Value == null)
                {
                    continue;
                }

                foreach (var relayEntry in pumpEntry.Value)
                {
                    var schedule = relayEntry.Value;
                    if (schedule == null || !schedule.Enabled)
                    {
                        continue;
                    }

                    schedule.PumpKey = pumpKey;
                    schedule.RelayKey = string.IsNullOrWhiteSpace(schedule.RelayKey)
                        ? relayEntry.Key
                        : schedule.RelayKey;

                    var activeUntilLocal = ParseLocalDateTime(schedule.ActiveUntilLocal);
                    if (activeUntilLocal != null)
                    {
                        if (activeUntilLocal <= nowLocal)
                        {
                            await SetRelayAsync(
                                pumpKey,
                                schedule.RelayKey,
                                false,
                                "schedule",
                                cancellationToken: cancellationToken);
                            schedule.ActiveUntilAt = null;
                            schedule.ActiveUntilLocal = null;
                            await _firebase.SetAsync(
                                $"pumpSchedules/{pumpKey}/{schedule.RelayKey}",
                                schedule,
                                cancellationToken);
                        }

                        continue;
                    }

                    var nextRunLocal = ParseLocalDateTime(schedule.NextRunLocal);
                    if (nextRunLocal == null)
                    {
                        nextRunLocal = CalculateNextRun(schedule, nowLocal);
                    }

                    if (nextRunLocal > nowLocal)
                    {
                        continue;
                    }

                    await SetRelayAsync(
                        pumpKey,
                        schedule.RelayKey,
                        true,
                        "schedule",
                        cancellationToken: cancellationToken);
                    var stopAtLocal = nowLocal.AddMinutes(schedule.DurationMinutes);
                    schedule.LastRunAt = ToUtcOffset(nowLocal).ToString("O");
                    schedule.LastRunLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    schedule.ActiveUntilAt = ToUtcOffset(stopAtLocal).ToString("O");
                    schedule.ActiveUntilLocal = stopAtLocal.ToString("yyyy-MM-dd HH:mm:ss");

                    // Move next run strictly forward so the scheduler won't retrigger the same slot.
                    var recomputeFrom = nowLocal.AddMinutes(schedule.IntervalMinutes);
                    var nextRun = CalculateNextRun(schedule, recomputeFrom);
                    if (nextRun < stopAtLocal)
                    {
                        nextRun = stopAtLocal;
                    }
                    schedule.NextRunAt = ToUtcOffset(nextRun).ToString("O");
                    schedule.NextRunLocal = nextRun.ToString("yyyy-MM-dd HH:mm:ss");

                    await _firebase.SetAsync(
                        $"pumpSchedules/{pumpKey}/{schedule.RelayKey}",
                        schedule,
                        cancellationToken);
                }
            }
        }

        public Task ProcessSmartIrrigationAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        private static DateTime CalculateNextRun(AutoIrrigationScheduleDto schedule, DateTime referenceLocal)
        {
            var start = ParseTimeOfDay(schedule.StartTime);
            var firstRunToday = referenceLocal.Date.Add(start);

            if (!schedule.Enabled)
            {
                return firstRunToday;
            }

            if (referenceLocal <= firstRunToday)
            {
                return firstRunToday;
            }

            var interval = Math.Max(1, schedule.IntervalMinutes);
            var elapsedMinutes = (referenceLocal - firstRunToday).TotalMinutes;
            var cycles = Math.Ceiling(elapsedMinutes / interval);
            return firstRunToday.AddMinutes(cycles * interval);
        }

        private static TimeSpan ParseTimeOfDay(string? raw)
        {
            return TimeSpan.TryParse(raw, out var parsed) ? parsed : new TimeSpan(6, 0, 0);
        }

        private static DateTime? ParseLocalDateTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return DateTime.TryParse(raw, out var parsed) ? parsed : null;
        }

        private static DateTimeOffset ToUtcOffset(DateTime localTime)
        {
            var offset = VietnamTimeZone.GetUtcOffset(localTime);
            return new DateTimeOffset(localTime, offset).ToUniversalTime();
        }

        private static TimeZoneInfo ResolveVietnamTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
        }
    }
}
