using System.Windows.Threading;
using System.Text;

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
        _timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public AppSettings Settings => _settings;

    public RuntimeSnapshot CurrentSnapshot => CreateSnapshot(DateTimeOffset.UtcNow);

    public void Start()
    {
        if (_started)
            return;

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
        if (CurrentSnapshot.IsPlanActive)
            throw new InvalidOperationException("已有计划正在执行。");
        if (focusMinutes is < 15 or > 90)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes));
        if (restMinutes is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(restMinutes));
        if (rounds is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(rounds));

        _runtime = new RuntimeState
        {
            PlanId = Guid.NewGuid(),
            PlanStartedUtc = DateTimeOffset.UtcNow,
            Phase = LockPhase.Focus,
            PhaseEndsUtc = DateTimeOffset.UtcNow.AddMinutes(focusMinutes),
            CurrentRound = 1,
            TotalRounds = rounds,
            FocusMinutes = focusMinutes,
            RestMinutes = restMinutes,
            DelayedSleepOccurrenceDate = _runtime.DelayedSleepOccurrenceDate,
            DelayedSleepMinutes = _runtime.DelayedSleepMinutes,
            LastSleepWarningDate = _runtime.LastSleepWarningDate
        };
        SaveAndPublish();
    }

    public void CancelPomodoro(string reason)
    {
        var normalizedReason = reason.Trim();
        if (normalizedReason.EnumerateRunes().Count() < 5)
            throw new ArgumentException("理由不能少于 5 个字。", nameof(reason));
        if (_runtime.Phase != LockPhase.Focus || _runtime.PlanId is null)
            throw new InvalidOperationException("只能在番茄专注期间取消计划；强制休息不能跳过。");

        var remaining = _runtime.PhaseEndsUtc is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((_runtime.PhaseEndsUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));
        _store.AppendPlanEvent(new PlanEventRecord(
            Guid.NewGuid(),
            _runtime.PlanId.Value,
            PlanEventType.PlanCancelled,
            DateTimeOffset.Now,
            normalizedReason,
            _runtime.CurrentRound,
            _runtime.TotalRounds,
            _runtime.FocusMinutes,
            _runtime.RestMinutes,
            remaining));

        ResetToIdle();
    }

    public void EndRestEarly(string reason)
    {
        var normalizedReason = reason.Trim();
        if (normalizedReason.EnumerateRunes().Count() < 5)
            throw new ArgumentException("理由不能少于 5 个字。", nameof(reason));
        if (_runtime.Phase != LockPhase.Rest || _runtime.PlanId is null)
            throw new InvalidOperationException("当前不在番茄休息阶段。");

        var remaining = _runtime.PhaseEndsUtc is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((_runtime.PhaseEndsUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));
        _store.AppendPlanEvent(new PlanEventRecord(
            Guid.NewGuid(),
            _runtime.PlanId.Value,
            PlanEventType.RestEndedEarly,
            DateTimeOffset.Now,
            normalizedReason,
            _runtime.CurrentRound,
            _runtime.TotalRounds,
            _runtime.FocusMinutes,
            _runtime.RestMinutes,
            remaining));

        if (_runtime.CurrentRound >= _runtime.TotalRounds)
        {
            ResetToIdle();
            return;
        }

        _runtime.CurrentRound++;
        _runtime.Phase = LockPhase.Focus;
        _runtime.PhaseEndsUtc = DateTimeOffset.UtcNow.AddMinutes(_runtime.FocusMinutes);
        SaveAndPublish();
    }

    public void StartRestPreview(TimeSpan duration)
    {
        EnsureIdleForPreview();
        _runtime.Phase = LockPhase.RestPreview;
        _runtime.PhaseEndsUtc = DateTimeOffset.UtcNow.Add(duration);
        SaveAndPublish();
    }

    public void StartSleepPreview(TimeSpan duration)
    {
        EnsureIdleForPreview();
        _runtime.Phase = LockPhase.SleepPreview;
        _runtime.PhaseEndsUtc = DateTimeOffset.UtcNow.Add(duration);
        SaveAndPublish();
    }

    public void DelayCurrentSleepOccurrence(int minutes)
    {
        if (minutes is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(minutes));

        var occurrence = FindWarningOccurrence(DateTime.Now);
        if (occurrence is null)
            return;
        if (_runtime.DelayedSleepOccurrenceDate == DateOnly.FromDateTime(occurrence.Value.Start))
            return;

        _runtime.DelayedSleepOccurrenceDate = DateOnly.FromDateTime(occurrence.Value.Start);
        _runtime.DelayedSleepMinutes = minutes;
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
            var sleepEndUtc = new DateTimeOffset(activeSleep.Value.End).ToUniversalTime();
            if (_runtime.Phase != LockPhase.SleepLock || _runtime.PhaseEndsUtc != sleepEndUtc)
            {
                _runtime.Phase = LockPhase.SleepLock;
                _runtime.PhaseEndsUtc = sleepEndUtc;
                SaveAndPublish();
                return;
            }
        }
        else if (_runtime.Phase == LockPhase.SleepLock)
        {
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
                    _runtime.Phase = LockPhase.Rest;
                    _runtime.PhaseEndsUtc = previousEnd.AddMinutes(_runtime.RestMinutes);
                    changed = true;
                    break;
                case LockPhase.Rest:
                    if (_runtime.CurrentRound >= _runtime.TotalRounds)
                    {
                        ResetToIdle(false);
                    }
                    else
                    {
                        _runtime.CurrentRound++;
                        _runtime.Phase = LockPhase.Focus;
                        _runtime.PhaseEndsUtc = previousEnd.AddMinutes(_runtime.FocusMinutes);
                    }
                    changed = true;
                    break;
                case LockPhase.RestPreview:
                case LockPhase.SleepPreview:
                    ResetToIdle(false);
                    changed = true;
                    break;
                default:
                    return;
            }
        }

        if (changed)
            _store.SaveRuntime(_runtime);
    }

    private void CheckSleepWarning(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled || _runtime.Phase is LockPhase.SleepLock or LockPhase.SleepPreview)
            return;

        var occurrence = FindWarningOccurrence(localNow);
        if (occurrence is null)
            return;

        var occurrenceDate = DateOnly.FromDateTime(occurrence.Value.Start);
        if (_runtime.LastSleepWarningDate == occurrenceDate)
            return;

        var warningStart = occurrence.Value.Start.AddSeconds(-_settings.SleepWarningSeconds);
        if (localNow < warningStart || localNow >= occurrence.Value.Start)
            return;

        _runtime.LastSleepWarningDate = occurrenceDate;
        _store.SaveRuntime(_runtime);
        SleepWarningRequested?.Invoke(this, new SleepWarningEventArgs(occurrence.Value.Start, occurrence.Value.End));
    }

    private (DateTime Start, DateTime End)? FindActiveSleepOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled)
            return null;

        foreach (var dayOffset in new[] { -1, 0 })
        {
            var date = localNow.Date.AddDays(dayOffset);
            var occurrence = CreateOccurrence(date);
            if (occurrence is not null && localNow >= occurrence.Value.Start && localNow < occurrence.Value.End)
                return occurrence;
        }

        return null;
    }

    private (DateTime Start, DateTime End)? FindWarningOccurrence(DateTime localNow)
    {
        if (!_settings.SleepProtectionEnabled)
            return null;

        foreach (var dayOffset in new[] { 0, 1 })
        {
            var occurrence = CreateOccurrence(localNow.Date.AddDays(dayOffset));
            if (occurrence is null)
                continue;

            var warningStart = occurrence.Value.Start.AddSeconds(-_settings.SleepWarningSeconds);
            if (localNow >= warningStart && localNow < occurrence.Value.Start)
                return occurrence;
        }

        return null;
    }

    private (DateTime Start, DateTime End)? CreateOccurrence(DateTime startDate)
    {
        if (!_settings.SleepDays.Contains(startDate.DayOfWeek))
            return null;

        var start = startDate.Date + _settings.SleepStart;
        var end = startDate.Date + _settings.SleepEnd;
        if (end <= start)
            end = end.AddDays(1);

        if (_runtime.DelayedSleepOccurrenceDate == DateOnly.FromDateTime(startDate))
            start = start.AddMinutes(Math.Clamp(_runtime.DelayedSleepMinutes, 0, 30));

        return (start, end);
    }

    private RuntimeSnapshot CreateSnapshot(DateTimeOffset utcNow)
    {
        var remaining = _runtime.PhaseEndsUtc is null
            ? TimeSpan.Zero
            : _runtime.PhaseEndsUtc.Value - utcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        var active = _runtime.Phase != LockPhase.Idle;
        var status = _runtime.Phase switch
        {
            LockPhase.Focus => $"第 {_runtime.CurrentRound}/{_runtime.TotalRounds} 轮 · 认真搞事业",
            LockPhase.Rest => $"第 {_runtime.CurrentRound}/{_runtime.TotalRounds} 轮 · 强制降温",
            LockPhase.SleepLock => "今日 AI 亲密额度已用完",
            LockPhase.RestPreview => "休息模式试贴",
            LockPhase.SleepPreview => "睡眠模式试贴",
            _ => "当前没有退烧计划"
        };

        return new RuntimeSnapshot(_runtime.Phase, _runtime.PhaseEndsUtc, remaining,
            _runtime.CurrentRound, _runtime.TotalRounds, active, status);
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
        if (publish)
            Publish();
    }

    private void SaveAndPublish()
    {
        _store.SaveRuntime(_runtime);
        Publish();
    }

    private void Publish() => StateChanged?.Invoke(this, CreateSnapshot(DateTimeOffset.UtcNow));

    private void EnsureIdleForPreview()
    {
        if (_runtime.Phase != LockPhase.Idle)
            throw new InvalidOperationException("当前计划执行期间不能启动预览。");
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (settings.FocusMinutes is < 15 or > 90)
            throw new ArgumentOutOfRangeException(nameof(settings.FocusMinutes));
        if (settings.RestMinutes is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(settings.RestMinutes));
        if (settings.FocusRounds is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(settings.FocusRounds));
        if (settings.SleepStart == settings.SleepEnd)
            throw new ArgumentException("睡眠保护开始时间和结束时间不能相同。");
        if (settings.SleepDays.Count == 0)
            throw new ArgumentException("至少选择一个睡眠保护生效日期。");
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.IsEnabled = false;
    }
}
