using System.Collections.Concurrent;
using IoTAgriculture.DTOs.Firebase;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class DeviceService : IDeviceService
    {
        private const string ScheduleSource = "schedule";
        private const string ThresholdSource = "threshold";
        private static readonly TimeSpan DiagnosticsWriteInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeviceLocks = new();
        private readonly IFirebaseRtdbService _firebase;
        private readonly IFirebasePushNotificationService? _pushNotifications;
        private readonly ILogger<DeviceService> _logger;

        public DeviceService(
            IFirebaseRtdbService firebase,
            ILogger<DeviceService> logger)
            : this(firebase, logger, null)
        {
        }

        public DeviceService(
            IFirebaseRtdbService firebase,
            ILogger<DeviceService> logger,
            IFirebasePushNotificationService? pushNotifications)
        {
            _firebase = firebase;
            _logger = logger;
            _pushNotifications = pushNotifications;
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
                    // A manual ON owns relay2 temporarily so automation cannot
                    // immediately turn it off. A manual OFF only cancels the
                    // current run and schedules the next normal cycle.
                    await ApplyManualOverrideAsync(
                        cleanPump,
                        value,
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
                AirTempThresholdEnabled = dto.AirTempThresholdEnabled,
                AirTempMin = dto.AirTempMin,
                AirTempMax = dto.AirTempMax,
                AirHumidityThresholdEnabled = dto.AirHumidityThresholdEnabled,
                AirHumidityThreshold =
                    dto.AirHumidityOnThreshold ?? dto.AirHumidityThreshold,
                AirHumidityOnThreshold =
                    dto.AirHumidityOnThreshold ?? dto.AirHumidityThreshold,
                AirHumidityOffThreshold =
                    dto.AirHumidityOffThreshold ?? dto.AirHumidityThreshold,
                CooldownMinutes = dto.CooldownMinutes,
                LastRunAt = existing?.LastRunAt,
                LastRunLocal = existing?.LastRunLocal,
                ActiveUntilAt = existing?.ActiveUntilAt,
                ActiveUntilLocal = existing?.ActiveUntilLocal,
                ActiveSource = existing?.ActiveSource,
                ManualOverrideUntilAt = existing?.ManualOverrideUntilAt,
                ManualOverrideUntilLocal = existing?.ManualOverrideUntilLocal,
                LastSmartRunAt = existing?.LastSmartRunAt,
                LastSmartRunLocal = existing?.LastSmartRunLocal,
                LastTriggeredAt = existing?.LastTriggeredAt,
                LastTriggeredLocal = existing?.LastTriggeredLocal,
                LastWaterTime = existing?.LastWaterTime ?? 0,
                ThresholdConditionActive = existing?.ThresholdConditionActive ?? false,
                TemperatureThresholdActive =
                    existing?.TemperatureThresholdActive ?? false,
                HumidityThresholdActive =
                    existing?.HumidityThresholdActive ?? false,
                ThresholdStatus = existing?.ThresholdStatus ?? "not-checked",
                ThresholdReason = existing?.ThresholdReason,
                AutomationLastCheckedAt = existing?.AutomationLastCheckedAt,
                AutomationLastCheckedLocal = existing?.AutomationLastCheckedLocal,
                UpdatedAt = nowUtc.ToString("O"),
                UpdatedLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss")
            };

            if (schedule.Enabled)
            {
                var nextRun = CalculateNextRun(schedule, nowLocal.DateTime);
                SetNextRun(schedule, nextRun);
            }
            else
            {
                schedule.NextRunAt = null;
                schedule.NextRunLocal = null;
                schedule.NextWaterTime = 0;
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
            _logger.LogInformation(
                "[EngineRead] devices snapshot parsed; deviceCount={DeviceCount}; scheduleOwnerCount={ScheduleOwnerCount}.",
                devices.Count,
                legacySchedules.Count);

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

        public async Task ProcessThresholdAutomationAsync(
            CancellationToken cancellationToken = default)
        {
            var devices = await _firebase.GetAsync<
                    Dictionary<string, AutomationDeviceStateDto>>(
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
                    if (schedule == null ||
                        (!schedule.SmartEnabled &&
                            NormalizeSource(schedule.ActiveSource) != ThresholdSource &&
                            !schedule.ThresholdConditionActive))
                    {
                        continue;
                    }

                    await ProcessDeviceThresholdAutomationAsync(
                        deviceEntry.Key,
                        deviceEntry.Value,
                        devices,
                        schedule,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Smart threshold polling failed for device {PumpKey}; continuing other devices.",
                        deviceEntry.Key);
                }
            }
        }

        private async Task ProcessDeviceThresholdAutomationAsync(
            string pumpKey,
            AutomationDeviceStateDto? pump,
            IReadOnlyDictionary<string, AutomationDeviceStateDto> devices,
            AutoIrrigationScheduleDto schedule,
            CancellationToken cancellationToken)
        {
            var deviceLock = DeviceLocks.GetOrAdd(
                pumpKey,
                _ => new SemaphoreSlim(1, 1));
            await deviceLock.WaitAsync(cancellationToken);
            try
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var nowLocal = TimeZoneInfo.ConvertTime(
                    nowUtc,
                    VietnamTimeZone).DateTime;
                var activeSource = NormalizeSource(schedule.ActiveSource);
                var manualOverrideUntil = ParseUtcDateTimeOffset(
                    schedule.ManualOverrideUntilAt);
                var threshold = EvaluateThreshold(schedule, devices);
                var cooldownComplete = IsCooldownComplete(schedule, nowUtc);
                var diagnosticsChanged = UpdateAutomationDiagnostics(
                    schedule,
                    threshold,
                    cooldownComplete,
                    nowUtc,
                    nowLocal,
                    activeSource);

                _logger.LogInformation(
                    "[ThresholdPoll] pump={PumpKey}; enabled={Enabled}; sensor={SensorKey}; temperature={Temperature}; tempMax={TempMax}; temperatureActive={TemperatureActive}; humidity={Humidity}; humidityOn={HumidityOn}; humidityOff={HumidityOff}; humidityActive={HumidityActive}; irrigationRequested={IrrigationRequested}; reason={Reason}.",
                    pumpKey,
                    schedule.SmartEnabled,
                    threshold.SensorKey ?? "(none)",
                    threshold.Temperature,
                    threshold.TemperatureThreshold,
                    threshold.TemperatureActive,
                    threshold.Humidity,
                    threshold.HumidityOnThreshold,
                    threshold.HumidityOffThreshold,
                    threshold.HumidityActive,
                    threshold.IsViolated,
                    threshold.Reason);
                await WriteEngineHeartbeatAsync(
                    pumpKey,
                    schedule,
                    threshold,
                    cooldownComplete,
                    pump?.Relay2 == true,
                    nowUtc,
                    nowLocal,
                    cancellationToken);

                if (manualOverrideUntil.HasValue &&
                    manualOverrideUntil.Value > nowUtc)
                {
                    var hadScheduledRun = schedule.NextRunAt != null ||
                        schedule.NextRunLocal != null ||
                        schedule.NextWaterTime > 0;
                    ClearNextRun(schedule);
                    if (diagnosticsChanged || hadScheduledRun)
                    {
                        schedule.ThresholdStatus = "manual-override";
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                if (!schedule.SmartEnabled)
                {
                    schedule.TemperatureThresholdActive = false;
                    schedule.HumidityThresholdActive = false;
                    schedule.ThresholdConditionActive = false;
                    schedule.ThresholdStatus = "disabled";
                    if (activeSource == ThresholdSource)
                    {
                        const string disabledReason =
                            "Chế độ ngưỡng tưới đã tắt.";
                        await SetRelayIfChangedCoreAsync(
                            pumpKey,
                            "relay2",
                            false,
                            ThresholdSource,
                            null,
                            ActorForSource(ThresholdSource),
                            cancellationToken,
                            disabledReason,
                            threshold);
                        schedule.ActiveSource = null;
                        schedule.ActiveUntilAt = null;
                        schedule.ActiveUntilLocal = null;
                        schedule.LastWaterTime = nowUtc.ToUnixTimeSeconds();
                    }
                    await SaveAutomationStateAsync(schedule, cancellationToken);
                    return;
                }

                if (threshold.IsViolated)
                {
                    if (!cooldownComplete && activeSource != ThresholdSource)
                    {
                        schedule.ActiveSource = null;
                        schedule.ActiveUntilAt = null;
                        schedule.ActiveUntilLocal = null;
                        schedule.ThresholdStatus = "cooldown";
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                        _logger.LogInformation(
                            "[ThresholdAction] pump={PumpKey}; relay2=OFF; reason=mandatory-cooldown; condition remembered until cooldown expires.",
                            pumpKey);
                        return;
                    }

                    var changed = await SetRelayIfChangedCoreAsync(
                        pumpKey,
                        "relay2",
                        true,
                        ThresholdSource,
                        null,
                        ActorForSource(ThresholdSource),
                        cancellationToken,
                        threshold.Reason,
                        threshold);
                    schedule.ActiveSource = ThresholdSource;
                    schedule.ActiveUntilAt = null;
                    schedule.ActiveUntilLocal = null;
                    schedule.ThresholdStatus = "watering";
                    if (changed || activeSource != ThresholdSource)
                    {
                        schedule.LastSmartRunAt = nowUtc.ToString("O");
                        schedule.LastSmartRunLocal =
                            nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                        _logger.LogInformation(
                            "[ThresholdAction] pump={PumpKey}; relay2=ON; reason={Reason}; temperature={Temperature}; humidity={Humidity}.",
                            pumpKey,
                            threshold.Reason,
                            threshold.Temperature,
                            threshold.Humidity);
                    }
                    await SaveAutomationStateAsync(schedule, cancellationToken);
                    return;
                }

                if (!threshold.HasRequiredReading)
                {
                    // Never infer that an active condition has cleared from a
                    // missing reading. A known violation can still start the
                    // pump through the OR rule above, but stopping requires all
                    // enabled conditions to have real, safe readings.
                    if (diagnosticsChanged)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                if (activeSource == ThresholdSource)
                {
                    var reason = BuildThresholdClearReason(threshold);
                    await SetRelayIfChangedCoreAsync(
                        pumpKey,
                        "relay2",
                        false,
                        ThresholdSource,
                        null,
                        ActorForSource(ThresholdSource),
                        cancellationToken,
                        reason,
                        threshold);
                    _logger.LogInformation(
                        "[ThresholdAction] pump={PumpKey}; relay2=OFF; reason={Reason}; temperature={Temperature}; humidity={Humidity}.",
                        pumpKey,
                        reason,
                        threshold.Temperature,
                        threshold.Humidity);
                    schedule.ActiveSource = null;
                    schedule.ActiveUntilAt = null;
                    schedule.ActiveUntilLocal = null;
                    schedule.LastWaterTime = nowUtc.ToUnixTimeSeconds();
                    schedule.LastTriggeredAt = nowUtc.ToString("O");
                    schedule.LastTriggeredLocal =
                        nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                }

                if (diagnosticsChanged || activeSource == ThresholdSource)
                {
                    await SaveAutomationStateAsync(schedule, cancellationToken);
                }
            }
            finally
            {
                deviceLock.Release();
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
                var scheduleDue = schedule.Enabled &&
                    insideWindow &&
                    IsScheduleDue(schedule, nowLocal);
                var activeUntil = ParseLocalDateTime(schedule.ActiveUntilLocal);
                var activeSource = NormalizeSource(schedule.ActiveSource);
                var manualOverrideUntil = ParseUtcDateTimeOffset(
                    schedule.ManualOverrideUntilAt);
                var threshold = EvaluateThreshold(schedule, devices);
                var cooldownComplete = IsCooldownComplete(schedule, nowUtc);
                var diagnosticsChanged = UpdateAutomationDiagnostics(
                    schedule,
                    threshold,
                    cooldownComplete,
                    nowUtc,
                    nowLocal,
                    activeSource);
                _logger.LogInformation(
                    "[EngineConfig] pump={PumpKey}; relay=relay2; scheduleEnabled={ScheduleEnabled}; intervalMinutes={IntervalMinutes}; durationSeconds={DurationSeconds}; window={StartTime}-{EndTime}; thresholdEnabled={ThresholdEnabled}; sensor={SensorKey}; tempCheckEnabled={TempCheckEnabled}; tempMax={TempMax}; humidityCheckEnabled={HumidityCheckEnabled}; humidityOn={HumidityOn}; humidityOff={HumidityOff}; cooldownMinutes={CooldownMinutes}.",
                    pumpKey,
                    schedule.Enabled,
                    schedule.IntervalMinutes,
                    EffectiveDurationSeconds(schedule),
                    schedule.StartTime,
                    schedule.EndTime,
                    schedule.SmartEnabled,
                    schedule.SensorKey ?? "(none)",
                    schedule.AirTempThresholdEnabled,
                    schedule.AirTempMax,
                    schedule.AirHumidityThresholdEnabled,
                    schedule.AirHumidityOnThreshold ??
                        schedule.AirHumidityThreshold,
                    schedule.AirHumidityOffThreshold ??
                        schedule.AirHumidityThreshold,
                    schedule.CooldownMinutes);
                _logger.LogInformation(
                    "[EngineSensor] pump={PumpKey}; sensor={SensorKey}; temperature={Temperature}; humidity={Humidity}; readingComplete={ReadingComplete}; thresholdViolated={ThresholdViolated}; reason={ThresholdReason}.",
                    pumpKey,
                    threshold.SensorKey ?? "(none)",
                    threshold.Temperature,
                    threshold.Humidity,
                    threshold.HasRequiredReading,
                    threshold.IsViolated,
                    threshold.Reason);
                _logger.LogInformation(
                    "[EngineDecision] pump={PumpKey}; relay2={Relay2}; insideWindow={InsideWindow}; scheduleDue={ScheduleDue}; thresholdViolated={ThresholdViolated}; cooldownComplete={CooldownComplete}; activeSource={ActiveSource}; activeUntil={ActiveUntil}; manualOverrideUntil={ManualOverrideUntil}.",
                    pumpKey,
                    pump?.Relay2 == true,
                    insideWindow,
                    scheduleDue,
                    threshold.IsViolated,
                    cooldownComplete,
                    activeSource ?? "(none)",
                    activeUntil,
                    manualOverrideUntil);
                await WriteEngineHeartbeatAsync(
                    pumpKey,
                    schedule,
                    threshold,
                    cooldownComplete,
                    pump?.Relay2 == true,
                    nowUtc,
                    nowLocal,
                    cancellationToken);

                if (manualOverrideUntil.HasValue &&
                    manualOverrideUntil.Value > nowUtc)
                {
                    _logger.LogInformation(
                        "[EngineDecision] pump={PumpKey}; action=none; reason=manual-override.",
                        pumpKey);
                    schedule.ActiveUntilAt = null;
                    schedule.ActiveUntilLocal = null;
                    schedule.ActiveSource = null;
                    var hadScheduledRun = schedule.NextRunAt != null ||
                        schedule.NextRunLocal != null ||
                        schedule.NextWaterTime > 0;
                    ClearNextRun(schedule);
                    var manualStatusChanged = !string.Equals(
                        schedule.ThresholdStatus,
                        "manual-override",
                        StringComparison.Ordinal);
                    schedule.ThresholdStatus = "manual-override";
                    if (diagnosticsChanged || manualStatusChanged || hadScheduledRun)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                if (manualOverrideUntil.HasValue)
                {
                    schedule.ManualOverrideUntilAt = null;
                    schedule.ManualOverrideUntilLocal = null;
                    if (schedule.Enabled)
                    {
                        SetNextRun(
                            schedule,
                            CalculateNextRun(
                                schedule,
                                nowLocal.AddMilliseconds(1)));
                    }
                    else
                    {
                        ClearNextRun(schedule);
                    }
                    scheduleDue = false;
                    diagnosticsChanged = true;
                }

                // The threshold poller exclusively owns threshold decisions.
                // While a real threshold condition is active, the fixed
                // schedule must not start a timed run or stop an existing one.
                if (activeSource == ThresholdSource ||
                    (schedule.SmartEnabled && threshold.IsViolated))
                {
                    _logger.LogInformation(
                        "[EngineDecision] pump={PumpKey}; action=none; reason=threshold-poller-has-priority.",
                        pumpKey);
                    if (schedule.Enabled && IsScheduleDue(schedule, nowLocal))
                    {
                        SetNextRun(
                            schedule,
                            CalculateNextRun(
                                schedule,
                                nowLocal.AddMilliseconds(1)));
                        diagnosticsChanged = true;
                    }
                    if (diagnosticsChanged)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                if (activeUntil.HasValue)
                {
                    var ownerEnabled = schedule.Enabled;
                    var ownerWindowValid = insideWindow;
                    if (activeUntil.Value <= nowLocal ||
                        !ownerWindowValid ||
                        !ownerEnabled)
                    {
                        _logger.LogInformation(
                            "[EngineDecision] pump={PumpKey}; action=relay2-off; source={Source}; reason={Reason}.",
                            pumpKey,
                            activeSource ?? ScheduleSource,
                            activeUntil.Value <= nowLocal
                                ? "duration-complete"
                                : "automation-disabled-or-outside-window");
                        await SetRelayIfChangedCoreAsync(
                            pumpKey,
                            "relay2",
                            false,
                            activeSource ?? ScheduleSource,
                            null,
                            ActorForSource(activeSource),
                            cancellationToken,
                            reason: activeUntil.Value <= nowLocal
                                ? "Đã hết thời lượng tưới tự động."
                                : "Chế độ tự động đã tắt hoặc ngoài khung giờ hoạt động.");
                        schedule.ActiveUntilAt = null;
                        schedule.ActiveUntilLocal = null;
                        schedule.ActiveSource = null;
                        schedule.LastWaterTime = nowUtc.ToUnixTimeSeconds();
                        if (activeSource == ScheduleSource)
                        {
                            schedule.LastRunAt = nowUtc.ToString("O");
                            schedule.LastRunLocal =
                                nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                            SetNextRun(
                                schedule,
                                CalculateNextRun(schedule, nowLocal));
                        }
                        UpdateAutomationDiagnostics(
                            schedule,
                            threshold,
                            IsCooldownComplete(schedule, nowUtc),
                            nowUtc,
                            nowLocal,
                            activeSource: null);
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    else if (diagnosticsChanged)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }

                    return;
                }

                // A relay that is already on without automation ownership is manual.
                // Automation must never claim it and later turn it off.
                if (pump?.Relay2 == true)
                {
                    _logger.LogInformation(
                        "[EngineDecision] pump={PumpKey}; action=none; reason=relay2-owned-by-manual-command.",
                        pumpKey);
                    if (schedule.Enabled && IsScheduleDue(schedule, nowLocal))
                    {
                        SetNextRun(
                            schedule,
                            CalculateNextRun(
                                schedule,
                                nowLocal.AddMilliseconds(1)));
                        diagnosticsChanged = true;
                    }
                    if (diagnosticsChanged)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                string? triggerSource = null;
                if (scheduleDue)
                {
                    triggerSource = ScheduleSource;
                }

                if (triggerSource == null)
                {
                    _logger.LogInformation(
                        "[EngineDecision] pump={PumpKey}; action=none; reason=no-due-automation-condition.",
                        pumpKey);
                    if (diagnosticsChanged)
                    {
                        await SaveAutomationStateAsync(schedule, cancellationToken);
                    }
                    return;
                }

                var triggerReason = triggerSource == ScheduleSource
                    ? BuildScheduleReason(schedule)
                    : threshold.Reason;
                _logger.LogInformation(
                    "[EngineAction] pump={PumpKey}; source={Source}; relay2=true; reason={Reason}",
                    pumpKey,
                    triggerSource,
                    triggerReason);
                var changed = await SetRelayIfChangedCoreAsync(
                    pumpKey,
                    "relay2",
                    true,
                    triggerSource,
                    null,
                    ActorForSource(triggerSource),
                    cancellationToken,
                    triggerReason,
                    triggerSource == ThresholdSource ? threshold : null,
                    intervalMinutes: triggerSource == ScheduleSource
                        ? Math.Max(1, schedule.IntervalMinutes)
                        : null);
                if (!changed)
                {
                    return;
                }

                schedule.ActiveSource = triggerSource;
                if (triggerSource == ThresholdSource)
                {
                    schedule.ActiveUntilAt = null;
                    schedule.ActiveUntilLocal = null;
                    schedule.ThresholdStatus = "watering";
                }
                else
                {
                    var stopAtLocal = nowLocal.AddSeconds(
                        EffectiveDurationSeconds(schedule));
                    schedule.ActiveUntilAt =
                        ToUtcOffset(stopAtLocal).ToString("O");
                    schedule.ActiveUntilLocal =
                        stopAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    schedule.LastRunAt = nowUtc.ToString("O");
                    schedule.LastRunLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    ClearNextRun(schedule);
                }

                if (triggerSource == ThresholdSource)
                {
                    schedule.LastSmartRunAt = nowUtc.ToString("O");
                    schedule.LastSmartRunLocal =
                        nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                }

                await SaveAutomationStateAsync(schedule, cancellationToken);
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
            CancellationToken cancellationToken,
            string? reason = null,
            ThresholdEvaluation? threshold = null,
            int? intervalMinutes = null)
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
                Reason = reason,
                IntervalMinutes = intervalMinutes,
                SensorKey = threshold?.SensorKey,
                Temperature = threshold?.Temperature,
                Humidity = threshold?.Humidity,
                TemperatureThreshold = threshold?.TemperatureThreshold,
                HumidityThreshold = threshold?.HumidityOnThreshold,
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
                    [$"devices/{pumpKey}/lastActionReason"] = reason,
                    [$"pumpLogs/{pumpKey}/{logKey}"] = logEntry
                },
                cancellationToken);
            _logger.LogInformation(
                "[FirebaseUpdate] pump={PumpKey}; relay={RelayKey}; value={RelayValue}; source={Source}; activityLog=pumpLogs/{PumpKey}/{LogKey}",
                pumpKey,
                relayKey,
                value,
                source,
                pumpKey,
                logKey);

            if (relayKey == "relay2" && _pushNotifications != null)
            {
                try
                {
                    _logger.LogInformation(
                        "[PumpPush] pump={PumpKey}; relay=relay2; value={RelayValue}; source={Source}; notification=dispatching",
                        pumpKey,
                        value,
                        source);
                    await _pushNotifications.SendPumpStateChangedAsync(
                        pumpKey,
                        string.IsNullOrWhiteSpace(state?.DeviceName)
                            ? pumpKey
                            : state.DeviceName,
                        value,
                        source,
                        actorName,
                        reason,
                        cancellationToken);
                    _logger.LogInformation(
                        "[PumpPush] pump={PumpKey}; relay=relay2; value={RelayValue}; source={Source}; notification=sent",
                        pumpKey,
                        value,
                        source);
                }
                catch (Exception ex)
                {
                    // The relay transition and its activity log are already
                    // committed atomically. A notification outage must never
                    // roll back or disturb irrigation automation.
                    _logger.LogError(
                        ex,
                        "[PumpPush] pump={PumpKey}; relay=relay2; value={RelayValue}; source={Source}; notification=failed",
                        pumpKey,
                        value,
                        source);
                }
            }
            return true;
        }

        private async Task ApplyManualOverrideAsync(
            string pumpKey,
            bool requestedValue,
            CancellationToken cancellationToken)
        {
            var schedule = await GetScheduleAsync(pumpKey, "relay2");
            if (schedule == null)
            {
                return;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, VietnamTimeZone).DateTime;
            var interruptedSource = NormalizeSource(schedule.ActiveSource);
            schedule.ActiveUntilAt = null;
            schedule.ActiveUntilLocal = null;
            schedule.ActiveSource = null;

            if (!requestedValue)
            {
                // Manual OFF is a one-shot cancellation, not a persistent pause.
                // Use the stop time as the common anchor for the configured
                // schedule interval and smart-threshold cooldown.
                schedule.ManualOverrideUntilAt = null;
                schedule.ManualOverrideUntilLocal = null;
                schedule.LastWaterTime = nowUtc.ToUnixTimeSeconds();
                if (interruptedSource == ScheduleSource)
                {
                    schedule.LastRunAt = nowUtc.ToString("O");
                    schedule.LastRunLocal =
                        nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
                }
                if (schedule.Enabled)
                {
                    SetNextRun(schedule, CalculateNextRun(schedule, nowLocal));
                }
                else
                {
                    ClearNextRun(schedule);
                }
                schedule.ThresholdStatus = schedule.SmartEnabled
                    ? "cooldown"
                    : "disabled";

                await SaveAutomationStateAsync(schedule, cancellationToken);
                _logger.LogInformation(
                    "[ManualStop] pump={PumpKey}; relay2=false; nextRun={NextRun}; automation remains enabled.",
                    pumpKey,
                    schedule.NextRunAt ?? "(none)");
                return;
            }

            var overrideUntilUtc = nowUtc.AddMinutes(
                Math.Max(1, schedule.CooldownMinutes));
            var overrideUntilLocal = TimeZoneInfo.ConvertTime(
                overrideUntilUtc,
                VietnamTimeZone);
            schedule.ManualOverrideUntilAt = overrideUntilUtc.ToString("O");
            schedule.ManualOverrideUntilLocal =
                overrideUntilLocal.ToString("yyyy-MM-dd HH:mm:ss");
            schedule.ThresholdStatus = "manual-override";
            ClearNextRun(schedule);

            await SaveAutomationStateAsync(schedule, cancellationToken);
            _logger.LogInformation(
                "[ManualOverride] pump={PumpKey}; relay2={RelayValue}; automation suppressed until {OverrideUntil}.",
                pumpKey,
                requestedValue,
                overrideUntilUtc);
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

        private async Task SaveAutomationStateAsync(
            AutoIrrigationScheduleDto schedule,
            CancellationToken cancellationToken)
        {
            await SaveScheduleCopiesAsync(schedule, cancellationToken);
        }

        private async Task WriteEngineHeartbeatAsync(
            string pumpKey,
            AutoIrrigationScheduleDto schedule,
            ThresholdEvaluation threshold,
            bool cooldownComplete,
            bool relay2,
            DateTimeOffset nowUtc,
            DateTime nowLocal,
            CancellationToken cancellationToken)
        {
            schedule.AutomationLastCheckedAt = nowUtc.ToString("O");
            schedule.AutomationLastCheckedLocal =
                nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
            await _firebase.PatchAsync(
                $"devices/{pumpKey}",
                new Dictionary<string, object?>
                {
                    ["engineStatus"] = "checked",
                    ["engineLastCheckedAt"] = schedule.AutomationLastCheckedAt,
                    ["engineLastCheckedLocal"] = schedule.AutomationLastCheckedLocal,
                    ["engineMessage"] = threshold.Reason,
                    ["engineVersion"] = "backend-v2"
                },
                cancellationToken);
            _logger.LogInformation(
                "[EngineCheck] tick={TickAt}; pump={PumpKey}; sensor={SensorKey}; relay2={Relay2}; temp={Temperature}; tempMax={TemperatureMax}; humidity={Humidity}; humidityOn={HumidityOn}; humidityOff={HumidityOff}; violated={Violated}; cooldownComplete={CooldownComplete}; scheduleEnabled={ScheduleEnabled}; thresholdEnabled={ThresholdEnabled}",
                nowUtc,
                pumpKey,
                threshold.SensorKey ?? "(none)",
                relay2,
                threshold.Temperature,
                threshold.TemperatureThreshold,
                threshold.Humidity,
                threshold.HumidityOnThreshold,
                threshold.HumidityOffThreshold,
                threshold.IsViolated,
                cooldownComplete,
                schedule.Enabled,
                schedule.SmartEnabled);
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
                AirTempThresholdEnabled = config.AirTempThresholdEnabled,
                AirTempMin = config.AirTempMin,
                AirTempMax = config.AirTempMax,
                AirHumidityThresholdEnabled = config.AirHumidityThresholdEnabled,
                AirHumidityThreshold = config.AirHumidityThreshold,
                AirHumidityOnThreshold =
                    config.AirHumidityOnThreshold ??
                    config.AirHumidityThreshold,
                AirHumidityOffThreshold =
                    config.AirHumidityOffThreshold ??
                    config.AirHumidityThreshold,
                CooldownMinutes = Math.Max(1, config.CooldownMinutes),
                LastRunAt = runtime.LastRunAt ?? config.LastRunAt,
                LastRunLocal = runtime.LastRunLocal ?? config.LastRunLocal,
                ActiveUntilAt = runtime.ActiveUntilAt ?? config.ActiveUntilAt,
                ActiveUntilLocal = runtime.ActiveUntilLocal ?? config.ActiveUntilLocal,
                ActiveSource = NormalizeSource(runtime.ActiveSource ?? config.ActiveSource),
                ManualOverrideUntilAt =
                    runtime.ManualOverrideUntilAt ?? config.ManualOverrideUntilAt,
                ManualOverrideUntilLocal =
                    runtime.ManualOverrideUntilLocal ?? config.ManualOverrideUntilLocal,
                NextRunAt = runtime.NextRunAt ?? config.NextRunAt,
                NextRunLocal = runtime.NextRunLocal ?? config.NextRunLocal,
                NextWaterTime = Math.Max(runtime.NextWaterTime, config.NextWaterTime),
                UpdatedAt = config.UpdatedAt,
                UpdatedLocal = config.UpdatedLocal,
                LastSmartRunAt = runtime.LastSmartRunAt ?? config.LastSmartRunAt,
                LastSmartRunLocal = runtime.LastSmartRunLocal ?? config.LastSmartRunLocal,
                LastTriggeredAt = runtime.LastTriggeredAt ?? config.LastTriggeredAt,
                LastTriggeredLocal = runtime.LastTriggeredLocal ?? config.LastTriggeredLocal,
                LastWaterTime = Math.Max(runtime.LastWaterTime, config.LastWaterTime),
                ThresholdConditionActive = runtime.ThresholdConditionActive,
                TemperatureThresholdActive =
                    runtime.TemperatureThresholdActive,
                HumidityThresholdActive = runtime.HumidityThresholdActive,
                ThresholdStatus = runtime.ThresholdStatus,
                ThresholdReason = runtime.ThresholdReason,
                AutomationLastCheckedAt = runtime.AutomationLastCheckedAt,
                AutomationLastCheckedLocal = runtime.AutomationLastCheckedLocal
            };
            schedule.DurationMinutes ??= Math.Max(
                1,
                (int)Math.Ceiling(schedule.DurationSeconds / 60d));
            schedule.StartHour ??= ParseTimeOfDay(schedule.StartTime).Hours;
            schedule.EndHour ??= ParseTimeOfDay(schedule.EndTime).Hours;
            if (schedule.NextWaterTime == 0 &&
                ParseUtcDateTimeOffset(schedule.NextRunAt) is { } nextRun)
            {
                schedule.NextWaterTime = nextRun.ToUnixTimeSeconds();
            }
            return schedule;
        }

        private static ThresholdEvaluation EvaluateThreshold(
            AutoIrrigationScheduleDto schedule,
            IReadOnlyDictionary<string, AutomationDeviceStateDto> devices)
        {
            AutomationDeviceStateDto? sensor = null;
            string? sensorKey = null;
            if (!string.IsNullOrWhiteSpace(schedule.SensorKey))
            {
                sensorKey = schedule.SensorKey;
                devices.TryGetValue(sensorKey, out sensor);
            }
            else
            {
                var fallback = devices.FirstOrDefault(x =>
                    x.Value?.Temperature.HasValue == true ||
                    x.Value?.Humidity.HasValue == true);
                sensorKey = fallback.Key;
                sensor = fallback.Value;
            }

            if (sensor == null)
            {
                return new ThresholdEvaluation(
                    false,
                    schedule.TemperatureThresholdActive ||
                        schedule.HumidityThresholdActive,
                    schedule.TemperatureThresholdActive,
                    schedule.HumidityThresholdActive,
                    sensorKey,
                    null,
                    null,
                    schedule.AirTempThresholdEnabled
                        ? schedule.AirTempMax
                        : null,
                    schedule.AirHumidityThresholdEnabled
                        ? schedule.AirHumidityOnThreshold ??
                            schedule.AirHumidityThreshold
                        : null,
                    schedule.AirHumidityThresholdEnabled
                        ? schedule.AirHumidityOffThreshold ??
                            schedule.AirHumidityThreshold
                        : null,
                    string.IsNullOrWhiteSpace(sensorKey)
                        ? "Chưa cấu hình cảm biến cho ngưỡng tưới."
                        : $"Không tìm thấy dữ liệu cảm biến {sensorKey}.");
            }

            var hasRequiredReading =
                (!schedule.AirTempThresholdEnabled || sensor.Temperature.HasValue) &&
                (!schedule.AirHumidityThresholdEnabled || sensor.Humidity.HasValue);
            var humidityOnThreshold =
                schedule.AirHumidityOnThreshold ??
                schedule.AirHumidityThreshold;
            var humidityOffThreshold =
                schedule.AirHumidityOffThreshold ??
                schedule.AirHumidityThreshold;
            var temperatureActive = schedule.AirTempThresholdEnabled &&
                (sensor.Temperature.HasValue
                    ? schedule.AirTempMax.HasValue &&
                        (decimal)sensor.Temperature.Value >
                            schedule.AirTempMax.Value
                    : schedule.TemperatureThresholdActive);
            var humidityActive = schedule.AirHumidityThresholdEnabled &&
                (sensor.Humidity.HasValue
                    ? schedule.HumidityThresholdActive
                        ? humidityOffThreshold.HasValue &&
                            sensor.Humidity.Value < humidityOffThreshold.Value
                        : humidityOnThreshold.HasValue &&
                            sensor.Humidity.Value < humidityOnThreshold.Value
                    : schedule.HumidityThresholdActive);

            var reasons = new List<string>();
            if (temperatureActive)
            {
                reasons.Add(
                    $"Nhiệt độ {sensor.Temperature!.Value:0.##}°C > {schedule.AirTempMax!.Value:0.##}°C");
            }
            if (humidityActive)
            {
                var comparisonThreshold = schedule.HumidityThresholdActive
                    ? humidityOffThreshold
                    : humidityOnThreshold;
                reasons.Add(
                    $"Độ ẩm không khí {sensor.Humidity!.Value:0.##}% < {comparisonThreshold!.Value}%");
            }
            var violated = temperatureActive || humidityActive;
            var reason = violated
                ? string.Join("; ", reasons)
                : hasRequiredReading
                    ? "Các giá trị cảm biến đang nằm trong ngưỡng an toàn."
                    : "Cảm biến chưa có đủ giá trị cho các điều kiện đã bật.";
            return new ThresholdEvaluation(
                hasRequiredReading,
                violated,
                temperatureActive,
                humidityActive,
                sensorKey,
                sensor.Temperature,
                sensor.Humidity,
                schedule.AirTempThresholdEnabled
                    ? schedule.AirTempMax
                    : null,
                schedule.AirHumidityThresholdEnabled
                    ? humidityOnThreshold
                    : null,
                schedule.AirHumidityThresholdEnabled
                    ? humidityOffThreshold
                    : null,
                reason);
        }

        private static bool UpdateAutomationDiagnostics(
            AutoIrrigationScheduleDto schedule,
            ThresholdEvaluation threshold,
            bool cooldownComplete,
            DateTimeOffset nowUtc,
            DateTime nowLocal,
            string? activeSource)
        {
            var status = !schedule.SmartEnabled
                ? "disabled"
                : !threshold.HasRequiredReading
                    ? "sensor-unavailable"
                    : !threshold.IsViolated
                        ? "condition-clear"
                        : activeSource == ThresholdSource
                            ? "watering"
                            : cooldownComplete
                                ? "ready"
                                : "cooldown";
            var lastChecked = ParseUtcDateTimeOffset(schedule.AutomationLastCheckedAt);
            var stale = !lastChecked.HasValue ||
                nowUtc - lastChecked.Value >= DiagnosticsWriteInterval;
            var changed =
                schedule.ThresholdConditionActive != threshold.IsViolated ||
                !string.Equals(schedule.ThresholdStatus, status, StringComparison.Ordinal) ||
                !string.Equals(schedule.ThresholdReason, threshold.Reason, StringComparison.Ordinal) ||
                stale;

            schedule.ThresholdConditionActive = threshold.IsViolated;
            schedule.TemperatureThresholdActive =
                threshold.TemperatureActive;
            schedule.HumidityThresholdActive = threshold.HumidityActive;
            schedule.ThresholdStatus = status;
            schedule.ThresholdReason = threshold.Reason;
            if (changed)
            {
                schedule.AutomationLastCheckedAt = nowUtc.ToString("O");
                schedule.AutomationLastCheckedLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return changed;
        }

        private static string BuildScheduleReason(AutoIrrigationScheduleDto schedule)
        {
            return $"Đến lịch tưới, chu kỳ {Math.Max(1, schedule.IntervalMinutes)} phút.";
        }

        private static string BuildThresholdClearReason(
            ThresholdEvaluation threshold)
        {
            var readings = new List<string>();
            if (threshold.Temperature.HasValue &&
                threshold.TemperatureThreshold.HasValue)
            {
                readings.Add(
                    $"Nhiệt độ {threshold.Temperature.Value:0.##}°C ≤ {threshold.TemperatureThreshold.Value:0.##}°C");
            }
            if (threshold.Humidity.HasValue &&
                threshold.HumidityOffThreshold.HasValue)
            {
                readings.Add(
                    $"Độ ẩm {threshold.Humidity.Value:0.##}% ≥ {threshold.HumidityOffThreshold.Value}%");
            }

            return readings.Count == 0
                ? "Đã về ngưỡng an toàn."
                : $"Đã về ngưỡng an toàn — {string.Join("; ", readings)}.";
        }

        private static bool IsCooldownComplete(
            AutoIrrigationScheduleDto schedule,
            DateTimeOffset nowUtc)
        {
            if (schedule.LastWaterTime <= 0)
            {
                return true;
            }

            var lastStoppedAt =
                DateTimeOffset.FromUnixTimeSeconds(schedule.LastWaterTime);
            return nowUtc - lastStoppedAt >=
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
            SetNextRun(schedule, next);
        }

        private static void SetNextRun(
            AutoIrrigationScheduleDto schedule,
            DateTime nextLocal)
        {
            var nextUtc = ToUtcOffset(nextLocal);
            schedule.NextRunAt = nextUtc.ToString("O");
            schedule.NextRunLocal = nextLocal.ToString("yyyy-MM-dd HH:mm:ss");
            schedule.NextWaterTime = nextUtc.ToUnixTimeSeconds();
        }

        private static void ClearNextRun(AutoIrrigationScheduleDto schedule)
        {
            schedule.NextRunAt = null;
            schedule.NextRunLocal = null;
            schedule.NextWaterTime = 0;
        }

        private static DateTime CalculateNextRun(
            AutoIrrigationScheduleDto schedule,
            DateTime referenceLocal)
        {
            var start = ParseTimeOfDay(schedule.StartTime);
            var end = ParseTimeOfDay(schedule.EndTime);
            var firstRunToday = referenceLocal.Date.Add(start);
            var endToday = referenceLocal.Date.Add(end);
            var interval = TimeSpan.FromMinutes(
                Math.Max(1, schedule.IntervalMinutes));
            DateTime candidate;

            if (schedule.LastWaterTime > 0)
            {
                var lastWaterLocal = TimeZoneInfo.ConvertTime(
                    DateTimeOffset.FromUnixTimeSeconds(schedule.LastWaterTime),
                    VietnamTimeZone).DateTime;
                candidate = lastWaterLocal.Add(interval);
            }
            else
            {
                candidate = firstRunToday;
            }

            if (candidate.Date < referenceLocal.Date)
            {
                candidate = firstRunToday;
            }

            var candidateStart = candidate.Date.Add(start);
            var candidateEnd = candidate.Date.Add(end);
            if (candidate < candidateStart)
            {
                return candidateStart;
            }
            if (candidate >= candidateEnd)
            {
                return candidateStart.AddDays(1);
            }

            if (candidate.Date > referenceLocal.Date)
            {
                return candidate;
            }
            if (referenceLocal < firstRunToday)
            {
                return candidate < firstRunToday ? firstRunToday : candidate;
            }
            if (referenceLocal >= endToday)
            {
                return firstRunToday.AddDays(1);
            }
            if (candidate <= referenceLocal)
            {
                var elapsedTicks = referenceLocal.Ticks - candidate.Ticks;
                var intervalsToSkip = elapsedTicks / interval.Ticks + 1;
                candidate = candidate.AddTicks(interval.Ticks * intervalsToSkip);
            }

            return candidate >= endToday
                ? firstRunToday.AddDays(1)
                : candidate;
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
            if (dto.AirTempThresholdEnabled && !dto.AirTempMax.HasValue)
            {
                throw new ArgumentException("AirTempMax is required when enabled.");
            }
            if (dto.AirTempMin.HasValue && dto.AirTempMax.HasValue &&
                dto.AirTempMin.Value >= dto.AirTempMax.Value)
            {
                throw new ArgumentException("AirTempMax must be greater than AirTempMin.");
            }
            var humidityOn =
                dto.AirHumidityOnThreshold ?? dto.AirHumidityThreshold;
            var humidityOff =
                dto.AirHumidityOffThreshold ?? dto.AirHumidityThreshold;
            if (dto.AirHumidityThresholdEnabled &&
                (!humidityOn.HasValue || !humidityOff.HasValue))
            {
                throw new ArgumentException(
                    "AirHumidityOnThreshold and AirHumidityOffThreshold are required when enabled.");
            }
            if (dto.AirHumidityThresholdEnabled &&
                (dto.AirHumidityOnThreshold.HasValue ||
                    dto.AirHumidityOffThreshold.HasValue) &&
                humidityOn!.Value >= humidityOff!.Value)
            {
                throw new ArgumentException(
                    "AirHumidityOffThreshold must be greater than AirHumidityOnThreshold.");
            }
            if (dto.SmartEnabled &&
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
                ? "Auto - Ngưỡng tưới"
                : "Auto - Lịch tưới";
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

        private sealed record ThresholdEvaluation(
            bool HasRequiredReading,
            bool IsViolated,
            bool TemperatureActive,
            bool HumidityActive,
            string? SensorKey,
            double? Temperature,
            double? Humidity,
            decimal? TemperatureThreshold,
            int? HumidityOnThreshold,
            int? HumidityOffThreshold,
            string Reason);

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
