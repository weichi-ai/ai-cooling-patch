using System.Text.Json;
using System.IO;

namespace LockPC.App.Core;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _runtimePath;
    private readonly string _planEventsPath;
    private readonly ActivityEventStore _activityEvents;

    public StateStore()
    {
        _dataDirectory = Environment.GetEnvironmentVariable("LOCKPC_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockPC");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _runtimePath = Path.Combine(_dataDirectory, "runtime.json");
        _planEventsPath = Path.Combine(_dataDirectory, "plan-events.json");
        Directory.CreateDirectory(_dataDirectory);
        _activityEvents = new ActivityEventStore(_dataDirectory);
    }

    public AppSettings LoadSettings() => Load(_settingsPath, new AppSettings());

    public RuntimeState LoadRuntime() => Load(_runtimePath, new RuntimeState());

    public void SaveSettings(AppSettings settings) => SaveAtomic(_settingsPath, settings);

    public void SaveRuntime(RuntimeState state) => SaveAtomic(_runtimePath, state);

    public IReadOnlyList<PlanEventRecord> LoadPlanEvents() =>
        Load(_planEventsPath, new List<PlanEventRecord>());

    public void AppendPlanEvent(PlanEventRecord record)
    {
        var records = LoadPlanEvents().ToList();
        records.Add(record);
        SaveAtomic(_planEventsPath, records);
    }

    public IReadOnlyList<ActivityEventRecord> LoadActivityEvents() => _activityEvents.LoadAll();

    public IReadOnlyList<ActivityEventRecord> LoadActivityEvents(DateTimeOffset fromInclusive) =>
        _activityEvents.LoadRange(fromInclusive);

    public ActivityEventPage LoadActivityEventPage(DateTimeOffset? fromInclusive, int page, int pageSize) =>
        _activityEvents.LoadPage(fromInclusive, page, pageSize);

    public void AppendActivityEvent(ActivityEventRecord record)
    {
        _activityEvents.Append(record);
    }

    private static T Load<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path))
                return fallback;

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void SaveAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
