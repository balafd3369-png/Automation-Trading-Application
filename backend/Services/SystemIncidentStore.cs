using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ILogger<SystemIncidentStore> _log;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] ActiveStatuses =
    [
        nameof(SystemIncidentStatus.Detected),
        nameof(SystemIncidentStatus.Acknowledged),
        nameof(SystemIncidentStatus.Investigating),
    ];

    public SystemIncidentStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ILogger<SystemIncidentStore> log)
    {
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path);
        _log = log;
        Initialize();
    }

    public SystemIncidentRecord? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_incidents WHERE id = $id LIMIT 1;";
            Add(command, "$id", id.Trim());
            return ReadIncidents(command).FirstOrDefault();
        }
    }

    public SystemIncidentRecord? GetActiveByFingerprint(string fingerprint)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incidents
                WHERE fingerprint = $fp
                  AND status IN ($s0, $s1, $s2)
                ORDER BY last_occurrence_utc DESC
                LIMIT 1;
                """;
            Add(command, "$fp", fingerprint);
            Add(command, "$s0", ActiveStatuses[0]);
            Add(command, "$s1", ActiveStatuses[1]);
            Add(command, "$s2", ActiveStatuses[2]);
            return ReadIncidents(command).FirstOrDefault();
        }
    }

    public SystemIncidentRecord? GetLatestByFingerprintExcluding(string fingerprint, string excludeIncidentId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incidents
                WHERE fingerprint = $fp AND id <> $exclude
                ORDER BY first_detected_utc DESC
                LIMIT 1;
                """;
            Add(command, "$fp", fingerprint);
            Add(command, "$exclude", excludeIncidentId);
            return ReadIncidents(command).FirstOrDefault();
        }
    }

    public IReadOnlyList<SystemIncidentRecord> List(SystemIncidentListQuery query)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var sql = new StringBuilder("SELECT * FROM system_incidents WHERE 1=1");
            AppendListFilters(command, sql, query);
            sql.Append(" ORDER BY last_occurrence_utc DESC LIMIT $limit OFFSET $offset;");
            command.CommandText = sql.ToString();
            Add(command, "$limit", limit);
            Add(command, "$offset", offset);
            return ReadIncidents(command);
        }
    }

    public IReadOnlyList<SystemIncidentRecord> ListActive(int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incidents
                WHERE status IN ($s0, $s1, $s2)
                ORDER BY severity DESC, last_occurrence_utc DESC
                LIMIT $limit;
                """;
            Add(command, "$s0", ActiveStatuses[0]);
            Add(command, "$s1", ActiveStatuses[1]);
            Add(command, "$s2", ActiveStatuses[2]);
            Add(command, "$limit", limit);
            return ReadIncidents(command);
        }
    }

    public SystemIncidentRecord CreateOrUpdateOccurrence(
        IncidentEvaluationCandidate candidate,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var existing = GetActiveByFingerprintUnsafe(connection, transaction, candidate.Fingerprint);
            if (existing is not null)
            {
                var updated = existing with
                {
                    LastOccurrenceUtc = now,
                    LastUpdatedUtc = now,
                    OccurrenceCount = existing.OccurrenceCount + 1,
                    LastError = candidate.LastError ?? existing.LastError,
                    Severity = MaxSeverity(existing.Severity, candidate.Severity),
                    Description = candidate.Description,
                };
                UpdateIncidentUnsafe(connection, transaction, updated);
                AppendEventUnsafe(
                    connection,
                    transaction,
                    updated.Id,
                    SystemIncidentEventTypes.OccurrenceUpdated,
                    updated.Status,
                    updated.Status,
                    updated.Severity,
                    $"Occurrence count is now {updated.OccurrenceCount}.",
                    null,
                    now,
                    null);
                transaction.Commit();
                return updated;
            }

            var created = new SystemIncidentRecord(
                Guid.NewGuid().ToString("N"),
                candidate.Fingerprint,
                candidate.Title,
                candidate.Description,
                candidate.Component,
                candidate.ComponentIdentifier,
                candidate.Category,
                candidate.Severity,
                SystemIncidentStatus.Detected,
                now,
                now,
                now,
                null,
                null,
                null,
                null,
                null,
                1,
                candidate.LastError,
                candidate.MetadataJson ?? "{}");
            try
            {
                InsertIncidentUnsafe(connection, transaction, created);
                AppendEventUnsafe(
                    connection,
                    transaction,
                    created.Id,
                    SystemIncidentEventTypes.Detected,
                    null,
                    SystemIncidentStatus.Detected,
                    created.Severity,
                    created.Description,
                    null,
                    now,
                    null);
                transaction.Commit();
                return created;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                var raced = GetActiveByFingerprint(candidate.Fingerprint);
                if (raced is not null)
                {
                    return CreateOrUpdateOccurrence(candidate, now);
                }

                throw;
            }
        }
    }

    public SystemIncidentRecord UpdateStatus(
        string incidentId,
        SystemIncidentStatus newStatus,
        string eventType,
        string message,
        string? actor,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var current = GetByIdUnsafe(connection, transaction, incidentId)
                ?? throw new InvalidOperationException("Incident not found.");
            var updated = current with { Status = newStatus, LastUpdatedUtc = now };
            if (newStatus == SystemIncidentStatus.Acknowledged)
            {
                updated = updated with { AcknowledgedUtc = now, AcknowledgedBy = actor };
            }
            else if (newStatus == SystemIncidentStatus.Resolved)
            {
                updated = updated with { ResolvedUtc = now };
            }
            else if (newStatus == SystemIncidentStatus.Closed)
            {
                updated = updated with { ClosedUtc = now, ClosedBy = actor };
            }

            UpdateIncidentUnsafe(connection, transaction, updated);
            AppendEventUnsafe(
                connection,
                transaction,
                incidentId,
                eventType,
                current.Status,
                newStatus,
                updated.Severity,
                message,
                actor,
                now,
                null);
            transaction.Commit();
            return updated;
        }
    }

    public SystemIncidentNoteRecord AddNote(string incidentId, string note, string createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Note is required.", nameof(note));
        }

        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var incident = GetByIdUnsafe(connection, transaction, incidentId)
                ?? throw new InvalidOperationException("Incident not found.");
            var record = new SystemIncidentNoteRecord(
                Guid.NewGuid().ToString("N"),
                incidentId,
                note.Trim(),
                createdBy,
                now);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO system_incident_notes(id,incident_id,note,created_by,created_utc)
                    VALUES($id,$incident,$note,$by,$at);
                    """;
                Add(command, "$id", record.Id);
                Add(command, "$incident", record.IncidentId);
                Add(command, "$note", record.Note);
                Add(command, "$by", record.CreatedBy);
                Add(command, "$at", Utc(record.CreatedUtc));
                command.ExecuteNonQuery();
            }

            AppendEventUnsafe(
                connection,
                transaction,
                incidentId,
                SystemIncidentEventTypes.NoteAdded,
                incident.Status,
                incident.Status,
                incident.Severity,
                "Incident note added.",
                createdBy,
                now,
                null);
            transaction.Commit();
            return record;
        }
    }

    public IReadOnlyList<SystemIncidentEventRecord> ListEvents(string incidentId, int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incident_events
                WHERE incident_id = $id
                ORDER BY timestamp_utc ASC
                LIMIT $limit;
                """;
            Add(command, "$id", incidentId);
            Add(command, "$limit", limit);
            return ReadEvents(command);
        }
    }

    public IReadOnlyList<SystemIncidentNoteRecord> ListNotes(string incidentId, int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incident_notes
                WHERE incident_id = $id
                ORDER BY created_utc DESC
                LIMIT $limit;
                """;
            Add(command, "$id", incidentId);
            Add(command, "$limit", limit);
            return ReadNotes(command);
        }
    }

    public SystemAlertRecord CreateAlert(SystemAlertRecord alert)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_incident_alerts(
                    id,incident_id,fingerprint,severity,status,title,message,created_utc,
                    dispatched_utc,acknowledged_utc,acknowledged_by,attempt_count,last_error)
                VALUES(
                    $id,$incident,$fp,$severity,$status,$title,$message,$created,
                    $dispatched,$ack,$ackBy,$attempts,$error);
                """;
            WriteAlert(command, alert);
            command.ExecuteNonQuery();
            return alert;
        }
    }

    public SystemAlertRecord? GetAlert(string id)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_incident_alerts WHERE id = $id LIMIT 1;";
            Add(command, "$id", id);
            return ReadAlerts(command).FirstOrDefault();
        }
    }

    public SystemAlertRecord? GetRecentAlertByFingerprint(string fingerprint, DateTimeOffset sinceUtc)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM system_incident_alerts
                WHERE fingerprint = $fp AND created_utc >= $since
                ORDER BY created_utc DESC
                LIMIT 1;
                """;
            Add(command, "$fp", fingerprint);
            Add(command, "$since", Utc(sinceUtc));
            return ReadAlerts(command).FirstOrDefault();
        }
    }

    public IReadOnlyList<SystemAlertRecord> ListAlerts(SystemAlertListQuery query)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var sql = new StringBuilder("SELECT * FROM system_incident_alerts WHERE 1=1");
            if (query.Status is not null)
            {
                sql.Append(" AND status = $status");
                Add(command, "$status", query.Status.Value.ToString());
            }

            if (query.MinSeverity is not null)
            {
                sql.Append(" AND severity >= $minSeverity");
                Add(command, "$minSeverity", (int)query.MinSeverity.Value);
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
            return ReadAlerts(command);
        }
    }

    public SystemAlertRecord UpdateAlert(SystemAlertRecord alert)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE system_incident_alerts
                SET status = $status,
                    dispatched_utc = $dispatched,
                    acknowledged_utc = $ack,
                    acknowledged_by = $ackBy,
                    attempt_count = $attempts,
                    last_error = $error
                WHERE id = $id;
                """;
            Add(command, "$id", alert.Id);
            Add(command, "$status", alert.Status.ToString());
            Add(command, "$dispatched", alert.DispatchedUtc is null ? null : Utc(alert.DispatchedUtc.Value));
            Add(command, "$ack", alert.AcknowledgedUtc is null ? null : Utc(alert.AcknowledgedUtc.Value));
            Add(command, "$ackBy", alert.AcknowledgedBy);
            Add(command, "$attempts", alert.AttemptCount);
            Add(command, "$error", alert.LastError);
            command.ExecuteNonQuery();
            return alert;
        }
    }

    public SystemIncidentStatistics BuildStatistics(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var sql =
                """
                SELECT
                    COUNT(*) AS total,
                    COALESCE(SUM(CASE WHEN status IN ('Detected','Acknowledged','Investigating') THEN 1 ELSE 0 END), 0) AS active,
                    COALESCE(SUM(CASE WHEN severity = 'Critical' THEN 1 ELSE 0 END), 0) AS critical,
                    COALESCE(SUM(CASE WHEN severity = 'High' THEN 1 ELSE 0 END), 0) AS high,
                    COALESCE(SUM(CASE WHEN status = 'Acknowledged' THEN 1 ELSE 0 END), 0) AS ack,
                    COALESCE(SUM(CASE WHEN status = 'Investigating' THEN 1 ELSE 0 END), 0) AS investigating,
                    COALESCE(SUM(CASE WHEN status = 'Resolved' THEN 1 ELSE 0 END), 0) AS resolved,
                    COALESCE(SUM(CASE WHEN status = 'Closed' THEN 1 ELSE 0 END), 0) AS closed
                FROM system_incidents
                WHERE 1=1
                """;
            if (fromUtc is not null)
            {
                sql += " AND first_detected_utc >= $from";
                Add(command, "$from", Utc(fromUtc.Value));
            }

            if (toUtc is not null)
            {
                sql += " AND first_detected_utc <= $to";
                Add(command, "$to", Utc(toUtc.Value));
            }

            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            reader.Read();
            var total = reader.GetInt32(0);
            var active = reader.GetInt32(1);
            var critical = reader.GetInt32(2);
            var high = reader.GetInt32(3);
            var ack = reader.GetInt32(4);
            var investigating = reader.GetInt32(5);
            var resolved = reader.GetInt32(6);
            var closed = reader.GetInt32(7);
            reader.Close();

            return new SystemIncidentStatistics(
                total,
                active,
                critical,
                high,
                ack,
                investigating,
                resolved,
                closed,
                GroupCounts(connection, "component", fromUtc, toUtc),
                GroupCounts(connection, "severity", fromUtc, toUtc),
                GroupCounts(connection, "category", fromUtc, toUtc),
                AverageDuration(connection, "acknowledged_utc", "first_detected_utc", fromUtc, toUtc),
                AverageDuration(connection, "resolved_utc", "first_detected_utc", fromUtc, toUtc),
                RepeatedFingerprints(connection, fromUtc, toUtc),
                DateTimeOffset.UtcNow,
                fromUtc,
                toUtc);
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
                CREATE TABLE IF NOT EXISTS system_incidents (
                    id TEXT PRIMARY KEY,
                    fingerprint TEXT NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    component TEXT NOT NULL,
                    component_identifier TEXT NOT NULL,
                    category TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    status TEXT NOT NULL,
                    first_detected_utc TEXT NOT NULL,
                    last_occurrence_utc TEXT NOT NULL,
                    last_updated_utc TEXT NOT NULL,
                    acknowledged_utc TEXT NULL,
                    acknowledged_by TEXT NULL,
                    resolved_utc TEXT NULL,
                    closed_utc TEXT NULL,
                    closed_by TEXT NULL,
                    occurrence_count INTEGER NOT NULL,
                    last_error TEXT NULL,
                    metadata_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_system_incidents_last_occurrence
                    ON system_incidents(last_occurrence_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_system_incidents_status
                    ON system_incidents(status);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_system_incidents_active_fingerprint
                    ON system_incidents(fingerprint)
                    WHERE status IN ('Detected','Acknowledged','Investigating');
                CREATE TABLE IF NOT EXISTS system_incident_events (
                    id TEXT PRIMARY KEY,
                    incident_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    previous_status TEXT NULL,
                    new_status TEXT NULL,
                    severity TEXT NOT NULL,
                    message TEXT NOT NULL,
                    actor TEXT NULL,
                    timestamp_utc TEXT NOT NULL,
                    metadata_json TEXT NULL,
                    FOREIGN KEY(incident_id) REFERENCES system_incidents(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_system_incident_events_incident
                    ON system_incident_events(incident_id, timestamp_utc);
                CREATE TABLE IF NOT EXISTS system_incident_notes (
                    id TEXT PRIMARY KEY,
                    incident_id TEXT NOT NULL,
                    note TEXT NOT NULL,
                    created_by TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    FOREIGN KEY(incident_id) REFERENCES system_incidents(id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS system_incident_alerts (
                    id TEXT PRIMARY KEY,
                    incident_id TEXT NOT NULL,
                    fingerprint TEXT NOT NULL,
                    severity TEXT NOT NULL,
                    status TEXT NOT NULL,
                    title TEXT NOT NULL,
                    message TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    dispatched_utc TEXT NULL,
                    acknowledged_utc TEXT NULL,
                    acknowledged_by TEXT NULL,
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    last_error TEXT NULL,
                    FOREIGN KEY(incident_id) REFERENCES system_incidents(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_system_incident_alerts_created
                    ON system_incident_alerts(created_utc DESC);
                """;
            command.ExecuteNonQuery();
        }
    }

    private static Dictionary<string, int> GroupCounts(
        SqliteConnection connection,
        string column,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column}, COUNT(*) FROM system_incidents WHERE 1=1";
        if (fromUtc is not null)
        {
            command.CommandText += " AND first_detected_utc >= $from";
            Add(command, "$from", Utc(fromUtc.Value));
        }

        if (toUtc is not null)
        {
            command.CommandText += " AND first_detected_utc <= $to";
            Add(command, "$to", Utc(toUtc.Value));
        }

        command.CommandText += $" GROUP BY {column};";
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }

        return map;
    }

    private static double? AverageDuration(
        SqliteConnection connection,
        string endColumn,
        string startColumn,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT AVG(
                (julianday({endColumn}) - julianday({startColumn})) * 86400.0
            )
            FROM system_incidents
            WHERE {endColumn} IS NOT NULL
            """;
        if (fromUtc is not null)
        {
            command.CommandText += " AND first_detected_utc >= $from";
            Add(command, "$from", Utc(fromUtc.Value));
        }

        if (toUtc is not null)
        {
            command.CommandText += " AND first_detected_utc <= $to";
            Add(command, "$to", Utc(toUtc.Value));
        }

        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static int RepeatedFingerprints(SqliteConnection connection, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM (
                SELECT fingerprint
                FROM system_incidents
                WHERE 1=1
            """;
        if (fromUtc is not null)
        {
            command.CommandText += " AND first_detected_utc >= $from";
            Add(command, "$from", Utc(fromUtc.Value));
        }

        if (toUtc is not null)
        {
            command.CommandText += " AND first_detected_utc <= $to";
            Add(command, "$to", Utc(toUtc.Value));
        }

        command.CommandText += " GROUP BY fingerprint HAVING COUNT(*) > 1);";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SystemIncidentRecord? GetByIdUnsafe(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM system_incidents WHERE id = $id LIMIT 1;";
        Add(command, "$id", id);
        return ReadIncidents(command).FirstOrDefault();
    }

    private static SystemIncidentRecord? GetActiveByFingerprintUnsafe(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fingerprint)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT * FROM system_incidents
            WHERE fingerprint = $fp
              AND status IN ($s0, $s1, $s2)
            ORDER BY last_occurrence_utc DESC
            LIMIT 1;
            """;
        Add(command, "$fp", fingerprint);
        Add(command, "$s0", ActiveStatuses[0]);
        Add(command, "$s1", ActiveStatuses[1]);
        Add(command, "$s2", ActiveStatuses[2]);
        return ReadIncidents(command).FirstOrDefault();
    }

    private static void InsertIncidentUnsafe(SqliteConnection connection, SqliteTransaction transaction, SystemIncidentRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO system_incidents(
                id,fingerprint,title,description,component,component_identifier,category,severity,status,
                first_detected_utc,last_occurrence_utc,last_updated_utc,acknowledged_utc,acknowledged_by,
                resolved_utc,closed_utc,closed_by,occurrence_count,last_error,metadata_json)
            VALUES(
                $id,$fp,$title,$desc,$component,$ident,$category,$severity,$status,
                $first,$last,$updated,$ackAt,$ackBy,$resolved,$closed,$closedBy,$count,$error,$meta);
            """;
        WriteIncident(command, record);
        command.ExecuteNonQuery();
    }

    private static void UpdateIncidentUnsafe(SqliteConnection connection, SqliteTransaction transaction, SystemIncidentRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE system_incidents
            SET title=$title,description=$desc,component=$component,component_identifier=$ident,
                category=$category,severity=$severity,status=$status,last_occurrence_utc=$last,
                last_updated_utc=$updated,acknowledged_utc=$ackAt,acknowledged_by=$ackBy,
                resolved_utc=$resolved,closed_utc=$closed,closed_by=$closedBy,
                occurrence_count=$count,last_error=$error,metadata_json=$meta
            WHERE id=$id;
            """;
        WriteIncident(command, record);
        command.ExecuteNonQuery();
    }

    private static void WriteIncident(SqliteCommand command, SystemIncidentRecord record)
    {
        Add(command, "$id", record.Id);
        Add(command, "$fp", record.Fingerprint);
        Add(command, "$title", record.Title);
        Add(command, "$desc", record.Description);
        Add(command, "$component", record.Component);
        Add(command, "$ident", record.ComponentIdentifier);
        Add(command, "$category", record.Category.ToString());
        Add(command, "$severity", record.Severity.ToString());
        Add(command, "$status", record.Status.ToString());
        Add(command, "$first", Utc(record.FirstDetectedUtc));
        Add(command, "$last", Utc(record.LastOccurrenceUtc));
        Add(command, "$updated", Utc(record.LastUpdatedUtc));
        Add(command, "$ackAt", record.AcknowledgedUtc is null ? null : Utc(record.AcknowledgedUtc.Value));
        Add(command, "$ackBy", record.AcknowledgedBy);
        Add(command, "$resolved", record.ResolvedUtc is null ? null : Utc(record.ResolvedUtc.Value));
        Add(command, "$closed", record.ClosedUtc is null ? null : Utc(record.ClosedUtc.Value));
        Add(command, "$closedBy", record.ClosedBy);
        Add(command, "$count", record.OccurrenceCount);
        Add(command, "$error", record.LastError);
        Add(command, "$meta", record.MetadataJson);
    }

    private static void AppendEventUnsafe(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string incidentId,
        string eventType,
        SystemIncidentStatus? previousStatus,
        SystemIncidentStatus? newStatus,
        SystemIncidentSeverity severity,
        string message,
        string? actor,
        DateTimeOffset now,
        string? metadataJson)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO system_incident_events(
                id,incident_id,event_type,previous_status,new_status,severity,message,actor,timestamp_utc,metadata_json)
            VALUES($id,$incident,$type,$prev,$next,$sev,$msg,$actor,$at,$meta);
            """;
        Add(command, "$id", Guid.NewGuid().ToString("N"));
        Add(command, "$incident", incidentId);
        Add(command, "$type", eventType);
        Add(command, "$prev", previousStatus?.ToString());
        Add(command, "$next", newStatus?.ToString());
        Add(command, "$sev", severity.ToString());
        Add(command, "$msg", message);
        Add(command, "$actor", actor);
        Add(command, "$at", Utc(now));
        Add(command, "$meta", metadataJson);
        command.ExecuteNonQuery();
    }

    private static void AppendListFilters(SqliteCommand command, StringBuilder sql, SystemIncidentListQuery query)
    {
        if (query.Status is not null)
        {
            sql.Append(" AND status = $status");
            Add(command, "$status", query.Status.Value.ToString());
        }

        if (query.MinSeverity is not null)
        {
            sql.Append(" AND severity >= $minSeverity");
            Add(command, "$minSeverity", (int)query.MinSeverity.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Component))
        {
            sql.Append(" AND component = $component");
            Add(command, "$component", query.Component.Trim());
        }

        if (query.FromUtc is not null)
        {
            sql.Append(" AND last_occurrence_utc >= $from");
            Add(command, "$from", Utc(query.FromUtc.Value));
        }

        if (query.ToUtc is not null)
        {
            sql.Append(" AND last_occurrence_utc <= $to");
            Add(command, "$to", Utc(query.ToUtc.Value));
        }
    }

    private static List<SystemIncidentRecord> ReadIncidents(SqliteCommand command)
    {
        var list = new List<SystemIncidentRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemIncidentRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                Enum.Parse<SystemIncidentCategory>(reader.GetString(6), ignoreCase: true),
                Enum.Parse<SystemIncidentSeverity>(reader.GetString(7), ignoreCase: true),
                Enum.Parse<SystemIncidentStatus>(reader.GetString(8), ignoreCase: true),
                ParseUtc(reader.GetString(9)),
                ParseUtc(reader.GetString(10)),
                ParseUtc(reader.GetString(11)),
                reader.IsDBNull(12) ? null : ParseUtc(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14)),
                reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15)),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.GetString(19)));
        }

        return list;
    }

    private static List<SystemIncidentEventRecord> ReadEvents(SqliteCommand command)
    {
        var list = new List<SystemIncidentEventRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemIncidentEventRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : Enum.Parse<SystemIncidentStatus>(reader.GetString(3), ignoreCase: true),
                reader.IsDBNull(4) ? null : Enum.Parse<SystemIncidentStatus>(reader.GetString(4), ignoreCase: true),
                Enum.Parse<SystemIncidentSeverity>(reader.GetString(5), ignoreCase: true),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                ParseUtc(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return list;
    }

    private static List<SystemIncidentNoteRecord> ReadNotes(SqliteCommand command)
    {
        var list = new List<SystemIncidentNoteRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemIncidentNoteRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ParseUtc(reader.GetString(4))));
        }

        return list;
    }

    private static List<SystemAlertRecord> ReadAlerts(SqliteCommand command)
    {
        var list = new List<SystemAlertRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemAlertRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<SystemIncidentSeverity>(reader.GetString(3), ignoreCase: true),
                Enum.Parse<SystemAlertStatus>(reader.GetString(4), ignoreCase: true),
                reader.GetString(5),
                reader.GetString(6),
                ParseUtc(reader.GetString(7)),
                reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8)),
                reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return list;
    }

    private static void WriteAlert(SqliteCommand command, SystemAlertRecord alert)
    {
        Add(command, "$id", alert.Id);
        Add(command, "$incident", alert.IncidentId);
        Add(command, "$fp", alert.Fingerprint);
        Add(command, "$severity", alert.Severity.ToString());
        Add(command, "$status", alert.Status.ToString());
        Add(command, "$title", alert.Title);
        Add(command, "$message", alert.Message);
        Add(command, "$created", Utc(alert.CreatedUtc));
        Add(command, "$dispatched", alert.DispatchedUtc is null ? null : Utc(alert.DispatchedUtc.Value));
        Add(command, "$ack", alert.AcknowledgedUtc is null ? null : Utc(alert.AcknowledgedUtc.Value));
        Add(command, "$ackBy", alert.AcknowledgedBy);
        Add(command, "$attempts", alert.AttemptCount);
        Add(command, "$error", alert.LastError);
    }

    private static SystemIncidentSeverity MaxSeverity(SystemIncidentSeverity left, SystemIncidentSeverity right) =>
        (SystemIncidentSeverity)Math.Max((int)left, (int)right);

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
}