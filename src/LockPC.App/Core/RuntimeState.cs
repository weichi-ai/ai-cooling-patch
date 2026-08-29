namespace LockPC.App.Core;

public enum LockPhase
{
    Idle,
    Focus,
    Rest,
    SleepLock,
    RestPreview,
    RestPeelPreview,
    SleepPreview
}

public sealed class RuntimeState
{
    public Guid? PlanId { get; set; }
    public DateTimeOffset? PlanStartedUtc { get; set; }
    public LockPhase Phase { get; set; } = LockPhase.Idle;
    public DateTimeOffset? PhaseEndsUtc { get; set; }
    public DateTimeOffset? PhaseStartedUtc { get; set; }
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public int FocusMinutes { get; set; }
    public int RestMinutes { get; set; }
    public DateOnly? DelayedSleepOccurrenceDate { get; set; }
    public int DelayedSleepMinutes { get; set; }
    public DateOnly? LastSleepWarningDate { get; set; }
    public DateOnly? ActiveSleepOccurrenceDate { get; set; }
}

public enum PlanEventType { PlanCancelled, RestEndedEarly }

public sealed record PlanEventRecord(Guid Id, Guid PlanId, PlanEventType EventType,
    DateTimeOffset EventAt, string Reason, int CurrentRound, int TotalRounds,
    int FocusMinutes, int RestMinutes, int RemainingSeconds);

public sealed record RuntimeSnapshot(LockPhase Phase, DateTimeOffset? PhaseEndsUtc,
    TimeSpan Remaining, int CurrentRound, int TotalRounds, bool IsPlanActive,
    string StatusText, double PhaseProgress);

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
    SleepDelayed
}

public sealed record ActivityEventRecord(Guid Id, Guid? PlanId, ActivityEventType EventType,
    DateTimeOffset EventAt, int CurrentRound = 0, int TotalRounds = 0,
    int DurationSeconds = 0, int RemainingSeconds = 0, int DelayMinutes = 0,
    string? Reason = null);

public sealed class SleepWarningEventArgs(DateTime scheduledStartLocal, DateTime scheduledEndLocal) : EventArgs
{
    public DateTime ScheduledStartLocal { get; } = scheduledStartLocal;
    public DateTime ScheduledEndLocal { get; } = scheduledEndLocal;
}