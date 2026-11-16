using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentNotificationStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ILogger<SystemIncidentNotificationStore> _log;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string RuntimeSettingsKey = "notification_settings";
    public const string RuntimeEscalationKey = "escalation_policies";

    public SystemIncidentNotificationStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ILogger<SystemIncidentNotificationStore> log)
    {
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
    }

    public IncidentNotificationRecord? GetNotification(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_incident_notifications WHERE id = $id LIMIT 1;";
            Add(command, "$id", id);
            return ReadNotifications(command).FirstOrDefault();
        }
    }

    public IReadOnlyList<IncidentNotificationRecord> ListNotifications(IncidentNotificationListQuery query)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var sql = new StringBuilder("SELECT * FROM system_incident_notifications WHERE 1=1");
            if (!string.IsNullOrWhiteSpace(query.IncidentId))
            {
                sql.Append(" AND incident_id = $incident");
                Add(command, "$incident", query.IncidentId.Trim());
            }

            if (query.Channel is not null)
            {
                sql.Append(" AND channel = $channel");
                Add(command, "$channel", query.Channel.Value.ToString());
            }

            if (query.Status is not null)
            {
                sql.Append(" AND delivery_status = $status");
                Add(command, "$status", query.Status.Value.ToString());
            }

            if (query.NotificationType is not null)
            {
                sql.Append(" AND notification_type = $type");
                Add(command, "$type", query.NotificationType.Value.ToString());
            }

            if (query.FromUtc is not null)
            {
                sql.Append(" AND created_utc >= $from");
                Add(command, "$from", Utc(query.FromUtc.Value));
            }

            if (query.ToUtc is not null)
            {
                sql.Append(" AND created_utc <= $to");
                Add(command, "$to", Utc(query.ToUtc.Value));
            }

            sql.Append(" ORDER BY created_utc DESC LIMIT $limit OFFSET $offset;");
            command.CommandText = sql.ToString();
            Add(command, "$limit", limit);
            Add(command, "$offset", offset);
            return ReadNotifications(command);
        }
    }

    public bool TryInsertNotification(IncidentNotificationRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                InsertNotificationUnsafe(connection, transaction, record);
                transaction.Commit();
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                return false;
            }
        }
    }

    public IncidentNotificationRecord UpdateNotification(IncidentNotificationRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            UpdateNotificationUnsafe(connection, transaction, record);
            transaction.Commit();
            return GetNotification(record.Id)!;
        }
    }

    public IReadOnlyList<IncidentNotificationRecord> ClaimDueNotifications(
        int limit,
        DateTimeOffset now,
        string ownerInstanceId,
        int leaseSeconds)
    {
        limit = Math.Clamp(limit, 1, 50);
        leaseSeconds = Math.Clamp(leaseSeconds, 30, 3600);
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText =
                """
                SELECT id FROM system_incident_notifications
                WHERE delivery_status IN ($pending, $retry)
                  AND (next_retry_utc IS NULL OR next_retry_utc <= $now)
                  AND (processing_lease_expires_utc IS NULL OR processing_lease_expires_utc <= $now)
                ORDER BY created_utc ASC
                LIMIT $limit;
                """;
            Add(select, "$pending", nameof(IncidentNotificationDeliveryStatus.Pending));
            Add(select, "$retry", nameof(IncidentNotificationDeliveryStatus.RetryScheduled));
            Add(select, "$now", Utc(now));
            Add(select, "$limit", limit);
            var ids = new List<string>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }

            var claimed = new List<IncidentNotificationRecord>();
            var leaseExpires = now.AddSeconds(leaseSeconds);
            var claimToken = Guid.NewGuid().ToString("N");
            foreach (var id in ids)
            {
                using var claim = connection.CreateCommand();
                claim.Transaction = transaction;
                claim.CommandText =
                    """
                    UPDATE system_incident_notifications
                    SET delivery_status = $processing,
                        processing_owner = $owner,
                        processing_lease_expires_utc = $lease,
                        claim_token = $token,
                        last_attempt_utc = $now
                    WHERE id = $id
                      AND delivery_status IN ($pending, $retry)
                      AND (processing_lease_expires_utc IS NULL OR processing_lease_expires_utc <= $now);
                    """;
                Add(claim, "$processing", nameof(IncidentNotificationDeliveryStatus.Processing));
                Add(claim, "$owner", ownerInstanceId);
                Add(claim, "$lease", Utc(leaseExpires));
                Add(claim, "$token", claimToken);
                Add(claim, "$now", Utc(now));
                Add(claim, "$id", id);
                Add(claim, "$pending", nameof(IncidentNotificationDeliveryStatus.Pending));
                Add(claim, "$retry", nameof(IncidentNotificationDeliveryStatus.RetryScheduled));
                if (claim.ExecuteNonQuery() == 0)
                {
                    continue;
                }

                using var load = connection.CreateCommand();
                load.Transaction = transaction;
                load.CommandText = "SELECT * FROM system_incident_notifications WHERE id = $id;";
                Add(load, "$id", id);
                var current = ReadNotifications(load).FirstOrDefault();
                if (current is not null)
                {
                    claimed.Add(current);
                }
            }

            transaction.Commit();
            return claimed;
        }
    }

    public bool TryInsertEscalationJob(IncidentEscalationJobRecord job)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                InsertEscalationJobUnsafe(connection, transaction, job);
                transaction.Commit();
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                return false;
            }
        }
    }

    public IReadOnlyList<IncidentEscalationJobRecord> ListDueEscalationJobs(DateTimeOffset now, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incident_escalation_jobs
                WHERE status = $pending AND scheduled_utc <= $now
                ORDER BY scheduled_utc ASC
                LIMIT $limit;
                """;
            Add(command, "$pending", nameof(EscalationJobStatus.Pending));
            Add(command, "$now", Utc(now));
            Add(command, "$limit", limit);
            return ReadEscalationJobs(command);
        }
    }

    public void CancelEscalationJobsForIncident(string incidentId, IEnumerable<EscalationStepKind>? kinds = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            if (kinds is null)
            {
                command.CommandText =
                    """
                    UPDATE system_incident_escalation_jobs
                    SET status = $cancelled
                    WHERE incident_id = $incident AND status = $pending;
                    """;
            }
            else
            {
                var kindList = kinds.ToList();
                if (kindList.Count == 0)
                {
                    return;
                }

                var placeholders = new StringBuilder();
                for (var i = 0; i < kindList.Count; i++)
                {
                    if (i > 0)
                    {
                        placeholders.Append(',');
                    }

                    var name = $"$k{i}";
                    placeholders.Append(name);
                    Add(command, name, kindList[i].ToString());
                }

                command.CommandText =
                    $"""
                    UPDATE system_incident_escalation_jobs
                    SET status = $cancelled
                    WHERE incident_id = $incident AND status = $pending AND step_kind IN ({placeholders});
                    """;
            }

            Add(command, "$cancelled", nameof(EscalationJobStatus.Cancelled));
            Add(command, "$pending", nameof(EscalationJobStatus.Pending));
            Add(command, "$incident", incidentId);
            command.ExecuteNonQuery();
        }
    }

    public IncidentEscalationJobRecord MarkEscalationExecuted(string jobId, DateTimeOffset executedUtc)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE system_incident_escalation_jobs
                SET status = $executed, executed_utc = $at
                WHERE id = $id AND status = $pending;
                """;
            Add(command, "$executed", nameof(EscalationJobStatus.Executed));
            Add(command, "$at", Utc(executedUtc));
            Add(command, "$id", jobId);
            Add(command, "$pending", nameof(EscalationJobStatus.Pending));
            command.ExecuteNonQuery();
            command.CommandText = "SELECT * FROM system_incident_escalation_jobs WHERE id = $id;";
            return ReadEscalationJobs(command).First();
        }
    }

    public bool HasRecentNotification(string idempotencyKey, DateTimeOffset sinceUtc)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 1 FROM system_incident_notifications
                WHERE idempotency_key = $key AND created_utc >= $since
                  AND delivery_status NOT IN ($cancelled)
                LIMIT 1;
                """;
            Add(command, "$key", idempotencyKey);
            Add(command, "$since", Utc(sinceUtc));
            Add(command, "$cancelled", nameof(IncidentNotificationDeliveryStatus.Cancelled));
            return command.ExecuteScalar() is not null;
        }
    }

    public void SaveRuntimeJson(string key, string json)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_notification_runtime_config(config_key, json_value, updated_utc)
                VALUES ($key, $json, $updated)
                ON CONFLICT(config_key) DO UPDATE SET json_value = excluded.json_value, updated_utc = excluded.updated_utc;
                """;
            Add(command, "$key", key);
            Add(command, "$json", json);
            Add(command, "$updated", Utc(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public string? LoadRuntimeJson(string key)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json_value FROM system_notification_runtime_config WHERE config_key = $key LIMIT 1;";
            Add(command, "$key", key);
            return command.ExecuteScalar() as string;
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
                CREATE TABLE IF NOT EXISTS system_incident_notifications (
                    id TEXT PRIMARY KEY,
                    incident_id TEXT NOT NULL,
                    idempotency_key TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    recipient TEXT NOT NULL,
                    notification_type TEXT NOT NULL,
                    subject TEXT NOT NULL,
                    body_html TEXT NOT NULL,
                    body_text TEXT NOT NULL,
                    delivery_status TEXT NOT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    created_utc TEXT NOT NULL,
                    sent_utc TEXT NULL,
                    last_attempt_utc TEXT NULL,
                    next_retry_utc TEXT NULL,
                    failure_reason TEXT NULL,
                    provider_metadata_json TEXT NULL,
                    FOREIGN KEY(incident_id) REFERENCES system_incidents(id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_system_incident_notifications_idempotency
                    ON system_incident_notifications(idempotency_key);
                CREATE INDEX IF NOT EXISTS ix_system_incident_notifications_pending
                    ON system_incident_notifications(delivery_status, next_retry_utc, created_utc);
                CREATE INDEX IF NOT EXISTS ix_system_incident_notifications_incident
                    ON system_incident_notifications(incident_id, created_utc DESC);
                CREATE TABLE IF NOT EXISTS system_incident_escalation_jobs (
                    id TEXT PRIMARY KEY,
                    incident_id TEXT NOT NULL,
                    step_kind TEXT NOT NULL,
                    step_index INTEGER NOT NULL,
                    idempotency_key TEXT NOT NULL,
                    status TEXT NOT NULL,
                    scheduled_utc TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    executed_utc TEXT NULL,
                    FOREIGN KEY(incident_id) REFERENCES system_incidents(id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_system_escalation_active
                    ON system_incident_escalation_jobs(incident_id, step_kind, step_index)
                    WHERE status = 'Pending';
                CREATE UNIQUE INDEX IF NOT EXISTS ux_system_escalation_idempotency
                    ON system_incident_escalation_jobs(idempotency_key);
                CREATE INDEX IF NOT EXISTS ix_system_escalation_due
                    ON system_incident_escalation_jobs(status, scheduled_utc);
                CREATE TABLE IF NOT EXISTS system_notification_runtime_config (
                    config_key TEXT PRIMARY KEY,
                    json_value TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );
                """;
            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Notification store initialization failed.");
                throw;
            }
        }
    }

    private static void InsertNotificationUnsafe(SqliteConnection connection, SqliteTransaction transaction, IncidentNotificationRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO system_incident_notifications(
                id,incident_id,idempotency_key,channel,recipient,notification_type,subject,body_html,body_text,
                delivery_status,attempt_count,created_utc,sent_utc,last_attempt_utc,next_retry_utc,failure_reason,provider_metadata_json,
                processing_owner,processing_lease_expires_utc,claim_token,claim_fencing_token)
            VALUES(
                $id,$incident,$key,$channel,$recipient,$type,$subject,$html,$text,
                $status,$attempts,$created,$sent,$last,$next,$failure,$meta,
                $owner,$plexp,$ctoken,$cfence);
            """;
        WriteNotification(command, record);
        command.ExecuteNonQuery();
    }

    private static void UpdateNotificationUnsafe(SqliteConnection connection, SqliteTransaction transaction, IncidentNotificationRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE system_incident_notifications
            SET delivery_status=$status,attempt_count=$attempts,sent_utc=$sent,last_attempt_utc=$last,
                next_retry_utc=$next,failure_reason=$failure,provider_metadata_json=$meta,
                processing_owner=$owner,processing_lease_expires_utc=$plexp,claim_token=$ctoken,claim_fencing_token=$cfence
            WHERE id=$id;
            """;
        WriteNotification(command, record);
        command.ExecuteNonQuery();
    }

    private static void WriteNotification(SqliteCommand command, IncidentNotificationRecord record)
    {
        Add(command, "$id", record.Id);
        Add(command, "$incident", record.IncidentId);
        Add(command, "$key", record.IdempotencyKey);
        Add(command, "$channel", record.Channel.ToString());
        Add(command, "$recipient", record.Recipient);
        Add(command, "$type", record.NotificationType.ToString());
        Add(command, "$subject", record.Subject);
        Add(command, "$html", record.BodyHtml);
        Add(command, "$text", record.BodyText);
        Add(command, "$status", record.DeliveryStatus.ToString());
        Add(command, "$attempts", record.AttemptCount);
        Add(command, "$created", Utc(record.CreatedUtc));
        Add(command, "$sent", record.SentUtc is null ? null : Utc(record.SentUtc.Value));
        Add(command, "$last", record.LastAttemptUtc is null ? null : Utc(record.LastAttemptUtc.Value));
        Add(command, "$next", record.NextRetryUtc is null ? null : Utc(record.NextRetryUtc.Value));
        Add(command, "$failure", record.FailureReason);
        Add(command, "$meta", record.ProviderMetadataJson);
        Add(command, "$owner", record.ProcessingOwner);
        Add(command, "$plexp", record.ProcessingLeaseExpiresUtc is null ? null : Utc(record.ProcessingLeaseExpiresUtc.Value));
        Add(command, "$ctoken", record.ClaimToken);
        Add(command, "$cfence", record.ClaimFencingToken);
    }

    private static void InsertEscalationJobUnsafe(SqliteConnection connection, SqliteTransaction transaction, IncidentEscalationJobRecord job)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO system_incident_escalation_jobs(
                id,incident_id,step_kind,step_index,idempotency_key,status,scheduled_utc,created_utc,executed_utc)
            VALUES($id,$incident,$kind,$index,$key,$status,$scheduled,$created,$executed);
            """;
        Add(command, "$id", job.Id);
        Add(command, "$incident", job.IncidentId);
        Add(command, "$kind", job.StepKind.ToString());
        Add(command, "$index", job.StepIndex);
        Add(command, "$key", job.IdempotencyKey);
        Add(command, "$status", job.Status.ToString());
        Add(command, "$scheduled", Utc(job.ScheduledUtc));
        Add(command, "$created", Utc(job.CreatedUtc));
        Add(command, "$executed", job.ExecutedUtc is null ? null : Utc(job.ExecutedUtc.Value));
        command.ExecuteNonQuery();
    }

    private static List<IncidentNotificationRecord> ReadNotifications(SqliteCommand command)
    {
        var list = new List<IncidentNotificationRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new IncidentNotificationRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<IncidentNotificationChannel>(reader.GetString(3), ignoreCase: true),
                reader.GetString(4),
                Enum.Parse<IncidentNotificationType>(reader.GetString(5), ignoreCase: true),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                Enum.Parse<IncidentNotificationDeliveryStatus>(reader.GetString(9), ignoreCase: true),
                reader.GetInt32(10),
                ParseUtc(reader.GetString(11)),
                reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12)),
                reader.IsDBNull(13) ? null : ParseUtc(reader.GetString(13)),
                reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14)),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : null,
                reader.FieldCount > 18 && !reader.IsDBNull(18) ? ParseUtc(reader.GetString(18)) : null,
                reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetString(19) : null,
                reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetInt64(20) : null));
        }

        return list;
    }

    private static List<IncidentEscalationJobRecord> ReadEscalationJobs(SqliteCommand command)
    {
        var list = new List<IncidentEscalationJobRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new IncidentEscalationJobRecord(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<EscalationStepKind>(reader.GetString(2), ignoreCase: true),
                reader.GetInt32(3),
                reader.GetString(4),
                Enum.Parse<EscalationJobStatus>(reader.GetString(5), ignoreCase: true),
                ParseUtc(reader.GetString(6)),
                ParseUtc(reader.GetString(7)),
                reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8))));
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

    private static DateTimeOffset ParseUtc(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);

    public static string SanitizeMetadata(object? value) =>
        value is null ? "{}" : JsonSerializer.Serialize(value, JsonOptions);
}