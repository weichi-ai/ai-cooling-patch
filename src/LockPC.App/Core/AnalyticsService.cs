namespace LockPC.App.Core;

public sealed record AnalyticsSnapshot(
    int FocusMinutes,
    int FocusRounds,
    int FullRestCount,
    int RestAttemptCount,
    int Interruptions,
    int SleepNights,
    int SleepDelays,
    IReadOnlyList<DailyFocusPoint> DailyFocus,
    IReadOnlyList<InterruptionBucket> InterruptionBuckets)
{
    public int FullRestRate => RestAttemptCount == 0 ? 0 : (int)Math.Round(100d * FullRestCount / RestAttemptCount);
}

public sealed record DailyFocusPoint(string Day, int Minutes, double BarWidth);
public sealed record InterruptionBucket(string Label, int Count, double BarWidth);
public sealed record ActivityRow(string Time, string Type, string Detail);
public enum ActivityHistoryRange { Last7Days, Last15Days, Last30Days, All }
public sealed record ActivityHistoryPage(IReadOnlyList<ActivityRow> Rows, int TotalCount, int Page, int PageCount);

public sealed class AnalyticsService(StateStore store)
{
    public AnalyticsSnapshot BuildLastSevenDays()
    {
        var start = DateTime.Today.AddDays(-6);
        var events = store.LoadActivityEvents(new DateTimeOffset(start))
            .OrderBy(item => item.EventAt)
            .ToList();
        var legacyInterruptions = store.LoadPlanEvents()
            .Where(item => item.EventAt.LocalDateTime >= start)
            .Where(item => !events.Any(activity =>
                activity.EventType == (item.EventType == PlanEventType.PlanCancelled ? ActivityEventType.PlanCancelled : ActivityEventType.RestEndedEarly) &&
                activity.Reason == item.Reason &&
                Math.Abs((activity.EventAt - item.EventAt).TotalSeconds) < 5))
            .ToList();
        var focusEvents = events.Where(item => item.EventType == ActivityEventType.FocusCompleted).ToList();
        var fullRests = events.Count(item => item.EventType == ActivityEventType.RestCompleted);
        var earlyRests = events.Count(item => item.EventType == ActivityEventType.RestEndedEarly);
        var interruptions = events.Count(item => item.EventType is ActivityEventType.PlanCancelled or ActivityEventType.RestEndedEarly) + legacyInterruptions.Count;

        var dailyValues = Enumerable.Range(0, 7)
            .Select(offset => start.AddDays(offset))
            .Select(day => new
            {
                Day = day,
                Minutes = focusEvents.Where(item => item.EventAt.LocalDateTime.Date == day)
                    .Sum(item => item.DurationSeconds) / 60
            }).ToList();
        var maxDaily = Math.Max(1, dailyValues.Max(item => item.Minutes));
        var daily = dailyValues.Select(item => new DailyFocusPoint(
            item.Day.ToString("MM/dd"), item.Minutes, 210d * item.Minutes / maxDaily)).ToList();

        var bucketSpecs = new[] { ("凌晨 00–06", 0, 6), ("上午 06–12", 6, 12), ("下午 12–18", 12, 18), ("晚上 18–24", 18, 24) };
        var interruptionTimes = events.Where(item => item.EventType is ActivityEventType.PlanCancelled or ActivityEventType.RestEndedEarly).Select(item => item.EventAt.LocalDateTime)
            .Concat(legacyInterruptions.Select(item => item.EventAt.LocalDateTime)).ToList();
        var bucketValues = bucketSpecs.Select(spec => new
        {
            spec.Item1,
            Count = interruptionTimes.Count(time => time.Hour >= spec.Item2 && time.Hour < spec.Item3)
        }).ToList();
        var maxBucket = Math.Max(1, bucketValues.Max(item => item.Count));
        var buckets = bucketValues.Select(item => new InterruptionBucket(item.Item1, item.Count, 170d * item.Count / maxBucket)).ToList();

        return new AnalyticsSnapshot(
            focusEvents.Sum(item => item.DurationSeconds) / 60,
            focusEvents.Count,
            fullRests,
            fullRests + earlyRests,
            interruptions,
            events.Count(item => item.EventType == ActivityEventType.SleepCompleted),
            events.Count(item => item.EventType == ActivityEventType.SleepDelayed),
            daily, buckets);
    }

    public ActivityHistoryPage BuildActivityPage(ActivityHistoryRange range, int page, int pageSize)
    {
        var fromInclusive = range switch
        {
            ActivityHistoryRange.Last7Days => new DateTimeOffset(DateTime.Today.AddDays(-6)),
            ActivityHistoryRange.Last15Days => new DateTimeOffset(DateTime.Today.AddDays(-14)),
            ActivityHistoryRange.Last30Days => new DateTimeOffset(DateTime.Today.AddDays(-29)),
            _ => (DateTimeOffset?)null
        };
        var result = store.LoadActivityEventPage(fromInclusive, page, pageSize);
        var pageCount = Math.Max(1, (result.TotalCount + pageSize - 1) / pageSize);
        var normalizedPage = Math.Clamp(page, 1, pageCount);
        if (normalizedPage != page)
            result = store.LoadActivityEventPage(fromInclusive, normalizedPage, pageSize);
        var rows = result.Events.Select(item => new ActivityRow(
            item.EventAt.LocalDateTime.ToString("MM-dd HH:mm"), EventLabel(item.EventType), EventDetail(item))).ToList();
        return new ActivityHistoryPage(rows, result.TotalCount, normalizedPage, pageCount);
    }

    private static string EventLabel(ActivityEventType type) => type switch
    {
        ActivityEventType.PlanStarted => "开始专注计划",
        ActivityEventType.FocusCompleted => "完成专注",
        ActivityEventType.RestCompleted => "完整休息",
        ActivityEventType.PlanCompleted => "完成整组计划",
        ActivityEventType.PlanCancelled => "结束专注计划",
        ActivityEventType.RestEndedEarly => "提前撕贴",
        ActivityEventType.SleepStarted => "睡眠保护生效",
        ActivityEventType.SleepCompleted => "睡眠保护完成",
        ActivityEventType.SleepDelayed => "延迟睡眠保护",
        ActivityEventType.SleepDelayRejected => "拒绝重复延迟",
        _ => type.ToString()
    };

    private static string EventDetail(ActivityEventRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.Reason)) return item.Reason!;
        if (item.DelayMinutes > 0) return $"延迟 {item.DelayMinutes} 分钟";
        if (item.DurationSeconds > 0) return $"{TimeSpan.FromSeconds(item.DurationSeconds):hh\\:mm\\:ss}";
        return item.TotalRounds > 0 ? $"第 {item.CurrentRound}/{item.TotalRounds} 轮" : "—";
    }
}
