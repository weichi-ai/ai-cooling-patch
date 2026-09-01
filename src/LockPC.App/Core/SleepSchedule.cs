namespace LockPC.App.Core;

public readonly record struct SleepOccurrence(
    DateOnly OccurrenceDate,
    DateTime ScheduledStart,
    DateTime Start,
    DateTime End);

public static class SleepSchedule
{
    public static SleepOccurrence? Create(AppSettings settings, RuntimeState runtime, DateTime scheduleDate)
    {
        var occurrenceDate = DateOnly.FromDateTime(scheduleDate);
        if (!settings.SleepDays.Contains(scheduleDate.DayOfWeek)) return null;

        var scheduledStart = scheduleDate.Date + settings.SleepStart;
        var end = scheduleDate.Date + settings.SleepEnd;
        if (end <= scheduledStart) end = end.AddDays(1);

        var start = scheduledStart;
        if (runtime.DelayedSleepOccurrenceDate == occurrenceDate)
            start = start.AddMinutes(Math.Clamp(runtime.DelayedSleepMinutes, 0, 30));

        return new SleepOccurrence(occurrenceDate, scheduledStart, start, end);
    }

    public static bool HasValidLegacyDelayEvidence(RuntimeState runtime, AppSettings settings,
        IEnumerable<ActivityEventRecord> events)
    {
        if (runtime.DelayedSleepOccurrenceDate is not { } occurrenceDate ||
            runtime.DelayedSleepMinutes is < 1 or > 30)
            return false;

        var scheduledStart = occurrenceDate.ToDateTime(TimeOnly.MinValue) + settings.SleepStart;
        var warningStart = scheduledStart.AddSeconds(-settings.SleepWarningSeconds);
        return events.Any(item => item.EventType == ActivityEventType.SleepDelayed &&
            item.DelayMinutes == runtime.DelayedSleepMinutes &&
            item.EventAt.LocalDateTime >= warningStart &&
            item.EventAt.LocalDateTime < scheduledStart);
    }

    public static bool IsOccurrenceExpired(SleepOccurrence occurrence, DateTime localNow) =>
        localNow >= occurrence.End;
}
