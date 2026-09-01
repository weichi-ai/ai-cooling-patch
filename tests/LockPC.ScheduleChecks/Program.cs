using LockPC.App.Core;

var failures = new List<string>();
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

Console.WriteLine("All 7 sleep schedule scenarios passed.");
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
