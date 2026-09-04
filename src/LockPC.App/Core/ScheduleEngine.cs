using System.Text;
using System.Windows.Threading;

namespace LockPC.App.Core;

public sealed class ScheduleEngine : IDisposable
{
    private const int CurrentRuntimeSchemaVersion = 2;
    private readonly StateStore _store;
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _localNow;
    private readonly Func<DateTimeOffset> _utcNow;
    private RuntimeState _runtime;
    private AppSettings _settings;
    private bool _started;
    private bool _sessionLocked;
    private string? _lastLockTransitionKey;

    public event EventHandler<RuntimeSnapshot>? StateChanged;
    public event EventHandler<SleepWarningEventArgs>? SleepWarningRequested;
    public event EventHandler<LockTransitionEventArgs>? LockTransitionRequested;

    public ScheduleEngine(StateStore store) : this(store, () => DateTime.Now, () => DateTimeOffset.UtcNow)
    {
    }

    public ScheduleEngine(StateStore store, Func<DateTime> localNow, Func<DateTimeOffset> utcNow)
    {
        _store = store;
        _localNow = localNow;
        _utcNow = utcNow;
        _settings = store.LoadSettings();
        _runtime = store.LoadRuntime();
        MigrateLegacyRuntime(_localNow());
        _timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public AppSettings Settings => _settings;
    public RuntimeSnapshot CurrentSnapshot => CreateSnapshot(_utcNow());

    public void Start()
    {
        if (_started) return;
        _started = true;
        PruneExpiredSleepState(_localNow());
        RecoverExpiredState(_utcNow());
        Tick();
        _timer.Start();
    }

    public void UpdateSettings(AppSettings settings)
    {
        if (CurrentSnapshot.IsPlanActive)
            throw new InvalidOperationException("当前计划执行期间不能修改设置。");
        ValidateSettings(settings);
        _settings = settings;
        _store.SaveSettings(settings);
        Tick();
    }

    public void StartPomodoro(int focusMinutes, int restMinutes, int rounds)
    {
        if (CurrentSnapshot.IsPlanActive) throw new InvalidOperationException("已有计划正在执行。");
        if (focusMinutes is < 15 or > 90) throw new ArgumentOutOfRangeException(nameof(focusMinutes));
        if (restMinutes is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(restMinutes));
        if (rounds is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(rounds));

        var now = _utcNow();
        var planId = Guid.NewGuid();
        _runtime = new RuntimeState
        {
            PlanId = planId,
            PlanStartedUtc = now,
            Phase = LockPhase.Focus,
            PhaseStartedUtc = now,
            PhaseEndsUtc = now.AddMinutes(focusMinutes),
            CurrentRound = 1,
            TotalRounds = rounds,
            FocusMinutes = focusMinutes,
            RestMinutes = restMinutes,
            DelayedSleepOccurrenceDate = _runtime.DelayedSleepOccurrenceDate,
            DelayedSleepMinutes = _runtime.DelayedSleepMinutes,
            LastSleepWarningDate = _runtime.LastSleepWarningDate,
            ActiveSleepOccurrenceDate = _runtime.ActiveSleepOccurrenceDate,
            SchemaVersion = _runtime.SchemaVersion,
            SleepDelayAppliedAtUtc = _runtime.SleepDelayAppliedAtUtc,
            SleepDelaySource = _runtime.SleepDelaySource
        };
        AppendActivity(ActivityEventType.PlanStarted, now, planId);
        SaveAndPublish();
    }

    public void CancelPomodoro(string reason)
    {
        var normalized = ValidateReason(reason);
        if (_runtime.Phase != LockPhase.Focus || _runtime.PlanId is null)
            throw new InvalidOperationException("只能在专注模式生效期间结束本次计划。");
        var remaining = RemainingSeconds();
        _store.AppendPlanEvent(new PlanEventRecord(Guid.NewGuid(), _runtime.PlanId.Value,
            PlanEventType.PlanCancelled, _utcNow().ToLocalTime(), normalized, _runtime.CurrentRound,
            _runtime.TotalRounds, _runtime.FocusMinutes, _runtime.RestMinutes, remaining));
        AppendActivity(ActivityEventType.PlanCancelled, _utcNow(), _runtime.PlanId,
            remainingSeconds: remaining, reason: normalized);
        ResetToIdle();
    }

    public void EndRestEarly(string reason)
    {
        var normalized = ValidateReason(reason);
        if (_runtime.Phase != LockPhase.Rest || _runtime.PlanId is null)
            throw new InvalidOperationException("当前不在休息退烧阶段。");
        var remaining = RemainingSeconds();
        _store.AppendPlanEvent(new PlanEventRecord(Guid.NewGuid(), _runtime.PlanId.Value,
            PlanEventType.RestEndedEarly, _utcNow().ToLocalTime(), normalized, _runtime.CurrentRound,
            _runtime.TotalRounds, _runtime.FocusMinutes, _runtime.RestMinutes, remaining));
        AppendActivity(ActivityEventType.RestEndedEarly, _utcNow(), _runtime.PlanId,
            remainingSeconds: remaining, reason: normalized);

        if (_runtime.CurrentRound >= _runtime.TotalRounds)
        {
            AppendActivity(ActivityEventType.PlanCompleted, _utcNow(), _runtime.PlanId);
            ResetToIdle();
            return;
        }
        StartNextFocus(_utcNow());
        SaveAndPublish();
    }

    public void StartRestPreview(TimeSpan duration)
    {
        EnsureIdleForPreview();
        var now = _utcNow();
        _runtime.Phase = LockPhase.RestTransitionPreview;
        _runtime.PhaseStartedUtc = now;
        _runtime.PhaseEndsUtc = now.AddSeconds(AppMetadata.LockTransitionSeconds);
        _runtime.PreviewDurationSeconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));
        SaveAndPublish();
        RequestLockTransitionOnce($"rest-preview:{_runtime.PhaseEndsUtc.Value.UtcTicks}",
            new LockTransitionEventArgs(LockTransitionKind.Rest, _runtime.PhaseEndsUtc.Value));
    }
    public void StartRestPeelPreview(TimeSpan duration) => StartPreview(LockPhase.RestPeelPreview, duration);
    public void StartSleepPreview(TimeSpan duration)
    {
        EnsureIdleForPreview();
        var now = _utcNow();
        _runtime.Phase = LockPhase.SleepTransitionPreview;
        _runtime.PhaseStartedUtc = now;
        _runtime.PhaseEndsUtc = now.AddSeconds(AppMetadata.LockTransitionSeconds);
        _runtime.PreviewDurationSeconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));
        SaveAndPublish();
        RequestLockTransitionOnce($"sleep-preview:{_runtime.PhaseEndsUtc.Value.UtcTicks}",
            new LockTransitionEventArgs(LockTransitionKind.Sleep, _runtime.PhaseEndsUtc.Value));
    }

    public void EndPreview()
    {
        if (_runtime.Phase is not (LockPhase.RestTransitionPreview or LockPhase.RestPreview or LockPhase.RestPeelPreview or
            LockPhase.SleepTransitionPreview or LockPhase.SleepPreview or LockPhase.RestCelebrationPreview or LockPhase.SleepCelebrationPreview))
            throw new InvalidOperationException("当前没有可结束的演示。");
        ResetToIdle();
    }

    public bool DelayCurrentSleepOccurrence(DateOnly occurrenceDate, int minutes, SleepDelaySource source)
    {
        if (minutes is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(minutes));
        var occurrence = FindWarningOccurrence(_localNow());
        if (occurrence is null)
            return RejectSleepDelay(occurrenceDate, minutes, source, "当前不在允许延迟的提醒时间内");
        if (occurrence.Value.OccurrenceDate != occurrenceDate)
            return RejectSleepDelay(occurrenceDate, minutes, source, "延迟请求不属于当前睡眠计划");
        if (_runtime.DelayedSleepOccurrenceDate == occurrenceDate)
            return RejectSleepDelay(occurrenceDate, minutes, source, "本次睡眠计划已经延迟过一次");

        _runtime.DelayedSleepOccurrenceDate = occurrenceDate;
        _runtime.DelayedSleepMinutes = minutes;
        _runtime.SleepDelayAppliedAtUtc = _utcNow();
        _runtime.SleepDelaySource = source;
        AppendActivity(ActivityEventType.SleepDelayed, _utcNow(), delayMinutes: minutes,
            sleepOccurrenceDate: occurrenceDate, sleepDelaySource: source);
        _store.SaveRuntime(_runtime);
        Publish();
        return true;
    }

    public void SetSessionLocked(bool locked)
    {
        _sessionLocked = locked;
        if (!locked && _runtime.Phase == LockPhase.SleepCelebration && _runtime.PhaseEndsUtc is null)
        {
            var now = _utcNow();
            _runtime.PhaseStartedUtc = now;
            _runtime.PhaseEndsUtc = now.AddSeconds(AppMetadata.CelebrationSeconds);
            SaveAndPublish();
        }
    }

    private void Tick()
    {
        var utcNow = _utcNow();
        var localNow = _localNow();
        PruneExpiredSleepState(localNow);
        var activeSleep = FindActiveSleepOccurrence(localNow);
        if (activeSleep is not null && !IsSleepPreviewPhase(_runtime.Phase))
        {
            var occurrenceDate = activeSleep.Value.OccurrenceDate;
            var sleepEndUtc = new DateTimeOffset(activeSleep.Value.End).ToUniversalTime();
            if (_runtime.Phase != LockPhase.SleepLock || _runtime.PhaseEndsUtc != sleepEndUtc)
            {
                _runtime.Phase = LockPhase.SleepLock;
                _runtime.PhaseStartedUtc = utcNow;
                _runtime.PhaseEndsUtc = sleepEndUtc;
                if (_runtime.ActiveSleepOccurrenceDate != occurrenceDate)
                {
                    _runtime.ActiveSleepOccurrenceDate = occurrenceDate;
                    AppendActivity(ActivityEventType.SleepStarted, utcNow,
                        sleepOccurrenceDate: occurrenceDate);
                }
                SaveAndPublish();
                return;
            }
        }
        else if (_runtime.Phase == LockPhase.SleepLock)
        {
            var completedOccurrenceDate = _runtime.ActiveSleepOccurrenceDate;
            AppendActivity(ActivityEventType.SleepCompleted, utcNow,
                durationSeconds: ElapsedSeconds(utcNow),
                delayMinutes: completedOccurrenceDate is not null &&
                    _runtime.DelayedSleepOccurrenceDate == completedOccurrenceDate
                        ? _runtime.DelayedSleepMinutes
                        : 0,
                sleepOccurrenceDate: completedOccurrenceDate);
            ClearSleepOccurrenceState(completedOccurrenceDate);
            StartCelebration(LockPhase.SleepCelebration, utcNow, waitForSessionUnlock: _sessionLocked);
            return;
        }

        RecoverExpiredState(utcNow);
        CheckSleepWarning(localNow);
        CheckLockTransition(localNow, utcNow);
        Publish();
    }

    private void RecoverExpiredState(DateTimeOffset utcNow)
    {
        var changed = false;
        var guard = 0;
        while (_runtime.PhaseEndsUtc is not null && _runtime.PhaseEndsUtc <= utcNow && guard++ < 32)
        {
            var previousEnd = _runtime.PhaseEndsUtc.Value;
            switch (_runtime.Phase)
            {
                case LockPhase.Focus:
                    AppendActivity(ActivityEventType.FocusCompleted, previousEnd, _runtime.PlanId,
                        durationSeconds: _runtime.FocusMinutes * 60);
                    _runtime.Phase = LockPhase.Rest;
                    _runtime.PhaseStartedUtc = previousEnd;
                    _runtime.PhaseEndsUtc = previousEnd.AddMinutes(_runtime.RestMinutes);
                    changed = true;
                    break;
                case LockPhase.Rest:
                    AppendActivity(ActivityEventType.RestCompleted, previousEnd, _runtime.PlanId,
                        durationSeconds: _runtime.RestMinutes * 60);
                    if (_runtime.CurrentRound >= _runtime.TotalRounds)
                        AppendActivity(ActivityEventType.PlanCompleted, previousEnd, _runtime.PlanId);
                    StartCelebration(LockPhase.RestCelebration, utcNow, publish: false);
                    changed = true;
                    break;
                case LockPhase.RestCelebration:
                    if (_runtime.CurrentRound >= _runtime.TotalRounds)
                        ResetToIdle(false);
                    else
                        StartNextFocus(previousEnd);
                    changed = true;
                    break;
                case LockPhase.SleepCelebration:
                    ResetToIdle(false);
                    changed = true;
                    break;
                case LockPhase.RestPreview:
                    StartCelebration(LockPhase.RestCelebrationPreview, utcNow, publish: false);
                    changed = true;
                    break;
                case LockPhase.RestPeelPreview:
                    ResetToIdle(false);
                    changed = true;
                    break;
                case LockPhase.RestTransitionPreview:
                    _runtime.Phase = LockPhase.RestPreview;
                    _runtime.PhaseStartedUtc = previousEnd;
                    _runtime.PhaseEndsUtc = previousEnd.AddSeconds(Math.Max(1, _runtime.PreviewDurationSeconds));
                    changed = true;
                    break;
                case LockPhase.SleepTransitionPreview:
                    _runtime.Phase = LockPhase.SleepPreview;
                    _runtime.PhaseStartedUtc = previousEnd;
                    _runtime.PhaseEndsUtc = previousEnd.AddSeconds(Math.Max(1, _runtime.PreviewDurationSeconds));
                    changed = true;
                    break;
                case LockPhase.SleepPreview:
                    StartCelebration(LockPhase.SleepCelebrationPreview, utcNow, publish: false);
                    changed = true;
                    break;
                case LockPhase.RestCelebrationPreview:
                case LockPhase.SleepCelebrationPreview:
                    ResetToIdle(false);
                    changed = true;
                    break;
                default:
                    return;
            }
        }
        if (changed) _store.SaveRuntime(_runtime);
    }

    private void StartNextFocus(DateTimeOffset start)
    {
        _runtime.CurrentRound++;
        _runtime.Phase = LockPhase.Focus;
        _runtime.PhaseStartedUtc = start;
        _runtime.PhaseEndsUtc = start.AddMinutes(_runtime.FocusMinutes);
    }

    private void StartPreview(LockPhase phase, TimeSpan duration)
    {
        EnsureIdleForPreview();
        var now = _utcNow();
        _runtime.Phase = phase;
        _runtime.PhaseStartedUtc = now;
        _runtime.PhaseEndsUtc = now.Add(duration);
        SaveAndPublish();
    }

    private void StartCelebration(LockPhase phase, DateTimeOffset start,
        bool waitForSessionUnlock = false, bool publish = true)
    {
        _runtime.Phase = phase;
        _runtime.PhaseStartedUtc = start;
        _runtime.PhaseEndsUtc = waitForSessionUnlock ? null : start.AddSeconds(AppMetadata.CelebrationSeconds);
        if (publish) SaveAndPublish();
    }

    private void CheckSleepWarning(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled || _runtime.Phase is LockPhase.SleepLock || IsSleepPreviewPhase(_runtime.Phase)) return;
        var occurrence = FindWarningOccurrence(localNow);
        if (occurrence is null) return;
        var date = occurrence.Value.OccurrenceDate;
        if (_runtime.LastSleepWarningDate == date) return;
        var warningStart = occurrence.Value.Start.AddSeconds(-_settings.SleepWarningSeconds);
        if (localNow < warningStart || localNow >= occurrence.Value.Start) return;
        _runtime.LastSleepWarningDate = date;
        _store.SaveRuntime(_runtime);
        SleepWarningRequested?.Invoke(this, new SleepWarningEventArgs(occurrence.Value.Start, occurrence.Value.End,
            _runtime.DelayedSleepOccurrenceDate != date, date));
    }

    private void CheckLockTransition(DateTime localNow, DateTimeOffset utcNow)
    {
        var sleepOccurrence = FindLockTransitionSleepOccurrence(localNow);
        if (sleepOccurrence is not null)
        {
            var occurrenceDate = sleepOccurrence.Value.OccurrenceDate;
            var locksAtUtc = new DateTimeOffset(sleepOccurrence.Value.Start).ToUniversalTime();
            RequestLockTransitionOnce($"sleep:{locksAtUtc.UtcTicks}",
                new LockTransitionEventArgs(LockTransitionKind.Sleep, locksAtUtc,
                    _runtime.DelayedSleepOccurrenceDate != occurrenceDate, occurrenceDate));
            return;
        }

        if (_runtime.Phase != LockPhase.Focus || _runtime.PhaseEndsUtc is null)
            return;

        var remaining = _runtime.PhaseEndsUtc.Value - utcNow;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromSeconds(AppMetadata.LockTransitionSeconds))
            return;

        var key = $"rest:{_runtime.PlanId}:{_runtime.CurrentRound}:{_runtime.PhaseEndsUtc.Value.UtcTicks}";
        RequestLockTransitionOnce(key,
            new LockTransitionEventArgs(LockTransitionKind.Rest, _runtime.PhaseEndsUtc.Value));
    }

    private SleepOccurrence? FindLockTransitionSleepOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled) return null;
        foreach (var offset in new[] { 0, 1 })
        {
            var occurrence = CreateOccurrence(localNow.Date.AddDays(offset));
            if (occurrence is null) continue;
            var remaining = occurrence.Value.Start - localNow;
            if (remaining > TimeSpan.Zero && remaining <= TimeSpan.FromSeconds(AppMetadata.LockTransitionSeconds))
                return occurrence;
        }
        return null;
    }

    private void RequestLockTransitionOnce(string key, LockTransitionEventArgs args)
    {
        if (_lastLockTransitionKey == key) return;
        _lastLockTransitionKey = key;
        LockTransitionRequested?.Invoke(this, args);
    }

    private SleepOccurrence? FindActiveSleepOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled) return null;
        foreach (var offset in new[] { -1, 0 })
        {
            var occurrence = CreateOccurrence(localNow.Date.AddDays(offset));
            if (occurrence is not null && localNow >= occurrence.Value.Start && localNow < occurrence.Value.End) return occurrence;
        }
        return null;
    }

    private SleepOccurrence? FindWarningOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled) return null;
        foreach (var offset in new[] { 0, 1 })
        {
            var occurrence = CreateOccurrence(localNow.Date.AddDays(offset));
            if (occurrence is null) continue;
            var warningStart = occurrence.Value.Start.AddSeconds(-_settings.SleepWarningSeconds);
            if (localNow >= warningStart && localNow < occurrence.Value.Start) return occurrence;
        }
        return null;
    }

    private SleepOccurrence? CreateOccurrence(DateTime startDate) =>
        SleepSchedule.Create(_settings, _runtime, startDate);

    private RuntimeSnapshot CreateSnapshot(DateTimeOffset utcNow)
    {
        var remaining = _runtime.PhaseEndsUtc is null ? TimeSpan.Zero : _runtime.PhaseEndsUtc.Value - utcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var status = _runtime.Phase switch
        {
            LockPhase.Focus => "专注模式 · 正在生效",
            LockPhase.Rest => "退烧贴正在生效",
            LockPhase.RestCelebration => "休息完成 · 降温成功",
            LockPhase.SleepLock => "睡眠保护 · 正在生效",
            LockPhase.SleepCelebration => "睡眠保护完成 · 早安",
            LockPhase.RestTransitionPreview => "休息模式 · 过渡演示",
            LockPhase.RestPreview => "休息模式演示",
            LockPhase.RestPeelPreview => "提前撕贴演示",
            LockPhase.SleepTransitionPreview => "睡眠保护 · 过渡演示",
            LockPhase.SleepPreview => "睡眠保护演示",
            LockPhase.RestCelebrationPreview => "休息完成撒花演示",
            LockPhase.SleepCelebrationPreview => "睡眠保护完成撒花演示",
            _ => "当前没有专注计划"
        };
        var progress = 0d;
        if (_runtime.PhaseStartedUtc is not null && _runtime.PhaseEndsUtc is not null)
        {
            var total = (_runtime.PhaseEndsUtc.Value - _runtime.PhaseStartedUtc.Value).TotalSeconds;
            if (total > 0) progress = Math.Clamp((utcNow - _runtime.PhaseStartedUtc.Value).TotalSeconds / total, 0, 1);
        }
        return new RuntimeSnapshot(_runtime.Phase, _runtime.PhaseEndsUtc, remaining,
            _runtime.CurrentRound, _runtime.TotalRounds, _runtime.Phase != LockPhase.Idle, status, progress);
    }

    private void AppendActivity(ActivityEventType type, DateTimeOffset at, Guid? planId = null,
        int durationSeconds = 0, int remainingSeconds = 0, int delayMinutes = 0, string? reason = null,
        DateOnly? sleepOccurrenceDate = null, SleepDelaySource? sleepDelaySource = null) =>
        _store.AppendActivityEvent(new ActivityEventRecord(Guid.NewGuid(), planId, type, at.ToLocalTime(),
            _runtime.CurrentRound, _runtime.TotalRounds, durationSeconds, remainingSeconds, delayMinutes, reason,
            sleepOccurrenceDate, sleepDelaySource));

    private int RemainingSeconds() => _runtime.PhaseEndsUtc is null ? 0 :
        Math.Max(0, (int)Math.Ceiling((_runtime.PhaseEndsUtc.Value - _utcNow()).TotalSeconds));

    private int ElapsedSeconds(DateTimeOffset now) => _runtime.PhaseStartedUtc is null ? 0 :
        Math.Max(0, (int)Math.Floor((now - _runtime.PhaseStartedUtc.Value).TotalSeconds));

    private static string ValidateReason(string reason)
    {
        var normalized = reason.Trim();
        if (normalized.EnumerateRunes().Count() < 5)
            throw new ArgumentException("理由不能少于 5 个字。", nameof(reason));
        return normalized;
    }

    private static bool IsSleepPreviewPhase(LockPhase phase) => phase is
        LockPhase.SleepTransitionPreview or LockPhase.SleepPreview or LockPhase.SleepCelebrationPreview;

    private bool RejectSleepDelay(DateOnly occurrenceDate, int minutes, SleepDelaySource source, string reason)
    {
        AppendActivity(ActivityEventType.SleepDelayRejected, _utcNow(),
            delayMinutes: minutes, reason: reason, sleepOccurrenceDate: occurrenceDate,
            sleepDelaySource: source);
        return false;
    }

    private void ClearSleepOccurrenceState(DateOnly? occurrenceDate)
    {
        if (occurrenceDate is null || _runtime.DelayedSleepOccurrenceDate == occurrenceDate)
        {
            _runtime.DelayedSleepOccurrenceDate = null;
            _runtime.DelayedSleepMinutes = 0;
            _runtime.SleepDelayAppliedAtUtc = null;
            _runtime.SleepDelaySource = null;
        }
        if (occurrenceDate is null || _runtime.LastSleepWarningDate == occurrenceDate)
            _runtime.LastSleepWarningDate = null;
        if (occurrenceDate is null || _runtime.ActiveSleepOccurrenceDate == occurrenceDate)
            _runtime.ActiveSleepOccurrenceDate = null;
    }

    private void PruneExpiredSleepState(DateTime localNow)
    {
        var changed = false;
        if (_runtime.DelayedSleepOccurrenceDate is { } delayedDate)
        {
            var occurrence = CreateOccurrence(delayedDate.ToDateTime(TimeOnly.MinValue));
            if ((occurrence is null || SleepSchedule.IsOccurrenceExpired(occurrence.Value, localNow)) &&
                !(_runtime.Phase == LockPhase.SleepLock && _runtime.ActiveSleepOccurrenceDate == delayedDate))
            {
                ClearSleepOccurrenceState(delayedDate);
                changed = true;
            }
        }
        if (_runtime.LastSleepWarningDate is { } warningDate)
        {
            var occurrence = CreateOccurrence(warningDate.ToDateTime(TimeOnly.MinValue));
            if (occurrence is null || SleepSchedule.IsOccurrenceExpired(occurrence.Value, localNow))
            {
                _runtime.LastSleepWarningDate = null;
                changed = true;
            }
        }
        if (changed) _store.SaveRuntime(_runtime);
    }

    private void MigrateLegacyRuntime(DateTime localNow)
    {
        if (_runtime.SchemaVersion >= CurrentRuntimeSchemaVersion) return;

        var preserveDelay = SleepSchedule.HasValidLegacyDelayEvidence(
            _runtime, _settings, _store.LoadActivityEvents());
        if (!preserveDelay)
        {
            _runtime.DelayedSleepOccurrenceDate = null;
            _runtime.DelayedSleepMinutes = 0;
            _runtime.SleepDelayAppliedAtUtc = null;
            _runtime.SleepDelaySource = null;
        }
        _runtime.LastSleepWarningDate = null;
        if (_runtime.Phase != LockPhase.SleepLock)
            _runtime.ActiveSleepOccurrenceDate = null;
        _runtime.SchemaVersion = CurrentRuntimeSchemaVersion;
        _store.SaveRuntime(_runtime);
        PruneExpiredSleepState(localNow);
    }

    private void ResetToIdle(bool publish = true)
    {
        var delayedDate = _runtime.DelayedSleepOccurrenceDate;
        var delayedMinutes = _runtime.DelayedSleepMinutes;
        var warningDate = _runtime.LastSleepWarningDate;
        var delayAppliedAtUtc = _runtime.SleepDelayAppliedAtUtc;
        var delaySource = _runtime.SleepDelaySource;
        _runtime = new RuntimeState
        {
            SchemaVersion = CurrentRuntimeSchemaVersion,
            DelayedSleepOccurrenceDate = delayedDate,
            DelayedSleepMinutes = delayedMinutes,
            LastSleepWarningDate = warningDate,
            SleepDelayAppliedAtUtc = delayAppliedAtUtc,
            SleepDelaySource = delaySource
        };
        _store.SaveRuntime(_runtime);
        if (publish) Publish();
    }

    private void SaveAndPublish() { _store.SaveRuntime(_runtime); Publish(); }
    private void Publish() => StateChanged?.Invoke(this, CreateSnapshot(_utcNow()));
    private void EnsureIdleForPreview()
    {
        if (_runtime.Phase != LockPhase.Idle) throw new InvalidOperationException("当前计划执行期间不能启动演示。");
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (settings.FocusMinutes is < 15 or > 90) throw new ArgumentOutOfRangeException(nameof(settings.FocusMinutes));
        if (settings.RestMinutes is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(settings.RestMinutes));
        if (settings.FocusRounds is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(settings.FocusRounds));
        if (settings.SleepStart == settings.SleepEnd) throw new ArgumentException("睡眠保护开始时间和结束时间不能相同。");
        if (settings.SleepDays.Count == 0) throw new ArgumentException("至少选择一个睡眠保护生效日期。");
    }

    public void Dispose() { _timer.Stop(); _timer.IsEnabled = false; }
}
