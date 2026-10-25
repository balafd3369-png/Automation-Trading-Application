using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class OperationsStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly SystemIdentityOptions _identity;
    private readonly ILogger<OperationsStore> _log;

    public OperationsStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ICurrentUserAccessor currentUser,
        SystemIdentityOptions identity,
        ILogger<OperationsStore> log)
    {
        _currentUser = currentUser;
        _identity = identity;
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
    }

    private string ActiveUserId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_currentUser.UserId))
            {
                return _currentUser.UserId;
            }

            if (_identity.AllowAnonymousLocalUser)
            {
                return TradeHistoryStore.LocalUserId;
            }

            throw new UnauthorizedAccessException("Authentication required.");
        }
    }

    public OperationsAuditEntry AppendAudit(string category, string eventType, string message)
    {
        var entry = new OperationsAuditEntry(
            Guid.NewGuid().ToString("N"),
            category,
            eventType,
            message,
            DateTimeOffset.UtcNow);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO operations_audit(id,user_id,category,event_type,message,created_at)
                VALUES($id,$user,$cat,$type,$msg,$at);
                """;
            Add(command, "$id", entry.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$cat", entry.Category);
            Add(command, "$type", entry.EventType);
            Add(command, "$msg", entry.Message);
            Add(command, "$at", Utc(entry.At));
            command.ExecuteNonQuery();
        }

        return entry;
    }

    public OperationsNotification AppendNotification(string severity, string eventType, string message)
    {
        var note = new OperationsNotification(
            Guid.NewGuid().ToString("N"),
            severity,
            eventType,
            message,
            false,
            DateTimeOffset.UtcNow);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO operations_notifications(id,user_id,severity,event_type,message,read_flag,created_at)
                VALUES($id,$user,$sev,$type,$msg,0,$at);
                """;
            Add(command, "$id", note.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$sev", note.Severity);
            Add(command, "$type", note.EventType);
            Add(command, "$msg", note.Message);
            Add(command, "$at", Utc(note.At));
            command.ExecuteNonQuery();
        }

        return note;
    }

    public IReadOnlyList<OperationsAuditEntry> ListAudit(int limit = 200, string? userId = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,category,event_type,message,created_at FROM operations_audit
                WHERE ($user='' OR user_id=$user)
                ORDER BY created_at DESC LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 500));
            Add(command, "$user", userId ?? ActiveUserId);
            return ReadAudit(command);
        }
    }

    public IReadOnlyList<OperationsNotification> ListNotifications(
        int limit = 100,
        bool unreadOnly = false,
        string? userId = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,severity,event_type,message,read_flag,created_at
                FROM operations_notifications
                WHERE ($user='' OR user_id=$user)
                  AND ($unread=0 OR read_flag=0)
                ORDER BY created_at DESC LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 300));
            Add(command, "$user", userId ?? ActiveUserId);
            Add(command, "$unread", unreadOnly ? 1 : 0);
            var list = new List<OperationsNotification>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4) == 1,
                    DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
            }

            return list;
        }
    }

    public void MarkNotificationRead(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE operations_notifications SET read_flag=1 WHERE id=$id AND user_id=$user;";
            Add(command, "$id", id);
            Add(command, "$user", ActiveUserId);
            command.ExecuteNonQuery();
        }
    }

    public void SaveJournalNotes(string tradeId, string notes)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO trade_journal_notes(user_id,trade_id,notes,updated_at)
                VALUES($user,$id,$notes,$at)
                ON CONFLICT(user_id,trade_id) DO UPDATE SET notes=excluded.notes,updated_at=excluded.updated_at;
                """;
            Add(command, "$user", ActiveUserId);
            Add(command, "$id", tradeId);
            Add(command, "$notes", notes);
            Add(command, "$at", Utc(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public string? GetJournalNotes(string tradeId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT notes FROM trade_journal_notes WHERE user_id=$user AND trade_id=$id LIMIT 1;";
            Add(command, "$user", ActiveUserId);
            Add(command, "$id", tradeId);
            return command.ExecuteScalar() as string;
        }
    }

    public void AppendReconciliationHistory(BrokerReconciliationSnapshot snapshot)
    {
        var id = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(snapshot.Mismatches);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO broker_reconciliation_history(
                    id,result,issue_count,position_matches,position_mismatches,order_matches,order_mismatches,mismatches_json,created_at)
                VALUES($id,$result,$issues,$pm,$pmm,$om,$omm,$json,$at);
                DELETE FROM broker_reconciliation_history
                WHERE id NOT IN (
                    SELECT id FROM broker_reconciliation_history ORDER BY created_at DESC LIMIT 300
                );
                """;
            Add(command, "$id", id);
            Add(command, "$result", snapshot.Health.ToString());
            Add(command, "$issues", snapshot.IssueCount);
            Add(command, "$pm", snapshot.PositionMatches);
            Add(command, "$pmm", snapshot.PositionMismatches);
            Add(command, "$om", snapshot.OrderMatches);
            Add(command, "$omm", snapshot.OrderMismatches);
            Add(command, "$json", payload);
            Add(command, "$at", Utc(snapshot.LastRunAt ?? DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<BrokerReconciliationHistoryEntry> ListReconciliationHistory(int limit = 50)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,result,issue_count,position_matches,position_mismatches,order_matches,order_mismatches,mismatches_json,created_at
                FROM broker_reconciliation_history
                ORDER BY created_at DESC
                LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 200));
            var list = new List<BrokerReconciliationHistoryEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var mismatches = JsonSerializer.Deserialize<List<BrokerReconciliationMismatch>>(reader.GetString(7)) ?? [];
                var result = Enum.TryParse<BrokerReconciliationHealth>(reader.GetString(1), out var health)
                    ? health
                    : BrokerReconciliationHealth.Skipped;
                list.Add(new(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                    result,
                    ReconciliationResultLabel(result),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    mismatches));
            }

            return list;
        }
    }

    private static string ReconciliationResultLabel(BrokerReconciliationHealth health) => health switch
    {
        BrokerReconciliationHealth.Healthy => "Healthy",
        BrokerReconciliationHealth.IssuesFound => "Issues Found",
        BrokerReconciliationHealth.BrokerError => "Broker Error",
        _ => "Skipped",
    };

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS operations_audit (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL DEFAULT 'local-user',
                    category TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_operations_audit_created ON operations_audit(created_at DESC);
                CREATE TABLE IF NOT EXISTS operations_notifications (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL DEFAULT 'local-user',
                    severity TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    read_flag INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_operations_notifications_created ON operations_notifications(created_at DESC);
                CREATE TABLE IF NOT EXISTS trade_journal_notes (
                    user_id TEXT NOT NULL DEFAULT 'local-user',
                    trade_id TEXT NOT NULL,
                    notes TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (user_id, trade_id)
                );
                CREATE TABLE IF NOT EXISTS broker_reconciliation_history (
                    id TEXT PRIMARY KEY,
                    result TEXT NOT NULL,
                    issue_count INTEGER NOT NULL,
                    position_matches INTEGER NOT NULL,
                    position_mismatches INTEGER NOT NULL,
                    order_matches INTEGER NOT NULL,
                    order_mismatches INTEGER NOT NULL,
                    mismatches_json TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_broker_reconciliation_created ON broker_reconciliation_history(created_at DESC);
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "operations_audit", "user_id", "TEXT NOT NULL DEFAULT 'local-user'");
            EnsureColumn(connection, "operations_notifications", "user_id", "TEXT NOT NULL DEFAULT 'local-user'");
            MigrateJournalNotesSchema(connection);
        }
    }

    private static void MigrateJournalNotesSchema(SqliteConnection connection)
    {
        using var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(trade_journal_notes);";
        var columns = new List<string>();
        using (var reader = info.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (columns.Count == 0)
        {
            return;
        }

        if (columns.Contains("user_id", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        using var migrate = connection.CreateCommand();
        migrate.CommandText =
            """
            CREATE TABLE IF NOT EXISTS trade_journal_notes_v2 (
                user_id TEXT NOT NULL,
                trade_id TEXT NOT NULL,
                notes TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (user_id, trade_id)
            );
            INSERT INTO trade_journal_notes_v2(user_id,trade_id,notes,updated_at)
            SELECT 'local-user', trade_id, notes, updated_at FROM trade_journal_notes;
            DROP TABLE trade_journal_notes;
            ALTER TABLE trade_journal_notes_v2 RENAME TO trade_journal_notes;
            """;
        migrate.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static List<OperationsAuditEntry> ReadAudit(SqliteCommand command)
    {
        var list = new List<OperationsAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
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

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
