using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;

namespace TradeWithBeast.Services;

public sealed class TradingSafetyStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public TradingSafetyStore(IConfiguration configuration, DatabaseOptions database)
    {
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        Initialize();
    }

    public void SaveEmergencyGateState(bool halted, string reason, string changedBy)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_emergency_gate_state(id,is_halted,reason,changed_utc,changed_by)
                VALUES(1,$halt,$reason,$utc,$by)
                ON CONFLICT(id) DO UPDATE SET
                    is_halted=excluded.is_halted,
                    reason=excluded.reason,
                    changed_utc=excluded.changed_utc,
                    changed_by=excluded.changed_by;
                """;
            Add(command, "$halt", halted ? 1 : 0);
            Add(command, "$reason", reason ?? "");
            Add(command, "$utc", Utc(DateTimeOffset.UtcNow));
            Add(command, "$by", changedBy);
            command.ExecuteNonQuery();
        }
    }

    public EmergencyGatePersistedState? GetEmergencyGateState()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT is_halted,reason,changed_utc,changed_by FROM system_emergency_gate_state WHERE id=1 LIMIT 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new EmergencyGatePersistedState(
                reader.GetInt32(0) == 1,
                reader.GetString(1),
                ParseUtc(reader.GetString(2)),
                reader.GetString(3));
        }
    }

    public void AppendGateAudit(string action, string detail, string actor)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_emergency_gate_audit(id,action,detail,actor,created_utc)
                VALUES($id,$action,$detail,$actor,$utc);
                """;
            Add(command, "$id", Guid.NewGuid().ToString("N"));
            Add(command, "$action", action);
            Add(command, "$detail", detail);
            Add(command, "$actor", actor);
            Add(command, "$utc", Utc(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<EmergencyGateAuditEntry> ListGateAudit(int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT action,detail,actor,created_utc FROM system_emergency_gate_audit
                ORDER BY created_utc DESC LIMIT $limit;
                """;
            Add(command, "$limit", limit);
            var list = new List<EmergencyGateAuditEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EmergencyGateAuditEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    ParseUtc(reader.GetString(3))));
            }

            return list;
        }
    }

    public void RecordPoll(string orderKind, string orderId, bool success, string? error)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_order_status_polls(id,order_kind,order_id,success,error,polled_utc)
                VALUES($id,$kind,$oid,$ok,$err,$utc);
                """;
            Add(command, "$id", Guid.NewGuid().ToString("N"));
            Add(command, "$kind", orderKind);
            Add(command, "$oid", orderId);
            Add(command, "$ok", success ? 1 : 0);
            Add(command, "$err", error);
            Add(command, "$utc", Utc(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
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
                CREATE TABLE IF NOT EXISTS system_emergency_gate_state (
                    id INTEGER PRIMARY KEY,
                    is_halted INTEGER NOT NULL,
                    reason TEXT NOT NULL,
                    changed_utc TEXT NOT NULL,
                    changed_by TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS system_emergency_gate_audit (
                    id TEXT PRIMARY KEY,
                    action TEXT NOT NULL,
                    detail TEXT NOT NULL,
                    actor TEXT NOT NULL,
                    created_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_gate_audit_created ON system_emergency_gate_audit(created_utc DESC);
                CREATE TABLE IF NOT EXISTS system_order_status_polls (
                    id TEXT PRIMARY KEY,
                    order_kind TEXT NOT NULL,
                    order_id TEXT NOT NULL,
                    success INTEGER NOT NULL,
                    error TEXT NULL,
                    polled_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_order_polls_order ON system_order_status_polls(order_id, polled_utc DESC);
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

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

public sealed record EmergencyGatePersistedState(bool IsHalted, string Reason, DateTimeOffset ChangedUtc, string ChangedBy);

public sealed record EmergencyGateAuditEntry(string Action, string Detail, string Actor, DateTimeOffset CreatedUtc);