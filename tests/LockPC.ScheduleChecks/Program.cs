using LockPC.App.Core;
using LockPC.App.Services;
using System.Text.Json;

var failures = new List<string>();
Run("v1.1.3 会把 JSON 活动记录幂等迁移到 SQLite", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-SqliteMigration-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(testDirectory);
        var legacyEvents = new[]
        {
            DelayEvent(new DateTimeOffset(2026, 8, 31, 23, 29, 40, TimeSpan.FromHours(8)), 30),
            new ActivityEventRecord(Guid.NewGuid(), Guid.NewGuid(), ActivityEventType.PlanStarted,
                new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(8)), CurrentRound: 1, TotalRounds: 4)
        };
        File.WriteAllText(Path.Combine(testDirectory, "activity-events.json"), JsonSerializer.Serialize(legacyEvents));
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);

        var firstStore = new StateStore();
        Equal(2, firstStore.LoadActivityEvents().Count);
        True(File.Exists(Path.Combine(testDirectory, "activity-events.db")));
        True(File.Exists(Path.Combine(testDirectory, "activity-events.v1.1.2.json.bak")));

        var secondStore = new StateStore();
        Equal(2, secondStore.LoadActivityEvents().Count);
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

Run("SQLite 历史记录支持全量 50 行分页与日期范围", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-SqlitePaging-{Guid.NewGuid():N}");
    var baseTime = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    try
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);
        var store = new StateStore();
        for (var index = 0; index < 125; index++)
            store.AppendActivityEvent(new ActivityEventRecord(Guid.NewGuid(), null,
                ActivityEventType.PlanStarted, baseTime.AddMinutes(-index)));

        var firstPage = store.LoadActivityEventPage(null, 1, 50);
        var thirdPage = store.LoadActivityEventPage(null, 3, 50);
        var recentPage = store.LoadActivityEventPage(baseTime.AddMinutes(-9), 1, 50);
        Equal(125, firstPage.TotalCount);
        Equal(50, firstPage.Events.Count);
        Equal(25, thirdPage.Events.Count);
        Equal(10, recentPage.TotalCount);
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

Run("数据分析记录延迟分钟与按时睡眠", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-SleepAnalytics-{Guid.NewGuid():N}");
    try
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);
        var store = new StateStore();
        store.AppendActivityEvent(new ActivityEventRecord(Guid.NewGuid(), null,
            ActivityEventType.SleepDelayed, DateTimeOffset.Now.AddHours(-8), DelayMinutes: 30));
        store.AppendActivityEvent(new ActivityEventRecord(Guid.NewGuid(), null,
            ActivityEventType.SleepCompleted, DateTimeOffset.Now, DurationSeconds: 8 * 60 * 60,
            DelayMinutes: 30));

        var snapshot = new AnalyticsService(store).BuildLastSevenDays();
        var history = new AnalyticsService(store).BuildActivityPage(ActivityHistoryRange.All, 1, 50);
        Equal(1, snapshot.SleepDelays);
        Equal("延迟 30 分钟后睡眠", history.Rows[0].Detail);
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

Run("托盘空闲状态使用两行提示", () =>
{
    var snapshot = new RuntimeSnapshot(LockPhase.Idle, null, TimeSpan.Zero, 0, 0, false,
        "当前没有专注计划", 0);
    Equal("AI退烧贴\n当前没有专注计划", TrayService.BuildTooltip(snapshot));
});

Run("托盘专注状态显示轮次与倒计时", () =>
{
    var snapshot = new RuntimeSnapshot(LockPhase.Focus, null, new TimeSpan(0, 23, 41), 2, 4, true,
        "专注模式 · 正在生效", 0.5);
    Equal("专注中 · 第 2/4 轮\n23:41 后进入离屏休息", TrayService.BuildTooltip(snapshot));
});

Run("跨零点延迟保持原计划日期", () =>
{
    var settings = TestSettings();
    var runtime = new RuntimeState
    {
        SchemaVersion = 2,
        DelayedSleepOccurrenceDate = new DateOnly(2026, 8, 31),
        DelayedSleepMinutes = 30
    };
    var occurrence = SleepSchedule.Create(settings, runtime, new DateTime(2026, 8, 31))!.Value;
    Equal(new DateOnly(2026, 8, 31), occurrence.OccurrenceDate);
    Equal(new DateTime(2026, 8, 31, 23, 30, 0), occurrence.ScheduledStart);
    Equal(new DateTime(2026, 9, 1, 0, 0, 0), occurrence.Start);
    Equal(new DateTime(2026, 9, 1, 6, 0, 0), occurrence.End);
});

Run("前一晚延迟不会污染次日晚", () =>
{
    var settings = TestSettings();
    var runtime = new RuntimeState
    {
        SchemaVersion = 2,
        DelayedSleepOccurrenceDate = new DateOnly(2026, 8, 31),
        DelayedSleepMinutes = 30
    };
    var occurrence = SleepSchedule.Create(settings, runtime, new DateTime(2026, 9, 1))!.Value;
    Equal(new DateOnly(2026, 9, 1), occurrence.OccurrenceDate);
    Equal(new DateTime(2026, 9, 1, 23, 30, 0), occurrence.Start);
});

