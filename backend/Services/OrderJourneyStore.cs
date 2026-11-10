using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class OrderJourneyStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly SystemIdentityOptions _identity;
    private readonly ILogger<OrderJourneyStore> _log;

    public OrderJourneyStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ICurrentUserAccessor currentUser,
        SystemIdentityOptions identity,
        ILogger<OrderJourneyStore> log)
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

    public void UpsertJourney(
        string correlationId,
        string source,
        string executionMode,
        string? strategyId,
        string? symbol,
        string? orderId,
        string currentStage,
        string? lastReason)
    {
        var userId = ActiveUserId;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO order_journey(
                    correlation_id,user_id,source,execution_mode,strategy_id,symbol,order_id,
                    current_stage,last_reason,started_at,updated_at)
                VALUES($id,$user,$src,$mode,$strat,$sym,$oid,$stage,$reason,$started,$updated)
                ON CONFLICT(correlation_id) DO UPDATE SET
                    order_id=CASE WHEN length(excluded.order_id)>0 THEN excluded.order_id ELSE order_journey.order_id END,
                    current_stage=excluded.current_stage,
                    last_reason=excluded.last_reason,
                    updated_at=excluded.updated_at;
                """;
            Add(command, "$id", correlationId);
            Add(command, "$user", userId);
            Add(command, "$src", source);
            Add(command, "$mode", executionMode);
            Add(command, "$strat", strategyId ?? "");
            Add(command, "$sym", symbol ?? "");
            Add(command, "$oid", orderId ?? "");
            Add(command, "$stage", currentStage);
            Add(command, "$reason", lastReason ?? "");
            Add(command, "$started", Utc(now));
            Add(command, "$updated", Utc(now));
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                _log.LogDebug(ex, "Journey {Id} already exists", correlationId);
            }
        }
    }

    public void UpdateStage(
        string correlationId,
        string currentStage,
        string? lastReason,
        string? orderId = null,
        string? symbol = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE order_journey SET
                    current_stage=$stage,
                    last_reason=$reason,
                    updated_at=$updated,
                    order_id=CASE WHEN length($oid)>0 THEN $oid ELSE order_id END,
                    symbol=CASE WHEN length($sym)>0 THEN $sym ELSE symbol END
                WHERE correlation_id=$id;
                """;
            Add(command, "$id", correlationId);
            Add(command, "$stage", currentStage);
            Add(command, "$reason", lastReason ?? "");
            Add(command, "$updated", Utc(now));
            Add(command, "$oid", orderId ?? "");
            Add(command, "$sym", symbol ?? "");
            command.ExecuteNonQuery();
        }
    }

    public void AppendEvent(string correlationId, string stage, string? detail, string? reason)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO order_journey_events(id,correlation_id,stage,detail,reason,created_at)
                VALUES($id,$corr,$stage,$detail,$reason,$at);
                """;
            Add(command, "$id", eventId);
            Add(command, "$corr", correlationId);
            Add(command, "$stage", stage);
            Add(command, "$detail", detail ?? "");
            Add(command, "$reason", reason ?? "");
            Add(command, "$at", Utc(at));
            command.ExecuteNonQuery();
        }

        _log.LogInformation(
            "Order journey {CorrelationId} → {Stage} {Detail} {Reason}",
            correlationId,
            stage,
            detail ?? "",
            reason ?? "");
    }

    public OrderJourneySummary? GetSummary(string correlationId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT correlation_id,user_id,source,execution_mode,strategy_id,symbol,order_id,
                       current_stage,last_reason,started_at,updated_at
                FROM order_journey WHERE correlation_id=$id LIMIT 1;
                """;
            Add(command, "$id", correlationId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var summary = ReadSummary(reader);
            return summary with { Timeline = ListEventsUnsafe(connection, correlationId) };
        }
    }

    public IReadOnlyList<OrderJourneySummary> ListRecent(int limit = 40)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT correlation_id,user_id,source,execution_mode,strategy_id,symbol,order_id,
                       current_stage,last_reason,started_at,updated_at
                FROM order_journey
                ORDER BY updated_at DESC LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 200));
            var list = new List<OrderJourneySummary>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = ReadSummary(reader);
                list.Add(row with { Timeline = ListEventsUnsafe(connection, row.CorrelationId) });
            }

            return list;
        }
    }

    public IReadOnlyList<OrderJourneySummary> ListRecentRejections(int limit = 25)
    {
        var rejected = new[]
        {
            OrderJourneyStages.RiskRejected,
            OrderJourneyStages.BrokerRejected,
            OrderJourneyStages.CircuitBreakerBlocked,
            OrderJourneyStages.Failed,
            OrderJourneyStages.DailyOrderLimitExceeded,
        };
        var placeholders = string.Join(",", rejected.Select((_, i) => $"$s{i}"));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                SELECT correlation_id,user_id,source,execution_mode,strategy_id,symbol,order_id,
                       current_stage,last_reason,started_at,updated_at
                FROM order_journey
                WHERE current_stage IN ({placeholders})
                ORDER BY updated_at DESC LIMIT $limit;
                """;
            for (var i = 0; i < rejected.Length; i++)
            {
                Add(command, $"$s{i}", rejected[i]);
            }

            Add(command, "$limit", Math.Clamp(limit, 1, 100));
            var list = new List<OrderJourneySummary>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = ReadSummary(reader);
                list.Add(row with { Timeline = ListEventsUnsafe(connection, row.CorrelationId) });
            }

            return list;
        }
    }

    private static IReadOnlyList<OrderJourneyEvent> ListEventsUnsafe(SqliteConnection connection, string correlationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stage,detail,reason,created_at FROM order_journey_events
            WHERE correlation_id=$corr ORDER BY created_at ASC;
            """;
        Add(command, "$corr", correlationId);
        var events = new List<OrderJourneyEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            events.Add(new OrderJourneyEvent(
                reader.GetString(0),
                NullIfEmpty(reader.GetString(1)),
                NullIfEmpty(reader.GetString(2)),
                ParseUtc(reader.GetString(3))));
        }

        return events;
    }

    private static OrderJourneySummary ReadSummary(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NullIfEmpty(reader.GetString(4)),
            NullIfEmpty(reader.GetString(5)),
            NullIfEmpty(reader.GetString(6)),
            reader.GetString(7),
            NullIfEmpty(reader.GetString(8)),
            ParseUtc(reader.GetString(9)),
            ParseUtc(reader.GetString(10)),
            []);

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS order_journey (
                    correlation_id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    source TEXT NOT NULL,
                    execution_mode TEXT NOT NULL,
                    strategy_id TEXT NOT NULL DEFAULT '',
                    symbol TEXT NOT NULL DEFAULT '',
                    order_id TEXT NOT NULL DEFAULT '',
                    current_stage TEXT NOT NULL,
                    last_reason TEXT NOT NULL DEFAULT '',
                    started_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_order_journey_updated ON order_journey(updated_at DESC);
                CREATE TABLE IF NOT EXISTS order_journey_events (
                    id TEXT PRIMARY KEY,
                    correlation_id TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    detail TEXT NOT NULL DEFAULT '',
                    reason TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_order_journey_events_corr ON order_journey_events(correlation_id, created_at);
                """;
            command.ExecuteNonQuery();
        }
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("o");

    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
