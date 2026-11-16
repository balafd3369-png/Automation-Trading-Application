using System.Collections.Concurrent;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>In-process worker heartbeat and meaningful-operation tracking.</summary>
public sealed class WorkerHeartbeatRegistry
{
    private readonly string _instanceId;
    private readonly ConcurrentDictionary<string, WorkerHeartbeatState> _workers = new(StringComparer.Ordinal);

    public WorkerHeartbeatRegistry(SystemMonitoringOptions options)
    {
        _instanceId = string.IsNullOrWhiteSpace(options.InstanceId)
            ? $"{Environment.MachineName}:{Environment.ProcessId}"
            : options.InstanceId.Trim();
    }

    public string InstanceId => _instanceId;

    public void Register(string workerName)
    {
        var now = DateTimeOffset.UtcNow;
        _workers.GetOrAdd(
            workerName,
            _ => new WorkerHeartbeatState(workerName, _instanceId, WorkerRunStatus.Starting, now, now));
    }

    public void Pulse(
        string workerName,
        WorkerRunStatus status,
        string? lastError = null,
        bool markSuccessfulExecution = false)
    {
        var now = DateTimeOffset.UtcNow;
        _workers.AddOrUpdate(
            workerName,
            _ => CreateInitial(workerName, _instanceId, status, now, lastError, markSuccessfulExecution),
            (_, existing) => Update(existing, status, now, lastError, markSuccessfulExecution));
    }

    public IReadOnlyList<WorkerHeartbeatSnapshot> SnapshotAll(int staleAfterSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, staleAfterSeconds));
        return _workers.Values
            .OrderBy(w => w.WorkerName, StringComparer.Ordinal)
            .Select(w => w.ToLegacySnapshot(now, staleAfter))
            .ToList();
    }

    public IReadOnlyList<WorkerHealthDetailSnapshot> SnapshotDetailed(
        SystemMonitoringOptions options,
        bool liveTradingEnabled,
        bool brokerConnected,
        bool marketIntradayOpen)
    {
        var now = DateTimeOffset.UtcNow;
        return _workers.Values
            .OrderBy(w => w.WorkerName, StringComparer.Ordinal)
            .Select(w =>
            {
                var progressExpected = WorkerHealthEvaluator.ExpectsProgress(
                    w.WorkerName,
                    liveTradingEnabled,
                    brokerConnected,
                    marketIntradayOpen);
                return WorkerHealthEvaluator.Evaluate(w, options, now, progressExpected);
            })
            .ToList();
    }

    internal IReadOnlyList<WorkerHeartbeatState> States =>
        _workers.Values.OrderBy(w => w.WorkerName, StringComparer.Ordinal).ToList();

    private static WorkerHeartbeatState CreateInitial(
        string workerName,
        string instanceId,
        WorkerRunStatus status,
        DateTimeOffset now,
        string? lastError,
        bool markSuccessfulExecution)
    {
        var state = new WorkerHeartbeatState(workerName, instanceId, status, now, now);
        ApplyUpdate(state, status, now, lastError, markSuccessfulExecution);
        return state;
    }

    private static WorkerHeartbeatState Update(
        WorkerHeartbeatState existing,
        WorkerRunStatus status,
        DateTimeOffset now,
        string? lastError,
        bool markSuccessfulExecution)
    {
        ApplyUpdate(existing, status, now, lastError, markSuccessfulExecution);
        return existing;
    }

    private static void ApplyUpdate(
        WorkerHeartbeatState existing,
        WorkerRunStatus status,
        DateTimeOffset now,
        string? lastError,
        bool markSuccessfulExecution)
    {
        if (existing.Status != status)
        {
            existing.Status = status;
            existing.LastStateChange = now;
        }

        existing.LastHeartbeat = now;
        if (markSuccessfulExecution)
        {
            existing.LastSuccessfulExecution = now;
            existing.ConsecutiveFailureCount = 0;
            existing.LastError = null;
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            existing.LastError = lastError;
            existing.LastFailedOperation = now;
            existing.ConsecutiveFailureCount++;
            existing.TotalFailureCount++;
        }
    }

    public sealed class WorkerHeartbeatState
    {
        public WorkerHeartbeatState(
            string workerName,
            string instanceId,
            WorkerRunStatus status,
            DateTimeOffset heartbeat,
            DateTimeOffset stateChange)
        {
            WorkerName = workerName;
            InstanceId = instanceId;
            Status = status;
            LastHeartbeat = heartbeat;
            LastStateChange = stateChange;
        }

        public string WorkerName { get; }
        public string InstanceId { get; }
        public WorkerRunStatus Status { get; set; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public DateTimeOffset? LastSuccessfulExecution { get; set; }
        public DateTimeOffset? LastFailedOperation { get; set; }
        public string? LastError { get; set; }
        public int ConsecutiveFailureCount { get; set; }
        public int TotalFailureCount { get; set; }
        public DateTimeOffset? LastStateChange { get; set; }

        public WorkerHeartbeatSnapshot ToLegacySnapshot(DateTimeOffset now, TimeSpan staleAfter)
        {
            var isStale = now - LastHeartbeat > staleAfter;
            var status = Status;
            if (isStale && status is WorkerRunStatus.Running or WorkerRunStatus.Starting or WorkerRunStatus.Degraded)
            {
                status = WorkerRunStatus.Stopped;
            }

            return new WorkerHeartbeatSnapshot(
                WorkerName,
                InstanceId,
                status,
                LastHeartbeat,
                LastSuccessfulExecution,
                LastError,
                ConsecutiveFailureCount,
                isStale);
        }
    }
}