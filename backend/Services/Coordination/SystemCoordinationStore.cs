using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemCoordinationStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly SystemReliabilityOptions _options;
    private readonly ILogger<SystemCoordinationStore> _log;

    public SystemCoordinationStore(
        IConfiguration configuration,
        DatabaseOptions database,
        SystemReliabilityOptions options,
        ILogger<SystemCoordinationStore> log)
    {
        _options = options;
        _log = log;
        var path = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        _connectionString = DatabaseConnectionResolver.BuildSqliteConnectionString(path, options.SqliteBusyTimeoutMs);
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        if (_options.SqliteWalMode)
        {
            using var wal = connection.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }

        return connection;
    }

    public void UpsertInstance(SystemInstanceRecord record)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_instance_registry(
                    instance_id,host_name,application_version,is_draining,is_leader,leader_role,
                    registered_utc,last_heartbeat_utc,lease_expires_utc)
                VALUES($id,$host,$ver,$drain,$leader,$role,$reg,$hb,$lease)
                ON CONFLICT(instance_id) DO UPDATE SET
                    host_name=excluded.host_name,
                    application_version=excluded.application_version,
                    is_draining=excluded.is_draining,
                    is_leader=excluded.is_leader,
                    leader_role=excluded.leader_role,
                    last_heartbeat_utc=excluded.last_heartbeat_utc,
                    lease_expires_utc=excluded.lease_expires_utc;
                """;
            WriteInstance(command, record);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SystemInstanceRecord> ListInstances()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_instance_registry ORDER BY last_heartbeat_utc DESC;";
            return ReadInstances(command);
        }
    }

    public SystemInstanceRecord? GetInstance(string instanceId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_instance_registry WHERE instance_id = $id LIMIT 1;";
            Add(command, "$id", instanceId);
            return ReadInstances(command).FirstOrDefault();
        }
    }

    public void SetInstanceDraining(string instanceId, bool draining)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE system_instance_registry SET is_draining = $d WHERE instance_id = $id;";
            Add(command, "$d", draining ? 1 : 0);
            Add(command, "$id", instanceId);
            command.ExecuteNonQuery();
        }
    }

    public DistributedLockAcquireResult TryAcquireLock(string lockName, string ownerInstanceId, string lockToken, DateTimeOffset now, DateTimeOffset expiresUtc)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var nextFence = NextFencingToken(connection, transaction, lockName) + 1;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO system_distributed_locks(
                    lock_name,owner_instance_id,lock_token,fencing_token,created_utc,expires_utc,last_renewed_utc)
                VALUES($name,$owner,$token,$fence,$created,$expires,$renew)
                ON CONFLICT(lock_name) DO UPDATE SET
                    owner_instance_id = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.owner_instance_id ELSE system_distributed_locks.owner_instance_id END,
                    lock_token = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.lock_token ELSE system_distributed_locks.lock_token END,
                    fencing_token = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.fencing_token ELSE system_distributed_locks.fencing_token END,
                    created_utc = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.created_utc ELSE system_distributed_locks.created_utc END,
                    expires_utc = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.expires_utc ELSE system_distributed_locks.expires_utc END,
                    last_renewed_utc = CASE WHEN system_distributed_locks.expires_utc <= $now THEN excluded.last_renewed_utc ELSE system_distributed_locks.last_renewed_utc END;
                """;
            Add(command, "$name", lockName);
            Add(command, "$owner", ownerInstanceId);
            Add(command, "$token", lockToken);
            Add(command, "$fence", nextFence);
            Add(command, "$created", Utc(now));
            Add(command, "$expires", Utc(expiresUtc));
            Add(command, "$renew", Utc(now));
            Add(command, "$now", Utc(now));
            command.ExecuteNonQuery();

            var current = GetLockUnsafe(connection, transaction, lockName);
            transaction.Commit();
            if (current is null)
            {
                return new DistributedLockAcquireResult(false, null, 0, null, null);
            }

            var acquired = current.OwnerInstanceId == ownerInstanceId && current.LockToken == lockToken;
            return new DistributedLockAcquireResult(
                acquired,
                acquired ? current.LockToken : null,
                current.FencingToken,
                current.ExpiresUtc,
                acquired ? null : current.OwnerInstanceId);
        }
    }

    public bool RenewLock(string lockName, string ownerInstanceId, string lockToken, DateTimeOffset expiresUtc, DateTimeOffset renewedUtc)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE system_distributed_locks
                SET expires_utc = $expires, last_renewed_utc = $renew
                WHERE lock_name = $name AND owner_instance_id = $owner AND lock_token = $token AND expires_utc > $now;
                """;
            Add(command, "$expires", Utc(expiresUtc));
            Add(command, "$renew", Utc(renewedUtc));
            Add(command, "$name", lockName);
            Add(command, "$owner", ownerInstanceId);
            Add(command, "$token", lockToken);
            Add(command, "$now", Utc(renewedUtc));
            return command.ExecuteNonQuery() > 0;
        }
    }

    public bool ReleaseLock(string lockName, string ownerInstanceId, string lockToken)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM system_distributed_locks
                WHERE lock_name = $name AND owner_instance_id = $owner AND lock_token = $token;
                """;
            Add(command, "$name", lockName);
            Add(command, "$owner", ownerInstanceId);
            Add(command, "$token", lockToken);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public IReadOnlyList<DistributedLockRecord> ListLocks()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_distributed_locks ORDER BY lock_name;";
            return ReadLocks(command, DateTimeOffset.UtcNow);
        }
    }

    public bool TryElectLeader(string role, string instanceId, DateTimeOffset now, DateTimeOffset leaseExpires, long fencingToken)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO system_leader_elections(role,leader_instance_id,fencing_token,elected_utc,lease_expires_utc,last_heartbeat_utc)
                VALUES($role,$leader,$fence,$elected,$lease,$hb)
                ON CONFLICT(role) DO UPDATE SET
                    leader_instance_id = CASE WHEN system_leader_elections.lease_expires_utc <= $now THEN excluded.leader_instance_id ELSE system_leader_elections.leader_instance_id END,
                    fencing_token = CASE WHEN system_leader_elections.lease_expires_utc <= $now THEN excluded.fencing_token ELSE system_leader_elections.fencing_token END,
                    elected_utc = CASE WHEN system_leader_elections.lease_expires_utc <= $now THEN excluded.elected_utc ELSE system_leader_elections.elected_utc END,
                    lease_expires_utc = CASE WHEN system_leader_elections.lease_expires_utc <= $now THEN excluded.lease_expires_utc ELSE system_leader_elections.lease_expires_utc END,
                    last_heartbeat_utc = CASE WHEN system_leader_elections.leader_instance_id = $leader THEN excluded.last_heartbeat_utc ELSE system_leader_elections.last_heartbeat_utc END;
                """;
            Add(command, "$role", role);
            Add(command, "$leader", instanceId);
            Add(command, "$fence", fencingToken);
            Add(command, "$elected", Utc(now));
            Add(command, "$lease", Utc(leaseExpires));
            Add(command, "$hb", Utc(now));
            Add(command, "$now", Utc(now));
            command.ExecuteNonQuery();
            var leader = GetLeaderUnsafe(connection, transaction, role);
            transaction.Commit();
            return leader?.LeaderInstanceId == instanceId;
        }
    }

    public LeaderElectionRecord? GetLeader(string role)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var leader = GetLeaderUnsafe(connection, transaction, role);
            transaction.Commit();
            return leader;
        }
    }

    public void UpsertWorker(SystemWorkerRegistryRecord worker)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_worker_registry(
                    worker_id,worker_name,instance_id,worker_type,status,started_utc,last_heartbeat_utc,
                    last_success_utc,last_failure_utc,restart_count,current_lease,last_error,recovery_enabled,recovery_eligible)
                VALUES($id,$name,$inst,$type,$status,$start,$hb,$ok,$fail,$restart,$lease,$err,$ren,$rel)
                ON CONFLICT(worker_id) DO UPDATE SET
                    worker_name=excluded.worker_name,
                    instance_id=excluded.instance_id,
                    worker_type=excluded.worker_type,
                    status=excluded.status,
                    last_heartbeat_utc=excluded.last_heartbeat_utc,
                    last_success_utc=excluded.last_success_utc,
                    last_failure_utc=excluded.last_failure_utc,
                    restart_count=excluded.restart_count,
                    current_lease=excluded.current_lease,
                    last_error=excluded.last_error,
                    recovery_enabled=excluded.recovery_enabled,
                    recovery_eligible=excluded.recovery_eligible;
                """;
            WriteWorker(command, worker);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SystemWorkerRegistryRecord> ListWorkers(string? instanceId = null)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_worker_registry WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                command.CommandText += " AND instance_id = $inst";
                Add(command, "$inst", instanceId.Trim());
            }

            command.CommandText += " ORDER BY worker_name;";
            return ReadWorkers(command);
        }
    }

    public SystemWorkerRegistryRecord? GetWorker(string workerId)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_worker_registry WHERE worker_id = $id LIMIT 1;";
            Add(command, "$id", workerId);
            return ReadWorkers(command).FirstOrDefault();
        }
    }

    public void AppendRecovery(SystemRecoveryHistoryRecord record)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_recovery_history(
                    id,worker_id,worker_name,instance_id,triggering_incident_id,recovery_reason,attempt_number,result,
                    correlation_id,started_utc,completed_utc,failure_reason)
                VALUES($id,$wid,$name,$inst,$incident,$reason,$attempt,$result,$corr,$start,$done,$fail);
                """;
            WriteRecovery(command, record);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SystemRecoveryHistoryRecord> ListRecoveryHistory(int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_recovery_history ORDER BY started_utc DESC LIMIT $limit;";
            Add(command, "$limit", limit);
            return ReadRecovery(command);
        }
    }

    public IReadOnlyList<SystemRecoveryPolicyRecord> ListRecoveryPolicies()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM system_recovery_policies ORDER BY worker_name;";
            return ReadPolicies(command);
        }
    }

    public void UpsertRecoveryPolicy(SystemRecoveryPolicyRecord policy)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO system_recovery_policies(worker_name,automatic_recovery_allowed,max_attempts,cooldown_seconds,notes)
                VALUES($name,$allow,$max,$cooldown,$notes)
                ON CONFLICT(worker_name) DO UPDATE SET
                    automatic_recovery_allowed=excluded.automatic_recovery_allowed,
                    max_attempts=excluded.max_attempts,
                    cooldown_seconds=excluded.cooldown_seconds,
                    notes=excluded.notes;
                """;
            Add(command, "$name", policy.WorkerName);
            Add(command, "$allow", policy.AutomaticRecoveryAllowed ? 1 : 0);
            Add(command, "$max", policy.MaxAttempts);
            Add(command, "$cooldown", policy.CooldownSeconds);
            Add(command, "$notes", policy.Notes);
            command.ExecuteNonQuery();
        }
    }

    public int ReleaseStuckNotificationClaims(DateTimeOffset now)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE system_incident_notifications
                SET delivery_status = $retry,
                    processing_owner = NULL,
                    processing_lease_expires_utc = NULL,
                    claim_token = NULL,
                    next_retry_utc = $now
                WHERE delivery_status = $processing
                  AND processing_lease_expires_utc IS NOT NULL
                  AND processing_lease_expires_utc < $now;
                """;
            Add(command, "$retry", nameof(IncidentNotificationDeliveryStatus.RetryScheduled));
            Add(command, "$processing", nameof(IncidentNotificationDeliveryStatus.Processing));
            Add(command, "$now", Utc(now));
            return command.ExecuteNonQuery();
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS system_instance_registry (
                    instance_id TEXT PRIMARY KEY,
                    host_name TEXT NOT NULL,
                    application_version TEXT NOT NULL,
                    is_draining INTEGER NOT NULL DEFAULT 0,
                    is_leader INTEGER NOT NULL DEFAULT 0,
                    leader_role TEXT NULL,
                    registered_utc TEXT NOT NULL,
                    last_heartbeat_utc TEXT NOT NULL,
                    lease_expires_utc TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS system_distributed_locks (
                    lock_name TEXT PRIMARY KEY,
                    owner_instance_id TEXT NOT NULL,
                    lock_token TEXT NOT NULL,
                    fencing_token INTEGER NOT NULL,
                    created_utc TEXT NOT NULL,
                    expires_utc TEXT NOT NULL,
                    last_renewed_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_system_distributed_locks_expires ON system_distributed_locks(expires_utc);
                CREATE TABLE IF NOT EXISTS system_leader_elections (
                    role TEXT PRIMARY KEY,
                    leader_instance_id TEXT NOT NULL,
                    fencing_token INTEGER NOT NULL,
                    elected_utc TEXT NOT NULL,
                    lease_expires_utc TEXT NOT NULL,
                    last_heartbeat_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS system_worker_registry (
                    worker_id TEXT PRIMARY KEY,
                    worker_name TEXT NOT NULL,
                    instance_id TEXT NOT NULL,
                    worker_type TEXT NOT NULL,
                    status TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    last_heartbeat_utc TEXT NOT NULL,
                    last_success_utc TEXT NULL,
                    last_failure_utc TEXT NULL,
                    restart_count INTEGER NOT NULL DEFAULT 0,
                    current_lease TEXT NULL,
                    last_error TEXT NULL,
                    recovery_enabled INTEGER NOT NULL DEFAULT 1,
                    recovery_eligible INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_system_worker_registry_instance ON system_worker_registry(instance_id);
                CREATE TABLE IF NOT EXISTS system_recovery_history (
                    id TEXT PRIMARY KEY,
                    worker_id TEXT NOT NULL,
                    worker_name TEXT NOT NULL,
                    instance_id TEXT NOT NULL,
                    triggering_incident_id TEXT NULL,
                    recovery_reason TEXT NOT NULL,
                    attempt_number INTEGER NOT NULL,
                    result TEXT NOT NULL,
                    correlation_id TEXT NOT NULL,
                    started_utc TEXT NOT NULL,
                    completed_utc TEXT NULL,
                    failure_reason TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_system_recovery_history_started ON system_recovery_history(started_utc DESC);
                CREATE TABLE IF NOT EXISTS system_recovery_policies (
                    worker_name TEXT PRIMARY KEY,
                    automatic_recovery_allowed INTEGER NOT NULL,
                    max_attempts INTEGER NOT NULL,
                    cooldown_seconds INTEGER NOT NULL,
                    notes TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            EnsureNotificationLeaseColumns(connection);
            SeedDefaultPolicies(connection);
        }
    }

    private static void EnsureNotificationLeaseColumns(SqliteConnection connection)
    {
        if (!TableExists(connection, "system_incident_notifications"))
        {
            return;
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(system_incident_notifications);";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (!existing.Contains("processing_owner"))
        {
            Exec(connection, "ALTER TABLE system_incident_notifications ADD COLUMN processing_owner TEXT NULL;");
        }

        if (!existing.Contains("processing_lease_expires_utc"))
        {
            Exec(connection, "ALTER TABLE system_incident_notifications ADD COLUMN processing_lease_expires_utc TEXT NULL;");
        }

        if (!existing.Contains("claim_token"))
        {
            Exec(connection, "ALTER TABLE system_incident_notifications ADD COLUMN claim_token TEXT NULL;");
        }

        if (!existing.Contains("claim_fencing_token"))
        {
            Exec(connection, "ALTER TABLE system_incident_notifications ADD COLUMN claim_fencing_token INTEGER NULL;");
        }

        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_notifications_processing_lease ON system_incident_notifications(delivery_status, processing_lease_expires_utc);");
    }

    private static void SeedDefaultPolicies(SqliteConnection connection)
    {
        foreach (var worker in new[]
                 {
                     MonitoredWorkerNames.SystemHealthMonitor,
                     "SystemIncidentEvaluationHostedService",
                     "SystemIncidentNotificationHostedService",
                 })
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO system_recovery_policies(worker_name,automatic_recovery_allowed,max_attempts,cooldown_seconds,notes)
                VALUES($name,1,3,120,$notes);
                """;
            Add(command, "$name", worker);
            Add(command, "$notes", "Infrastructure worker — safe housekeeping recovery only.");
            command.ExecuteNonQuery();
        }

        foreach (var worker in TradingCriticalWorkers.DenyAutomaticRecovery)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO system_recovery_policies(worker_name,automatic_recovery_allowed,max_attempts,cooldown_seconds,notes)
                VALUES($name,0,0,0,$notes);
                """;
            Add(command, "$name", worker);
            Add(command, "$notes", "Trading-critical — automatic recovery denied.");
            command.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        Add(command, "$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Called after notification schema is created so lease columns and indexes exist.</summary>
    public void EnsureNotificationLeaseSchemaIfPresent()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            EnsureNotificationLeaseColumns(connection);
        }
    }

    private static long NextFencingToken(SqliteConnection connection, SqliteTransaction transaction, string lockName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(fencing_token), 0) FROM system_distributed_locks WHERE lock_name = $name;";
        Add(command, "$name", lockName);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DistributedLockRecord? GetLockUnsafe(SqliteConnection connection, SqliteTransaction transaction, string lockName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM system_distributed_locks WHERE lock_name = $name LIMIT 1;";
        Add(command, "$name", lockName);
        return ReadLocks(command, DateTimeOffset.UtcNow).FirstOrDefault();
    }

    private static LeaderElectionRecord? GetLeaderUnsafe(SqliteConnection connection, SqliteTransaction transaction, string role)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM system_leader_elections WHERE role = $role LIMIT 1;";
        Add(command, "$role", role);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new LeaderElectionRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            ParseUtc(reader.GetString(3)),
            ParseUtc(reader.GetString(4)),
            ParseUtc(reader.GetString(5)));
    }

    private static void WriteInstance(SqliteCommand command, SystemInstanceRecord record)
    {
        Add(command, "$id", record.InstanceId);
        Add(command, "$host", record.HostName);
        Add(command, "$ver", record.ApplicationVersion);
        Add(command, "$drain", record.IsDraining ? 1 : 0);
        Add(command, "$leader", record.IsLeader ? 1 : 0);
        Add(command, "$role", record.LeaderRole);
        Add(command, "$reg", Utc(record.RegisteredUtc));
        Add(command, "$hb", Utc(record.LastHeartbeatUtc));
        Add(command, "$lease", record.LeaseExpiresUtc is null ? null : Utc(record.LeaseExpiresUtc.Value));
    }

    private static List<SystemInstanceRecord> ReadInstances(SqliteCommand command)
    {
        var list = new List<SystemInstanceRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemInstanceRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                ParseUtc(reader.GetString(6)),
                ParseUtc(reader.GetString(7)),
                reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8))));
        }

        return list;
    }

    private static List<DistributedLockRecord> ReadLocks(SqliteCommand command, DateTimeOffset now)
    {
        var list = new List<DistributedLockRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var expires = ParseUtc(reader.GetString(5));
            list.Add(new DistributedLockRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                ParseUtc(reader.GetString(4)),
                expires,
                ParseUtc(reader.GetString(6)),
                expires <= now));
        }

        return list;
    }

    private static void WriteWorker(SqliteCommand command, SystemWorkerRegistryRecord worker)
    {
        Add(command, "$id", worker.WorkerId);
        Add(command, "$name", worker.WorkerName);
        Add(command, "$inst", worker.InstanceId);
        Add(command, "$type", worker.WorkerType);
        Add(command, "$status", worker.Status.ToString());
        Add(command, "$start", Utc(worker.StartedUtc));
        Add(command, "$hb", Utc(worker.LastHeartbeatUtc));
        Add(command, "$ok", worker.LastSuccessfulExecutionUtc is null ? null : Utc(worker.LastSuccessfulExecutionUtc.Value));
        Add(command, "$fail", worker.LastFailureUtc is null ? null : Utc(worker.LastFailureUtc.Value));
        Add(command, "$restart", worker.RestartCount);
        Add(command, "$lease", worker.CurrentLease);
        Add(command, "$err", worker.LastError);
        Add(command, "$ren", worker.RecoveryEnabled ? 1 : 0);
        Add(command, "$rel", worker.RecoveryEligible ? 1 : 0);
    }

    private static List<SystemWorkerRegistryRecord> ReadWorkers(SqliteCommand command)
    {
        var list = new List<SystemWorkerRegistryRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemWorkerRegistryRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Enum.Parse<SystemWorkerRegistryStatus>(reader.GetString(4), ignoreCase: true),
                ParseUtc(reader.GetString(5)),
                ParseUtc(reader.GetString(6)),
                reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7)),
                reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8)),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetInt32(12) == 1,
                reader.GetInt32(13) == 1));
        }

        return list;
    }

    private static void WriteRecovery(SqliteCommand command, SystemRecoveryHistoryRecord record)
    {
        Add(command, "$id", record.Id);
        Add(command, "$wid", record.WorkerId);
        Add(command, "$name", record.WorkerName);
        Add(command, "$inst", record.InstanceId);
        Add(command, "$incident", record.TriggeringIncidentId);
        Add(command, "$reason", record.RecoveryReason);
        Add(command, "$attempt", record.AttemptNumber);
        Add(command, "$result", record.Result.ToString());
        Add(command, "$corr", record.CorrelationId);
        Add(command, "$start", Utc(record.StartedUtc));
        Add(command, "$done", record.CompletedUtc is null ? null : Utc(record.CompletedUtc.Value));
        Add(command, "$fail", record.FailureReason);
    }

    private static List<SystemRecoveryHistoryRecord> ReadRecovery(SqliteCommand command)
    {
        var list = new List<SystemRecoveryHistoryRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemRecoveryHistoryRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                Enum.Parse<SystemRecoveryResult>(reader.GetString(7), ignoreCase: true),
                reader.GetString(8),
                ParseUtc(reader.GetString(9)),
                reader.IsDBNull(10) ? null : ParseUtc(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return list;
    }

    private static List<SystemRecoveryPolicyRecord> ReadPolicies(SqliteCommand command)
    {
        var list = new List<SystemRecoveryPolicyRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SystemRecoveryPolicyRecord(
                reader.GetString(0),
                reader.GetInt32(1) == 1,
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4)));
        }

        return list;
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Utc(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}