using System.Collections.Concurrent;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class DeviceService : IDeviceService
    {
        private const string ScheduleSource = "schedule";
        private const string ThresholdSource = "threshold";
        private const string ScheduleActor = "Auto - Lịch tưới";
        private const string ThresholdActor = "Auto - Ngưỡng tưới";
        private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeviceLocks = new();
        private readonly IFirebaseRtdbService _firebase;
        private readonly ILogger<DeviceService> _logger;

        public DeviceService(
            IFirebaseRtdbService firebase,
            ILogger<DeviceService> logger)
        {
            _firebase = firebase;
            _logger = logger;
        }

        public Task<PumpStateDto?> GetPumpStateAsync(string pumpKey)
        {
            return _firebase.GetAsync<PumpStateDto>($"devices/{CleanKey(pumpKey, nameof(pumpKey))}");
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
            var cleanPump = CleanKey(pumpKey, nameof(pumpKey));
            var cleanRelay = CleanRelayKey(relayKey);
            var deviceLock = DeviceLocks.GetOrAdd(cleanPump, _ => new SemaphoreSlim(1, 1));
            await deviceLock.WaitAsync(cancellationToken);
            try
            {
                if (string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase) &&
                    cleanRelay == "relay2")
                {
                    // A manual command immediately releases ownership from either
                    // automation mode, so an old timer cannot turn a manual action off.
                    await _firebase.PatchAsync(
                        $"devices/{cleanPump}/schedule",
                        new Dictionary<string, object?>
                        {
                            ["activeUntilAt"] = null,
                            ["activeUntilLocal"] = null,
                            ["activeSource"] = null
                        },
                        cancellationToken);
                    await _firebase.PatchAsync(
                        $"pumpSchedules/{cleanPump}/relay2",
                        new Dictionary<string, object?>
                        {
                            ["activeUntilAt"] = null,
                            ["activeUntilLocal"] = null,
                            ["activeSource"] = null
                        },
                        cancellationToken);
                }

                await SetRelayIfChangedCoreAsync(
                    cleanPump,
                    cleanRelay,
                    value,
                    source,
                    actorUserId,
                    actorName ?? "System",
                    cancellationToken);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        public async Task<IReadOnlyList<PumpLogEntryDto>> GetPumpLogsAsync(
            string pumpKey,
            int limit = 50)
        {
            var cleanPump = CleanKey(pumpKey, nameof(pumpKey));
            var raw = await _firebase.GetAsync<Dictionary<string, PumpLogEntryDto>>(
                    $"pumpLogs/{cleanPump}")
                ?? new Dictionary<string, PumpLogEntryDto>();

            return raw
                .Select(kvp =>
                {
                    var item = kvp.Value ?? new PumpLogEntryDto();
                    item.PumpKey = string.IsNullOrWhiteSpace(item.PumpKey)
                        ? cleanPump
                        : item.PumpKey;
                    return item;
                })
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Clamp(limit, 1, 200))
                .ToList();
        }

        public async Task<AutoIrrigationScheduleDto?> GetScheduleAsync(
            string pumpKey,
            string relayKey)
        {
            var cleanPump = CleanKey(pumpKey, nameof(pumpKey));
            var cleanRelay = CleanRelayKey(relayKey);
            var embedded = await _firebase.GetAsync<AutoIrrigationScheduleDto>(
                $"devices/{cleanPump}/schedule");
            var legacy = await _firebase.GetAsync<AutoIrrigationScheduleDto>(
                $"pumpSchedules/{cleanPump}/{cleanRelay}");
            return MergeAndNormalizeSchedule(cleanPump, cleanRelay, embedded, legacy);
        }

        public async Task<AutoIrrigationScheduleDto> SaveScheduleAsync(
            string pumpKey,
            string relayKey,
            UpsertAutoIrrigationScheduleDto dto)
        {
            ValidateSchedule(dto);
            var cleanPump = CleanKey(pumpKey, nameof(pumpKey));
            var cleanRelay = CleanRelayKey(relayKey);
            if (cleanRelay != "relay2")
            {
                throw new ArgumentException("Automatic irrigation is only supported for relay2.");
            }

            var existing = await GetScheduleAsync(cleanPump, cleanRelay);
            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
            var schedule = new AutoIrrigationScheduleDto
            {
                PumpKey = cleanPump,
                RelayKey = cleanRelay,
                Enabled = dto.Enabled,
                IntervalMinutes = dto.IntervalMinutes,
                DurationSeconds = dto.DurationSeconds,
                DurationMinutes = Math.Max(1, (int)Math.Ceiling(dto.DurationSeconds / 60d)),
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                StartHour = ParseTimeOfDay(dto.StartTime).Hours,
                EndHour = ParseTimeOfDay(dto.EndTime).Hours,
                SmartEnabled = dto.SmartEnabled,
                SensorKey = string.IsNullOrWhiteSpace(dto.SensorKey)
                    ? existing?.SensorKey
                    : dto.SensorKey.Trim(),
                SoilMoistureThresholdEnabled = dto.SoilMoistureThresholdEnabled,
                SoilMoistureThreshold = dto.SoilMoistureThreshold,
                AirTempThresholdEnabled = dto.AirTempThresholdEnabled,
                AirTempMin = dto.AirTempMin,
                AirTempMax = dto.AirTempMax,
                AirHumidityThresholdEnabled = dto.AirHumidityThresholdEnabled,
                AirHumidityThreshold = dto.AirHumidityThreshold,
                CooldownMinutes = dto.CooldownMinutes,
                LastRunAt = existing?.LastRunAt,
                LastRunLocal = existing?.LastRunLocal,
                ActiveUntilAt = existing?.ActiveUntilAt,
                ActiveUntilLocal = existing?.ActiveUntilLocal,
                ActiveSource = existing?.ActiveSource,
                LastSmartRunAt = existing?.LastSmartRunAt,
                LastSmartRunLocal = existing?.LastSmartRunLocal,
                LastTriggeredAt = existing?.LastTriggeredAt,
                LastTriggeredLocal = existing?.LastTriggeredLocal,
                LastWaterTime = existing?.LastWaterTime ?? 0,
                UpdatedAt = nowUtc.ToString("O"),
                UpdatedLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss")
            };

            if (schedule.Enabled)
            {
                var nextRun = CalculateNextRun(schedule, nowLocal.DateTime);
                schedule.NextRunAt = ToUtcOffset(nextRun).ToString("O");
                schedule.NextRunLocal = nextRun.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                schedule.NextRunAt = null;
                schedule.NextRunLocal = null;
            }

            await SaveScheduleCopiesAsync(schedule, CancellationToken.None);
            return schedule;
        }

        public async Task ProcessAutomationAsync(
            CancellationToken cancellationToken = default)
        {
            var devices = await _firebase.GetAsync<Dictionary<string, AutomationDeviceStateDto>>(
                    "devices",
                    cancellationToken)
                ?? new Dictionary<string, AutomationDeviceStateDto>();
            var legacySchedules = await _firebase.GetAsync<
                    Dictionary<string, Dictionary<string, AutoIrrigationScheduleDto>>>(
                    "pumpSchedules",
                    cancellationToken)
                ?? new Dictionary<string, Dictionary<string, AutoIrrigationScheduleDto>>();

            foreach (var deviceEntry in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    legacySchedules.TryGetValue(deviceEntry.Key, out var relaySchedules);
                    AutoIrrigationScheduleDto? legacy = null;
                    relaySchedules?.TryGetValue("relay2", out legacy);
                    var schedule = MergeAndNormalizeSchedule(
                        deviceEntry.Key,
                        "relay2",
                        deviceEntry.Value?.Schedule,
                        legacy);
                    if (schedule == null)
                    {
                        continue;
                    }

                    await ProcessDeviceAutomationAsync(
                        deviceEntry.Key,
                        deviceEntry.Value,
                        devices,
                        schedule,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Pump automation failed for device {PumpKey}; continuing other devices.",
                        deviceEntry.Key);
                }
            }
        }

        private async Task ProcessDeviceAutomationAsync(
            string pumpKey,
            AutomationDeviceStateDto? pump,
            IReadOnlyDictionary<string, AutomationDeviceStateDto> devices,
            AutoIrrigationScheduleDto schedule,
            CancellationToken cancellationToken)
        {
            var deviceLock = DeviceLocks.GetOrAdd(pumpKey, _ => new SemaphoreSlim(1, 1));
            await deviceLock.WaitAsync(cancellationToken);
            try
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var nowLocalOffset = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
                var nowLocal = nowLocalOffset.DateTime;
                var insideWindow = IsInsideOperatingWindow(schedule, nowLocal);
                var activeUntil = ParseLocalDateTime(schedule.ActiveUntilLocal);
                var activeSource = NormalizeSource(schedule.ActiveSource);

                if (activeUntil.HasValue)
                {
                    var ownerEnabled = activeSource == ScheduleSource
                        ? schedule.Enabled
                        : activeSource == ThresholdSource && schedule.SmartEnabled;
                    var ownerWindowValid =
                        activeSource == ThresholdSource || insideWindow;
                    if (activeUntil.Value <= nowLocal ||
                        !ownerWindowValid ||
                        !ownerEnabled)
                    {
                        await SetRelayIfChangedCoreAsync(
                            pumpKey,
                            "relay2",
                            false,
                            activeSource ?? ScheduleSource,
                            null,
                            ActorForSource(activeSource),
                            cancellationToken);
                        schedule.ActiveUntilAt = null;
                        schedule.ActiveUntilLocal = null;
                        schedule.ActiveSource = null;
                        await SaveScheduleCopiesAsync(schedule, cancellationToken);
                    }

                    return;
                }

                // A relay that is already on without automation ownership is manual.
                // Automation must never claim it and later turn it off.
                if (pump?.Relay2 == true)
                {
                    if (schedule.Enabled &&
                        insideWindow &&
                        IsScheduleDue(schedule, nowLocal))
                    {
                        AdvanceNextScheduleSlot(schedule, nowLocal);
                        await SaveScheduleCopiesAsync(schedule, cancellationToken);
                    }
                    return;
                }

                string? triggerSource = null;
                if (schedule.Enabled &&
                    insideWindow &&
                    IsScheduleDue(schedule, nowLocal))
                {
                    triggerSource = ScheduleSource;
                }
                else if (schedule.SmartEnabled &&
                    IsThresholdViolated(schedule, devices) &&
                    IsCooldownComplete(schedule, nowUtc))
                {
                    triggerSource = ThresholdSource;
                }

                if (triggerSource == null)
                {
                    return;
                }

                var changed = await SetRelayIfChangedCoreAsync(
                    pumpKey,
                    "relay2",
                    true,
                    triggerSource,
                    null,
                    ActorForSource(triggerSource),
                    cancellationToken);
                if (!changed)
                {
                    return;
                }

                var stopAtLocal = nowLocal.AddSeconds(EffectiveDurationSeconds(schedule));
                schedule.ActiveUntilAt = ToUtcOffset(stopAtLocal).ToString("O");
                schedule.ActiveUntilLocal = stopAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
                schedule.ActiveSource = triggerSource;
                schedule.LastWaterTime = nowUtc.ToUnixTimeSeconds();

                if (triggerSource == ScheduleSource)
                {
                    schedule.LastRunAt = nowUtc.ToString("O");
                    schedule.LastRunLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    AdvanceNextScheduleSlot(schedule, nowLocal);
                }
                else
                {
                    schedule.LastTriggeredAt = nowUtc.ToString("O");
                    schedule.LastTriggeredLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    schedule.LastSmartRunAt = schedule.LastTriggeredAt;
                    schedule.LastSmartRunLocal = schedule.LastTriggeredLocal;
                }

                await SaveScheduleCopiesAsync(schedule, cancellationToken);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        private async Task<bool> SetRelayIfChangedCoreAsync(
            string pumpKey,
            string relayKey,
            bool value,
            string source,
            string? actorUserId,
            string actorName,
            CancellationToken cancellationToken)
        {
            var state = await _firebase.GetAsync<PumpStateDto>(
                $"devices/{pumpKey}",
                cancellationToken);
            var current = relayKey == "relay1" ? state?.Relay1 : state?.Relay2;
            if (current.HasValue && current.Value == value)
            {
                return false;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone);
            var logEntry = new PumpLogEntryDto
            {
                PumpKey = pumpKey,
                RelayKey = relayKey,
                Value = value,
                Action = value ? "ON" : "OFF",
                Source = source,
                ActorUserId = actorUserId,
                ActorName = actorName,
                Timestamp = nowUtc.ToUnixTimeMilliseconds(),
                UtcTime = nowUtc.ToString("O"),
                LocalTime = nowLocal.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var logKey = Guid.NewGuid().ToString("N");

            // Firebase supports atomic multi-location PATCH at the root. Relay
            // state, action metadata and activity history therefore succeed or
            // fail as one operation instead of leaving an unlogged relay change.
            await _firebase.PatchAsync(
                string.Empty,
                new Dictionary<string, object?>
                {
                    [$"devices/{pumpKey}/{relayKey}"] = value,
                    [$"devices/{pumpKey}/timestamp"] = nowUtc.ToUnixTimeSeconds().ToString(),
                    [$"devices/{pumpKey}/lastActionAt"] = nowUtc.ToString("O"),
                    [$"devices/{pumpKey}/lastActionLocal"] = nowLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                    [$"devices/{pumpKey}/lastActionSource"] = source,
                    [$"devices/{pumpKey}/lastActionBy"] = actorName,
                    [$"pumpLogs/{pumpKey}/{logKey}"] = logEntry
                },
                cancellationToken);
            return true;
        }

        private async Task SaveScheduleCopiesAsync(
            AutoIrrigationScheduleDto schedule,
            CancellationToken cancellationToken)
        {
            await _firebase.SetAsync(
                $"devices/{schedule.PumpKey}/schedule",
                schedule,
                cancellationToken);
            // Mirror the former location during rollout so older app/backend
            // versions do not silently overwrite the canonical device schedule.
            await _firebase.SetAsync(
                $"pumpSchedules/{schedule.PumpKey}/{schedule.RelayKey}",
                schedule,
                cancellationToken);
        }

        private static AutoIrrigationScheduleDto? MergeAndNormalizeSchedule(
            string pumpKey,
            string relayKey,
            AutoIrrigationScheduleDto? embedded,
            AutoIrrigationScheduleDto? legacy)
        {
            if (embedded == null && legacy == null)
            {
                return null;
            }

            // A record written by the app has UpdatedAt. It is the authoritative
            // full config; the embedded device-only record supplies runtime fields.
            var config = legacy?.UpdatedAt != null ? legacy : embedded ?? legacy!;
            var runtime = embedded ?? legacy!;
            var schedule = new AutoIrrigationScheduleDto
            {
                PumpKey = pumpKey,
                RelayKey = relayKey,
                Enabled = config.Enabled,
                IntervalMinutes = config.IntervalMinutes > 0
                    ? config.IntervalMinutes
                    : Math.Max(1, runtime.IntervalMinutes),
                DurationSeconds = config.DurationSeconds > 0
                    ? config.DurationSeconds
                    : EffectiveDurationSeconds(runtime),
                DurationMinutes = config.DurationMinutes ?? runtime.DurationMinutes,
                StartTime = EffectiveTime(config.StartTime, config.StartHour, runtime.StartTime, runtime.StartHour, "06:00"),
                EndTime = EffectiveTime(config.EndTime, config.EndHour, runtime.EndTime, runtime.EndHour, "18:00"),
                StartHour = config.StartHour ?? runtime.StartHour,
                EndHour = config.EndHour ?? runtime.EndHour,
                SmartEnabled = config.SmartEnabled,
                SensorKey = config.SensorKey,
                SoilMoistureThresholdEnabled = config.SoilMoistureThresholdEnabled,
                SoilMoistureThreshold = config.SoilMoistureThreshold,
                AirTempThresholdEnabled = config.AirTempThresholdEnabled,
                AirTempMin = config.AirTempMin,
                AirTempMax = config.AirTempMax,
                AirHumidityThresholdEnabled = config.AirHumidityThresholdEnabled,
                AirHumidityThreshold = config.AirHumidityThreshold,
                CooldownMinutes = Math.Max(1, config.CooldownMinutes),
                LastRunAt = runtime.LastRunAt ?? config.LastRunAt,
                LastRunLocal = runtime.LastRunLocal ?? config.LastRunLocal,
                ActiveUntilAt = runtime.ActiveUntilAt ?? config.ActiveUntilAt,
                ActiveUntilLocal = runtime.ActiveUntilLocal ?? config.ActiveUntilLocal,
                ActiveSource = NormalizeSource(runtime.ActiveSource ?? config.ActiveSource),
                NextRunAt = runtime.NextRunAt ?? config.NextRunAt,
                NextRunLocal = runtime.NextRunLocal ?? config.NextRunLocal,
                UpdatedAt = config.UpdatedAt,
                UpdatedLocal = config.UpdatedLocal,
                LastSmartRunAt = runtime.LastSmartRunAt ?? config.LastSmartRunAt,
                LastSmartRunLocal = runtime.LastSmartRunLocal ?? config.LastSmartRunLocal,
                LastTriggeredAt = runtime.LastTriggeredAt ?? config.LastTriggeredAt,
                LastTriggeredLocal = runtime.LastTriggeredLocal ?? config.LastTriggeredLocal,
                LastWaterTime = Math.Max(runtime.LastWaterTime, config.LastWaterTime)
            };
            schedule.DurationMinutes ??= Math.Max(
                1,
                (int)Math.Ceiling(schedule.DurationSeconds / 60d));
            schedule.StartHour ??= ParseTimeOfDay(schedule.StartTime).Hours;
            schedule.EndHour ??= ParseTimeOfDay(schedule.EndTime).Hours;
            return schedule;
        }

        private static bool IsThresholdViolated(
            AutoIrrigationScheduleDto schedule,
            IReadOnlyDictionary<string, AutomationDeviceStateDto> devices)
        {
            AutomationDeviceStateDto? sensor = null;
            if (!string.IsNullOrWhiteSpace(schedule.SensorKey))
            {
                devices.TryGetValue(schedule.SensorKey, out sensor);
            }

            sensor ??= devices.Values.FirstOrDefault(x =>
                x?.Temperature.HasValue == true ||
                x?.Humidity.HasValue == true ||
                x?.GroundHumidity.HasValue == true);

            var soilViolation = schedule.SoilMoistureThresholdEnabled &&
                sensor?.GroundHumidity.HasValue == true &&
                schedule.SoilMoistureThreshold.HasValue &&
                sensor.GroundHumidity.Value < schedule.SoilMoistureThreshold.Value;
            var temperatureViolation = schedule.AirTempThresholdEnabled &&
                sensor?.Temperature.HasValue == true &&
                schedule.AirTempMax.HasValue &&
                (decimal)sensor.Temperature.Value > schedule.AirTempMax.Value;
            var humidityViolation = schedule.AirHumidityThresholdEnabled &&
                sensor?.Humidity.HasValue == true &&
                schedule.AirHumidityThreshold.HasValue &&
                sensor.Humidity.Value < schedule.AirHumidityThreshold.Value;
            return soilViolation || temperatureViolation || humidityViolation;
        }

        private static bool IsCooldownComplete(
            AutoIrrigationScheduleDto schedule,
            DateTimeOffset nowUtc)
        {
            var lastTriggered = ParseUtcDateTimeOffset(
                schedule.LastTriggeredAt ?? schedule.LastSmartRunAt);
            return !lastTriggered.HasValue ||
                nowUtc - lastTriggered.Value >=
                TimeSpan.FromMinutes(Math.Max(1, schedule.CooldownMinutes));
        }

        private static bool IsScheduleDue(
            AutoIrrigationScheduleDto schedule,
            DateTime nowLocal)
        {
            var nextRun = ParseLocalDateTime(schedule.NextRunLocal);
            return !nextRun.HasValue ||
                nextRun.Value <= nowLocal;
        }

        private static void AdvanceNextScheduleSlot(
            AutoIrrigationScheduleDto schedule,
            DateTime nowLocal)
        {
            var next = CalculateNextRun(
                schedule,
                nowLocal.AddMilliseconds(1));
            schedule.NextRunAt = ToUtcOffset(next).ToString("O");
            schedule.NextRunLocal = next.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static DateTime CalculateNextRun(
            AutoIrrigationScheduleDto schedule,
            DateTime referenceLocal)
        {
            var start = ParseTimeOfDay(schedule.StartTime);
            var end = ParseTimeOfDay(schedule.EndTime);
            var firstRunToday = referenceLocal.Date.Add(start);
            var endToday = referenceLocal.Date.Add(end);
            if (referenceLocal <= firstRunToday)
            {
                return firstRunToday;
            }
            if (referenceLocal >= endToday)
            {
                return firstRunToday.AddDays(1);
            }

            var interval = Math.Max(1, schedule.IntervalMinutes);
            var elapsedMinutes = (referenceLocal - firstRunToday).TotalMinutes;
            var cycles = Math.Ceiling(elapsedMinutes / interval);
            var candidate = firstRunToday.AddMinutes(cycles * interval);
            return candidate < endToday ? candidate : firstRunToday.AddDays(1);
        }

        private static bool IsInsideOperatingWindow(
            AutoIrrigationScheduleDto schedule,
            DateTime localTime)
        {
            var time = localTime.TimeOfDay;
            return time >= ParseTimeOfDay(schedule.StartTime) &&
                time < ParseTimeOfDay(schedule.EndTime);
        }

        private static void ValidateSchedule(UpsertAutoIrrigationScheduleDto dto)
        {
            var start = ParseTimeOfDay(dto.StartTime);
            var end = ParseTimeOfDay(dto.EndTime);
            if (end <= start)
            {
                throw new ArgumentException("EndTime must be later than StartTime.");
            }
            if (dto.SoilMoistureThresholdEnabled && !dto.SoilMoistureThreshold.HasValue)
            {
                throw new ArgumentException("SoilMoistureThreshold is required when enabled.");
            }
            if (dto.AirTempThresholdEnabled && !dto.AirTempMax.HasValue)
            {
                throw new ArgumentException("AirTempMax is required when enabled.");
            }
            if (dto.AirTempMin.HasValue && dto.AirTempMax.HasValue &&
                dto.AirTempMin.Value >= dto.AirTempMax.Value)
            {
                throw new ArgumentException("AirTempMax must be greater than AirTempMin.");
            }
            if (dto.AirHumidityThresholdEnabled && !dto.AirHumidityThreshold.HasValue)
            {
                throw new ArgumentException("AirHumidityThreshold is required when enabled.");
            }
            if (dto.SmartEnabled &&
                !dto.SoilMoistureThresholdEnabled &&
                !dto.AirTempThresholdEnabled &&
                !dto.AirHumidityThresholdEnabled)
            {
                throw new ArgumentException("At least one irrigation threshold must be enabled.");
            }
        }

        private static int EffectiveDurationSeconds(AutoIrrigationScheduleDto schedule)
        {
            if (schedule.DurationSeconds > 0)
            {
                return schedule.DurationSeconds;
            }
            return Math.Max(1, schedule.DurationMinutes ?? 1) * 60;
        }

        private static string EffectiveTime(
            string? preferredTime,
            int? preferredHour,
            string? fallbackTime,
            int? fallbackHour,
            string defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(preferredTime) &&
                preferredTime != defaultValue)
            {
                return preferredTime;
            }
            if (preferredHour.HasValue)
            {
                return $"{Math.Clamp(preferredHour.Value, 0, 23):00}:00";
            }
            if (!string.IsNullOrWhiteSpace(fallbackTime))
            {
                return fallbackTime;
            }
            return fallbackHour.HasValue
                ? $"{Math.Clamp(fallbackHour.Value, 0, 23):00}:00"
                : defaultValue;
        }

        private static string? NormalizeSource(string? source)
        {
            if (string.Equals(source, "smart-threshold", StringComparison.OrdinalIgnoreCase))
            {
                return ThresholdSource;
            }
            return string.IsNullOrWhiteSpace(source) ? null : source;
        }

        private static string ActorForSource(string? source)
        {
            return NormalizeSource(source) == ThresholdSource
                ? ThresholdActor
                : ScheduleActor;
        }

        private static string CleanKey(string value, string parameterName)
        {
            var clean = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(clean) ||
                clean.IndexOfAny(new[] { '.', '#', '$', '[', ']', '/' }) >= 0)
            {
                throw new ArgumentException("Invalid Firebase key.", parameterName);
            }
            return clean;
        }

        private static string CleanRelayKey(string relayKey)
        {
            var clean = CleanKey(relayKey, nameof(relayKey)).ToLowerInvariant();
            return clean is "relay1" or "relay2"
                ? clean
                : throw new ArgumentException("Relay must be relay1 or relay2.", nameof(relayKey));
        }

        private static TimeSpan ParseTimeOfDay(string? raw)
        {
            return TimeSpan.TryParse(raw, out var parsed)
                ? parsed
                : new TimeSpan(6, 0, 0);
        }

        private static DateTime? ParseLocalDateTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
            return DateTime.TryParse(raw, out var parsed) ? parsed : null;
        }

        private static DateTimeOffset? ParseUtcDateTimeOffset(string? raw)
        {
            return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
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