Run("合法旧版延迟证据可以迁移", () =>
{
    var settings = TestSettings();
    var runtime = new RuntimeState
    {
        DelayedSleepOccurrenceDate = new DateOnly(2026, 8, 31),
        DelayedSleepMinutes = 30
    };
    var events = new[]
    {
        DelayEvent(new DateTimeOffset(2026, 8, 31, 23, 29, 40, TimeSpan.FromHours(8)), 30)
    };
    True(SleepSchedule.HasValidLegacyDelayEvidence(runtime, settings, events));
});

Run("跨日串期的旧版延迟证据会被拒绝", () =>
{
    var settings = TestSettings();
    var runtime = new RuntimeState
    {
        DelayedSleepOccurrenceDate = new DateOnly(2026, 9, 1),
        DelayedSleepMinutes = 30
    };
    var events = new[]
    {
        DelayEvent(new DateTimeOffset(2026, 8, 31, 23, 59, 36, TimeSpan.FromHours(8)), 30)
    };
    True(!SleepSchedule.HasValidLegacyDelayEvidence(runtime, settings, events));
});

Run("跨零点后的第二次延迟会被引擎拒绝", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-ScheduleChecks-{Guid.NewGuid():N}");
    var localNow = new DateTime(2026, 8, 31, 23, 29, 40);
    try
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);
        var store = new StateStore();
        store.SaveSettings(TestSettings());
        store.SaveRuntime(new RuntimeState { SchemaVersion = 2 });
        using var engine = new ScheduleEngine(store, () => localNow,
            () => new DateTimeOffset(localNow, TimeSpan.FromHours(8)).ToUniversalTime());
        var occurrenceDate = new DateOnly(2026, 8, 31);

        True(engine.DelayCurrentSleepOccurrence(
            occurrenceDate, 30, SleepDelaySource.WarningWindow));
        localNow = new DateTime(2026, 8, 31, 23, 59, 40);
        True(!engine.DelayCurrentSleepOccurrence(
            occurrenceDate, 30, SleepDelaySource.TransitionWindow));

        var runtime = store.LoadRuntime();
        Equal<DateOnly?>(occurrenceDate, runtime.DelayedSleepOccurrenceDate);
        Equal(30, runtime.DelayedSleepMinutes);
        var events = store.LoadActivityEvents();
        Equal(1, events.Count(item => item.EventType == ActivityEventType.SleepDelayed));
        Equal(1, events.Count(item => item.EventType == ActivityEventType.SleepDelayRejected));
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

Run("引擎启动会清理 v1.1.1 异常残留", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-ScheduleChecks-{Guid.NewGuid():N}");
    try
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);
        var store = new StateStore();
        var settings = TestSettings();
        store.SaveSettings(settings);
        store.SaveRuntime(new RuntimeState
        {
            SchemaVersion = 0,
            DelayedSleepOccurrenceDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            DelayedSleepMinutes = 30,
            LastSleepWarningDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        });
        store.AppendActivityEvent(DelayEvent(
            new DateTimeOffset(DateTime.Today.AddHours(23).AddMinutes(59)), 30));

        using var engine = new ScheduleEngine(store);
        var migrated = store.LoadRuntime();
        Equal(2, migrated.SchemaVersion);
        Equal<DateOnly?>(null, migrated.DelayedSleepOccurrenceDate);
        Equal(0, migrated.DelayedSleepMinutes);
        Equal<DateOnly?>(null, migrated.LastSleepWarningDate);
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

Run("引擎迁移会保留仍有效的合法延迟", () =>
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"LockPC-ScheduleChecks-{Guid.NewGuid():N}");
    var localNow = new DateTime(2026, 8, 31, 23, 40, 0);
    try
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", testDirectory);
        var store = new StateStore();
        store.SaveSettings(TestSettings());
        var occurrenceDate = new DateOnly(2026, 8, 31);
        store.SaveRuntime(new RuntimeState
        {
            SchemaVersion = 0,
            DelayedSleepOccurrenceDate = occurrenceDate,
            DelayedSleepMinutes = 30,
            LastSleepWarningDate = occurrenceDate
        });
        store.AppendActivityEvent(DelayEvent(
            new DateTimeOffset(2026, 8, 31, 23, 29, 40, TimeSpan.FromHours(8)), 30));

        using var engine = new ScheduleEngine(store, () => localNow,
            () => new DateTimeOffset(localNow, TimeSpan.FromHours(8)).ToUniversalTime());
        var migrated = store.LoadRuntime();
        Equal(2, migrated.SchemaVersion);
        Equal<DateOnly?>(occurrenceDate, migrated.DelayedSleepOccurrenceDate);
        Equal(30, migrated.DelayedSleepMinutes);
    }
    finally
    {
        Environment.SetEnvironmentVariable("LOCKPC_DATA_DIR", null);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
    }
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} scenario(s) failed:");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("All 12 schedule, tray, and SQLite scenarios passed.");
return 0;

void Run(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
    }
}

static AppSettings TestSettings() => new()
{
    SleepProtectionEnabled = true,
    SleepStart = new TimeSpan(23, 30, 0),
    SleepEnd = new TimeSpan(6, 0, 0),
    SleepDays = Enum.GetValues<DayOfWeek>().ToHashSet(),
    SleepWarningSeconds = 30
};

static ActivityEventRecord DelayEvent(DateTimeOffset at, int minutes) =>
    new(Guid.NewGuid(), null, ActivityEventType.SleepDelayed, at, DelayMinutes: minutes);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("condition was false");
}
