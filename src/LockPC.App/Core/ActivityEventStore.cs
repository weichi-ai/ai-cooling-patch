using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LockPC.App.Core;

public sealed record ActivityEventPage(IReadOnlyList<ActivityEventRecord> Events, int TotalCount);

public sealed class ActivityEventStore
{
    private const string LegacyMigrationKey = "activity_events_json_migrated_v1_1_3";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _databasePath;
    private readonly string _legacyJsonPath;
    private readonly string _legacyBackupPath;

    public ActivityEventStore(string dataDirectory)
    {
        _databasePath = Path.Combine(dataDirectory, "activity-events.db");
        _legacyJsonPath = Path.Combine(dataDirectory, "activity-events.json");
        _legacyBackupPath = Path.Combine(dataDirectory, "activity-events.v1.1.2.json.bak");
        Initialize();
        MigrateLegacyJson();
    }

    public void Append(ActivityEventRecord record)
    {
        using var connection = OpenConnection();
        using var command = CreateInsertCommand(connection, null, record);
        command.CommandText = command.CommandText.Replace("INSERT OR IGNORE", "INSERT", StringComparison.Ordinal);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ActivityEventRecord> LoadAll() => LoadRange(null);

    public IReadOnlyList<ActivityEventRecord> LoadRange(DateTimeOffset? fromInclusive)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = fromInclusive is null
            ? "SELECT * FROM activity_events ORDER BY event_at_unix_ms, id;"
            : "SELECT * FROM activity_events WHERE event_at_unix_ms >= $from ORDER BY event_at_unix_ms, id;";
        if (fromInclusive is not null)
            command.Parameters.AddWithValue("$from", fromInclusive.Value.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        return ReadRecords(reader);
    }

    public ActivityEventPage LoadPage(DateTimeOffset? fromInclusive, int page, int pageSize)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));

        using var connection = OpenConnection();
        var whereClause = fromInclusive is null ? string.Empty : " WHERE event_at_unix_ms >= $from";
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM activity_events{whereClause};";
        if (fromInclusive is not null)
            countCommand.Parameters.AddWithValue("$from", fromInclusive.Value.ToUnixTimeMilliseconds());
        var totalCount = Convert.ToInt32((long)(countCommand.ExecuteScalar() ?? 0L), CultureInfo.InvariantCulture);

        using var pageCommand = connection.CreateCommand();
        pageCommand.CommandText = $"SELECT * FROM activity_events{whereClause} ORDER BY event_at_unix_ms DESC, id DESC LIMIT $limit OFFSET $offset;";
        if (fromInclusive is not null)
            pageCommand.Parameters.AddWithValue("$from", fromInclusive.Value.ToUnixTimeMilliseconds());
        pageCommand.Parameters.AddWithValue("$limit", pageSize);
        pageCommand.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        using var reader = pageCommand.ExecuteReader();
        return new ActivityEventPage(ReadRecords(reader), totalCount);
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS activity_events (
                id TEXT NOT NULL PRIMARY KEY,
                plan_id TEXT NULL,
                event_type INTEGER NOT NULL,
                event_at TEXT NOT NULL,
                event_at_unix_ms INTEGER NOT NULL,
                current_round INTEGER NOT NULL,
                total_rounds INTEGER NOT NULL,
                duration_seconds INTEGER NOT NULL,
                remaining_seconds INTEGER NOT NULL,
                delay_minutes INTEGER NOT NULL,
                reason TEXT NULL,
                sleep_occurrence_date TEXT NULL,
                sleep_delay_source INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_activity_events_time
                ON activity_events(event_at_unix_ms DESC, id DESC);
            CREATE TABLE IF NOT EXISTS app_metadata (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            );
            PRAGMA user_version = 1;
            """;
        command.ExecuteNonQuery();
    }

    private void MigrateLegacyJson()
    {
        using var connection = OpenConnection();
        if (HasMetadata(connection, LegacyMigrationKey)) return;

        if (!File.Exists(_legacyJsonPath))
        {
            SetMetadata(connection, null, LegacyMigrationKey, DateTimeOffset.UtcNow.ToString("O"));
            return;
        }

        List<ActivityEventRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<List<ActivityEventRecord>>(
                File.ReadAllText(_legacyJsonPath), JsonOptions) ?? [];
        }
        catch
        {
            return;
        }

        if (!File.Exists(_legacyBackupPath))
            File.Copy(_legacyJsonPath, _legacyBackupPath);

        using var transaction = connection.BeginTransaction();
        foreach (var record in records)
        {
            using var command = CreateInsertCommand(connection, transaction, record);
            command.ExecuteNonQuery();
        }
        SetMetadata(connection, transaction, LegacyMigrationKey, DateTimeOffset.UtcNow.ToString("O"));
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand CreateInsertCommand(SqliteConnection connection,
        SqliteTransaction? transaction, ActivityEventRecord record)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO activity_events (
                id, plan_id, event_type, event_at, event_at_unix_ms, current_round, total_rounds,
                duration_seconds, remaining_seconds, delay_minutes, reason, sleep_occurrence_date,
                sleep_delay_source)
            VALUES (
                $id, $planId, $eventType, $eventAt, $eventAtUnixMs, $currentRound, $totalRounds,
                $durationSeconds, $remainingSeconds, $delayMinutes, $reason, $sleepOccurrenceDate,
                $sleepDelaySource);
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$planId", (object?)record.PlanId?.ToString("D") ?? DBNull.Value);
        command.Parameters.AddWithValue("$eventType", (int)record.EventType);
        command.Parameters.AddWithValue("$eventAt", record.EventAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$eventAtUnixMs", record.EventAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$currentRound", record.CurrentRound);
        command.Parameters.AddWithValue("$totalRounds", record.TotalRounds);
        command.Parameters.AddWithValue("$durationSeconds", record.DurationSeconds);
        command.Parameters.AddWithValue("$remainingSeconds", record.RemainingSeconds);
        command.Parameters.AddWithValue("$delayMinutes", record.DelayMinutes);
        command.Parameters.AddWithValue("$reason", (object?)record.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$sleepOccurrenceDate",
            record.SleepOccurrenceDate is null ? DBNull.Value : record.SleepOccurrenceDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sleepDelaySource",
            record.SleepDelaySource is null ? DBNull.Value : (int)record.SleepDelaySource.Value);
        return command;
    }

    private static List<ActivityEventRecord> ReadRecords(SqliteDataReader reader)
    {
        var records = new List<ActivityEventRecord>();
        while (reader.Read())
        {
            records.Add(new ActivityEventRecord(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                reader.IsDBNull(reader.GetOrdinal("plan_id")) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("plan_id"))),
                (ActivityEventType)reader.GetInt32(reader.GetOrdinal("event_type")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("event_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt32(reader.GetOrdinal("current_round")),
                reader.GetInt32(reader.GetOrdinal("total_rounds")),
                reader.GetInt32(reader.GetOrdinal("duration_seconds")),
                reader.GetInt32(reader.GetOrdinal("remaining_seconds")),
                reader.GetInt32(reader.GetOrdinal("delay_minutes")),
                reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                reader.IsDBNull(reader.GetOrdinal("sleep_occurrence_date")) ? null : DateOnly.ParseExact(
                    reader.GetString(reader.GetOrdinal("sleep_occurrence_date")), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(reader.GetOrdinal("sleep_delay_source")) ? null :
                    (SleepDelaySource)reader.GetInt32(reader.GetOrdinal("sleep_delay_source"))));
        }
        return records;
    }

    private static bool HasMetadata(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM app_metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is not null;
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction? transaction,
        string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO app_metadata(key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
