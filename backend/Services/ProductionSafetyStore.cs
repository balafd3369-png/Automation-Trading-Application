using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ProductionSafetyStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ILogger<ProductionSafetyStore> _log;

    public ProductionSafetyStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ILogger<ProductionSafetyStore> log)
    {
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
    }

    public void AppendBackup(DatabaseBackupRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_database_backups(
                    id,source_path,backup_path,status,file_size_bytes,failure_reason,integrity_verified,correlation_id,started_utc,completed_utc)
                VALUES($id,$src,$path,$status,$size,$fail,$verified,$corr,$start,$done);
                """;
            BindBackup(command, record);
            command.ExecuteNonQuery();
        }
    }

    public void UpdateBackup(DatabaseBackupRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE system_database_backups
                SET status=$status,file_size_bytes=$size,failure_reason=$fail,integrity_verified=$verified,completed_utc=$done
                WHERE id=$id;
                """;
            BindBackup(command, record);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<DatabaseBackupRecord> ListBackups(int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_database_backups ORDER BY started_utc DESC LIMIT $limit;
                """;
            Add(command, "$limit", limit);
            return ReadBackups(command);
        }
    }

    public DatabaseBackupRecord? GetBackup(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_database_backups WHERE id=$id LIMIT 1;";
            Add(command, "$id", id);
            return ReadBackups(command).FirstOrDefault();
        }
    }

    public void SaveStartupAudit(OrderStartupAuditSummary summary)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_order_startup_audits(id,finding_count,reconciled_count,uncertain_count,correlation_id,completed_utc,payload_json)
                VALUES($id,$fc,$rc,$uc,$corr,$done,$json);
                """;
            Add(command, "$id", summary.Id);
            Add(command, "$fc", summary.FindingCount);
            Add(command, "$rc", summary.ReconciledCount);
            Add(command, "$uc", summary.StillUncertainCount);
            Add(command, "$corr", summary.CorrelationId);
            Add(command, "$done", Utc(summary.CompletedUtc));
            Add(command, "$json", SystemIncidentNotificationStore.SanitizeMetadata(summary.Findings));
            command.ExecuteNonQuery();
        }
    }

    public OrderStartupAuditSummary? LatestStartupAudit()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_order_startup_audits ORDER BY completed_utc DESC LIMIT 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var findingsJson = reader.GetString(reader.GetOrdinal("payload_json"));
            IReadOnlyList<OrderSafetyAuditFinding> findings = string.IsNullOrWhiteSpace(findingsJson)
                ? []
                : System.Text.Json.JsonSerializer.Deserialize<List<OrderSafetyAuditFinding>>(findingsJson) ?? [];
            return new OrderStartupAuditSummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                ParseUtc(reader.GetString(5)),
                findings);
        }
    }

    public DatabaseBackupRecord? LastBackup()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_database_backups ORDER BY started_utc DESC LIMIT 1;";
            return ReadBackups(command).FirstOrDefault();
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
                CREATE TABLE IF NOT EXISTS system_database_backups (
                    id TEXT PRIMARY KEY,
                    source_path TEXT NOT NULL,
                    backup_path TEXT NOT NULL,
                    status TEXT NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    failure_reason TEXT NULL,
                    integrity_verified INTEGER NOT NULL,
                    correlation_id TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    completed_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_db_backups_started ON system_database_backups(started_utc DESC);
                CREATE TABLE IF NOT EXISTS system_order_startup_audits (
                    id TEXT PRIMARY KEY,
                    finding_count INTEGER NOT NULL,
                    reconciled_count INTEGER NOT NULL,
                    uncertain_count INTEGER NOT NULL,
                    correlation_id TEXT NOT NULL,
                    completed_utc TEXT NOT NULL,
                    payload_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_order_startup_completed ON system_order_startup_audits(completed_utc DESC);
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void BindBackup(SqliteCommand command, DatabaseBackupRecord record)
    {
        Add(command, "$id", record.Id);
        Add(command, "$src", record.SourceDatabasePath);
        Add(command, "$path", record.BackupFilePath);
        Add(command, "$status", record.Status.ToString());
        Add(command, "$size", record.FileSizeBytes);
        Add(command, "$fail", record.FailureReason);
        Add(command, "$verified", record.IntegrityVerified ? 1 : 0);
        Add(command, "$corr", record.CorrelationId);
        Add(command, "$start", Utc(record.StartedUtc));
        Add(command, "$done", record.CompletedUtc is null ? null : Utc(record.CompletedUtc.Value));
    }

    private static List<DatabaseBackupRecord> ReadBackups(SqliteCommand command)
    {
        var list = new List<DatabaseBackupRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DatabaseBackupRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<DatabaseBackupResultStatus>(reader.GetString(3)),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) == 1,
                reader.GetString(7),
                ParseUtc(reader.GetString(8)),
                reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9))));
        }

        return list;
    }

    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}