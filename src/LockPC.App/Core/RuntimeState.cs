namespace LockPC.App.Core;

public enum LockPhase
{
    // Keep the v1.1.0 numeric values stable because runtime.json stores enums as numbers.
    Idle = 0,
    Focus = 1,
    Rest = 2,
    SleepLock = 3,
    RestTransitionPreview = 4,
    RestPreview = 5,
    RestPeelPreview = 6,
    SleepPreview = 7,
    RestCelebration = 8,
    SleepCelebration = 9,
    SleepTransitionPreview = 10,
    RestCelebrationPreview = 11,
    SleepCelebrationPreview = 12
}

public sealed class RuntimeState
{
    public int SchemaVersion { get; set; }
    public Guid? PlanId { get; set; }
    public DateTimeOffset? PlanStartedUtc { get; set; }
    public LockPhase Phase { get; set; } = LockPhase.Idle;
    public DateTimeOffset? PhaseEndsUtc { get; set; }
    public DateTimeOffset? PhaseStartedUtc { get; set; }
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public int FocusMinutes { get; set; }
    public int RestMinutes { get; set; }
    public int PreviewDurationSeconds { get; set; }
    public DateOnly? DelayedSleepOccurrenceDate { get; set; }
    public int DelayedSleepMinutes { get; set; }
    public DateOnly? LastSleepWarningDate { get; set; }
    public DateOnly? ActiveSleepOccurrenceDate { get; set; }
    public DateTimeOffset? SleepDelayAppliedAtUtc { get; set; }
    public SleepDelaySource? SleepDelaySource { get; set; }
}

public enum PlanEventType { PlanCancelled, RestEndedEarly }

public sealed record PlanEventRecord(Guid Id, Guid PlanId, PlanEventType EventType,
    DateTimeOffset EventAt, string Reason, int CurrentRound, int TotalRounds,
    int FocusMinutes, int RestMinutes, int RemainingSeconds);

public sealed record RuntimeSnapshot(LockPhase Phase, DateTimeOffset? PhaseEndsUtc,
    TimeSpan Remaining, int CurrentRound, int TotalRounds, bool IsPlanActive,
    string StatusText, double PhaseProgress);

public enum LockTransitionKind { Rest, Sleep }

public sealed class LockTransitionEventArgs(LockTransitionKind kind, DateTimeOffset locksAtUtc,
    bool canDelaySleep = false, DateOnly? sleepOccurrenceDate = null) : EventArgs
{
    public LockTransitionKind Kind { get; } = kind;
    public DateTimeOffset LocksAtUtc { get; } = locksAtUtc;
    public bool CanDelaySleep { get; } = canDelaySleep;
    public DateOnly? SleepOccurrenceDate { get; } = sleepOccurrenceDate;
}

public enum SleepDelaySource { WarningWindow, TransitionWindow }

public sealed class SleepDelayRequestEventArgs(DateOnly occurrenceDate, int minutes,
    SleepDelaySource source) : EventArgs
{
    public DateOnly OccurrenceDate { get; } = occurrenceDate;
    public int Minutes { get; } = minutes;
    public SleepDelaySource Source { get; } = source;
}

public enum ActivityEventType
{
    PlanStarted,
    FocusCompleted,
    RestCompleted,
    PlanCompleted,
    PlanCancelled,
    RestEndedEarly,
    SleepStarted,
    SleepCompleted,
    SleepDelayed,
    SleepDelayRejected
}

public sealed record ActivityEventRecord(Guid Id, Guid? PlanId, ActivityEventType EventType,
    DateTimeOffset EventAt, int CurrentRound = 0, int TotalRounds = 0,
    int DurationSeconds = 0, int RemainingSeconds = 0, int DelayMinutes = 0,
    string? Reason = null, DateOnly? SleepOccurrenceDate = null,
    SleepDelaySource? SleepDelaySource = null);

public sealed class SleepWarningEventArgs(DateTime scheduledStartLocal, DateTime scheduledEndLocal,
    bool canDelay, DateOnly occurrenceDate) : EventArgs
{
    public DateTime ScheduledStartLocal { get; } = scheduledStartLocal;
    public DateTime ScheduledEndLocal { get; } = scheduledEndLocal;
    public bool CanDelay { get; } = canDelay;
    public DateOnly OccurrenceDate { get; } = occurrenceDate;
}
