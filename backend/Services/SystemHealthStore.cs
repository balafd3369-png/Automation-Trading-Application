using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemHealthStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ILogger<SystemHealthStore> _log;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SystemHealthStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ILogger<SystemHealthStore> log)
    {
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
    }

    public string? LastComprehensiveReportId { get; private set; }

    public DateTimeOffset? LastComprehensiveReportAt { get; private set; }

    public void SaveComprehensiveReport(SystemHealthComprehensiveReport report)
    {
        var id = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(report, JsonOptions);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_health_reports(id,report_kind,overall_status,summary_json,created_at)
                VALUES($id,'comprehensive',$status,$json,$at);
                """;
            Add(command, "$id", id);
            Add(command, "$status", report.OverallStatus.ToString());
            Add(command, "$json", payload);
            Add(command, "$at", Utc(report.GeneratedAt));
            command.ExecuteNonQuery();
            LastComprehensiveReportId = id;
            LastComprehensiveReportAt = report.GeneratedAt;
        }
    }

    public IReadOnlyList<SystemHealthReportEntry> ListReports(string? kind, int limit)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            if (string.IsNullOrWhiteSpace(kind))
            {
                command.CommandText =
                    """
                    SELECT id,report_kind,overall_status,summary_json,created_at
                    FROM system_health_reports
                    ORDER BY created_at DESC
                    LIMIT $limit;
                    """;
            }
            else
            {
                command.CommandText =
                    """
                    SELECT id,report_kind,overall_status,summary_json,created_at
                    FROM system_health_reports
                    WHERE report_kind = $kind
                    ORDER BY created_at DESC
                    LIMIT $limit;
                    """;
                Add(command, "$kind", kind.Trim());
            }

            Add(command, "$limit", limit);
            return ReadReports(command);
        }
    }

    public SystemHealthReportEntry? GetReport(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,report_kind,overall_status,summary_json,created_at
                FROM system_health_reports
                WHERE id = $id
                LIMIT 1;
                """;
            Add(command, "$id", id.Trim());
            return ReadReports(command).FirstOrDefault();
        }
    }

    internal DatabaseHealthSnapshot ProbeDatabase()
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            lock (_gate)
            {
                using var connection = Open();
                using (var read = connection.CreateCommand())
                {
                    read.CommandText = "SELECT 1;";
                    _ = read.ExecuteScalar();
                }

                using (var write = connection.CreateCommand())
                {
                    write.CommandText = "INSERT INTO system_health_probes(probe_at) VALUES($at);";
                    Add(write, "$at", Utc(started));
                    write.ExecuteNonQuery();
                }

                using (var prune = connection.CreateCommand())
                {
                    prune.CommandText =
                        """
                        DELETE FROM system_health_probes
                        WHERE id NOT IN (
                            SELECT id FROM system_health_probes ORDER BY id DESC LIMIT 50
                        );
                        """;
                    prune.ExecuteNonQuery();
                }
            }

            var latency = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
            return new DatabaseHealthSnapshot(true, true, true, null, DateTimeOffset.UtcNow, latency);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Database health probe failed.");
            var latency = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
            return new DatabaseHealthSnapshot(false, false, false, ex.Message, DateTimeOffset.UtcNow, latency);
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS system_health_reports (
                    id TEXT PRIMARY KEY,
                    report_kind TEXT NOT NULL,
                    overall_status TEXT NOT NULL,
                    summary_json TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_system_health_reports_created
                    ON system_health_reports(created_at DESC);
                CREATE TABLE IF NOT EXISTS system_health_probes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    probe_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private static List<SystemHealthReportEntry> ReadReports(SqliteCommand command)
    {
        var list = new List<SystemHealthReportEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var payload = JsonSerializer.Deserialize<SystemHealthComprehensiveReport>(
                reader.GetString(3),
                JsonOptions);
            if (payload is null)
            {
                continue;
            }

            list.Add(new SystemHealthReportEntry(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<SystemHealthOverallStatus>(reader.GetString(2), ignoreCase: true),
                payload,
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        }

        return list;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}