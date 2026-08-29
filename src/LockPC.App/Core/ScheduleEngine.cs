using System.Text;
using System.Windows.Threading;

namespace LockPC.App.Core;

public sealed class ScheduleEngine : IDisposable
{
    private readonly StateStore _store;
    private readonly DispatcherTimer _timer;
    private RuntimeState _runtime;
    private AppSettings _settings;
    private bool _started;

    public event EventHandler<RuntimeSnapshot>? StateChanged;
    public event EventHandler<SleepWarningEventArgs>? SleepWarningRequested;

    public ScheduleEngine(StateStore store)
    {
        _store = store;
        _settings = store.LoadSettings();
        _runtime = store.LoadRuntime();
        _timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public AppSettings Settings => _settings;
    public RuntimeSnapshot CurrentSnapshot => CreateSnapshot(DateTimeOffset.UtcNow);

    public void Start()
    {
        if (_started) return;
        _started = true;
        RecoverExpiredState(DateTimeOffset.UtcNow);
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

        var now = DateTimeOffset.UtcNow;
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
            ActiveSleepOccurrenceDate = _runtime.ActiveSleepOccurrenceDate
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
            PlanEventType.PlanCancelled, DateTimeOffset.Now, normalized, _runtime.CurrentRound,
            _runtime.TotalRounds, _runtime.FocusMinutes, _runtime.RestMinutes, remaining));
        AppendActivity(ActivityEventType.PlanCancelled, DateTimeOffset.UtcNow, _runtime.PlanId,
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
            PlanEventType.RestEndedEarly, DateTimeOffset.Now, normalized, _runtime.CurrentRound,
            _runtime.TotalRounds, _runtime.FocusMinutes, _runtime.RestMinutes, remaining));
        AppendActivity(ActivityEventType.RestEndedEarly, DateTimeOffset.UtcNow, _runtime.PlanId,
            remainingSeconds: remaining, reason: normalized);

        if (_runtime.CurrentRound >= _runtime.TotalRounds)
        {
            AppendActivity(ActivityEventType.PlanCompleted, DateTimeOffset.UtcNow, _runtime.PlanId);
            ResetToIdle();
            return;
        }
        StartNextFocus(DateTimeOffset.UtcNow);
        SaveAndPublish();
    }

    public void StartRestPreview(TimeSpan duration) => StartPreview(LockPhase.RestPreview, duration);
    public void StartRestPeelPreview(TimeSpan duration) => StartPreview(LockPhase.RestPeelPreview, duration);
    public void StartSleepPreview(TimeSpan duration) => StartPreview(LockPhase.SleepPreview, duration);

    public void EndPreview()
    {
        if (_runtime.Phase is not (LockPhase.RestPreview or LockPhase.RestPeelPreview or LockPhase.SleepPreview))
            throw new InvalidOperationException("当前没有可结束的演示。");
        ResetToIdle();
    }

    public void DelayCurrentSleepOccurrence(int minutes)
    {
        if (minutes is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(minutes));
        var occurrence = FindWarningOccurrence(DateTime.Now);
        if (occurrence is null) return;
        var occurrenceDate = DateOnly.FromDateTime(occurrence.Value.Start);
        if (_runtime.DelayedSleepOccurrenceDate == occurrenceDate) return;
        _runtime.DelayedSleepOccurrenceDate = occurrenceDate;
        _runtime.DelayedSleepMinutes = minutes;
        AppendActivity(ActivityEventType.SleepDelayed, DateTimeOffset.UtcNow, delayMinutes: minutes);
        _store.SaveRuntime(_runtime);
        Publish();
    }

    private void Tick()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var localNow = DateTime.Now;
        var activeSleep = FindActiveSleepOccurrence(localNow);
        if (activeSleep is not null && _runtime.Phase is not LockPhase.SleepPreview)
        {
            var occurrenceDate = DateOnly.FromDateTime(activeSleep.Value.Start);
            var sleepEndUtc = new DateTimeOffset(activeSleep.Value.End).ToUniversalTime();
            if (_runtime.Phase != LockPhase.SleepLock || _runtime.PhaseEndsUtc != sleepEndUtc)
            {
                _runtime.Phase = LockPhase.SleepLock;
                _runtime.PhaseStartedUtc = utcNow;
                _runtime.PhaseEndsUtc = sleepEndUtc;
                if (_runtime.ActiveSleepOccurrenceDate != occurrenceDate)
                {
                    _runtime.ActiveSleepOccurrenceDate = occurrenceDate;
                    AppendActivity(ActivityEventType.SleepStarted, utcNow);
                }
                SaveAndPublish();
                return;
            }
        }
        else if (_runtime.Phase == LockPhase.SleepLock)
        {
            AppendActivity(ActivityEventType.SleepCompleted, utcNow,
                durationSeconds: ElapsedSeconds(utcNow));
            ResetToIdle();
            return;
        }

        RecoverExpiredState(utcNow);
        CheckSleepWarning(localNow);
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
                    {
                        AppendActivity(ActivityEventType.PlanCompleted, previousEnd, _runtime.PlanId);
                        ResetToIdle(false);
                    }
                    else
                    {
                        StartNextFocus(previousEnd);
                    }
                    changed = true;
                    break;
                case LockPhase.RestPreview:
                case LockPhase.RestPeelPreview:
                case LockPhase.SleepPreview:
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
        var now = DateTimeOffset.UtcNow;
        _runtime.Phase = phase;
        _runtime.PhaseStartedUtc = now;
        _runtime.PhaseEndsUtc = now.Add(duration);
        SaveAndPublish();
    }

    private void CheckSleepWarning(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled || _runtime.Phase is LockPhase.SleepLock or LockPhase.SleepPreview) return;
        var occurrence = FindWarningOccurrence(localNow);
        if (occurrence is null) return;
        var date = DateOnly.FromDateTime(occurrence.Value.Start);
        if (_runtime.LastSleepWarningDate == date) return;
        var warningStart = occurrence.Value.Start.AddSeconds(-_settings.SleepWarningSeconds);
        if (localNow < warningStart || localNow >= occurrence.Value.Start) return;
        _runtime.LastSleepWarningDate = date;
        _store.SaveRuntime(_runtime);
        SleepWarningRequested?.Invoke(this, new SleepWarningEventArgs(occurrence.Value.Start, occurrence.Value.End));
    }

    private (DateTime Start, DateTime End)? FindActiveSleepOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled) return null;
        foreach (var offset in new[] { -1, 0 })
        {
            var occurrence = CreateOccurrence(localNow.Date.AddDays(offset));
            if (occurrence is not null && localNow >= occurrence.Value.Start && localNow < occurrence.Value.End) return occurrence;
        }
        return null;
    }

    private (DateTime Start, DateTime End)? FindWarningOccurrence(DateTime localNow)
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

    private (DateTime Start, DateTime End)? CreateOccurrence(DateTime startDate)
    {
        if (!_settings.SleepDays.Contains(startDate.DayOfWeek)) return null;
        var start = startDate.Date + _settings.SleepStart;
        var end = startDate.Date + _settings.SleepEnd;
        if (end <= start) end = end.AddDays(1);
        if (_runtime.DelayedSleepOccurrenceDate == DateOnly.FromDateTime(startDate))
            start = start.AddMinutes(Math.Clamp(_runtime.DelayedSleepMinutes, 0, 30));
        return (start, end);
    }

    private RuntimeSnapshot CreateSnapshot(DateTimeOffset utcNow)
    {
        var remaining = _runtime.PhaseEndsUtc is null ? TimeSpan.Zero : _runtime.PhaseEndsUtc.Value - utcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var status = _runtime.Phase switch
        {
            LockPhase.Focus => "专注模式 · 正在生效",
            LockPhase.Rest => "退烧贴正在生效",
            LockPhase.SleepLock => "睡眠保护 · 正在生效",
            LockPhase.RestPreview => "休息模式演示",
            LockPhase.RestPeelPreview => "提前撕贴演示",
            LockPhase.SleepPreview => "睡眠保护演示",
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
        int durationSeconds = 0, int remainingSeconds = 0, int delayMinutes = 0, string? reason = null) =>
        _store.AppendActivityEvent(new ActivityEventRecord(Guid.NewGuid(), planId, type, at.ToLocalTime(),
            _runtime.CurrentRound, _runtime.TotalRounds, durationSeconds, remainingSeconds, delayMinutes, reason));

    private int RemainingSeconds() => _runtime.PhaseEndsUtc is null ? 0 :
        Math.Max(0, (int)Math.Ceiling((_runtime.PhaseEndsUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));

    private int ElapsedSeconds(DateTimeOffset now) => _runtime.PhaseStartedUtc is null ? 0 :
        Math.Max(0, (int)Math.Floor((now - _runtime.PhaseStartedUtc.Value).TotalSeconds));

    private static string ValidateReason(string reason)
    {
        var normalized = reason.Trim();
        if (normalized.EnumerateRunes().Count() < 5)
            throw new ArgumentException("理由不能少于 5 个字。", nameof(reason));
        return normalized;
    }

    private void ResetToIdle(bool publish = true)
    {
        var delayedDate = _runtime.DelayedSleepOccurrenceDate;
        var delayedMinutes = _runtime.DelayedSleepMinutes;
        var warningDate = _runtime.LastSleepWarningDate;
        _runtime = new RuntimeState
        {
            DelayedSleepOccurrenceDate = delayedDate,
            DelayedSleepMinutes = delayedMinutes,
            LastSleepWarningDate = warningDate
        };
        _store.SaveRuntime(_runtime);
        if (publish) Publish();
    }

    private void SaveAndPublish() { _store.SaveRuntime(_runtime); Publish(); }
    private void Publish() => StateChanged?.Invoke(this, CreateSnapshot(DateTimeOffset.UtcNow));
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
