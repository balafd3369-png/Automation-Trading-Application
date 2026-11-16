using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemHealthService
{
    private readonly SystemMonitoringOptions _options;
    private readonly LiveExecutionOptions _live;
    private readonly BrokerConnectionManager _brokers;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly SystemHealthStore _store;
    private readonly SystemHealthExceptionTracker _exceptions;
    private readonly SystemHealthInfrastructureProbes _probes;
    private readonly DateTimeOffset _processStartedAt = DateTimeOffset.UtcNow;
    private readonly object _snapshotGate = new();
    private DatabaseHealthSnapshot _lastDatabase = new(false, false, false, "Not probed yet.", DateTimeOffset.MinValue, 0);

    public SystemHealthService(
        SystemMonitoringOptions options,
        LiveExecutionOptions live,
        BrokerConnectionManager brokers,
        WorkerHeartbeatRegistry heartbeats,
        SystemHealthStore store,
        SystemHealthExceptionTracker exceptions,
        SystemHealthInfrastructureProbes probes)
    {
        _options = options;
        _live = live;
        _brokers = brokers;
        _heartbeats = heartbeats;
        _store = store;
        _exceptions = exceptions;
        _probes = probes;
    }

    internal void UpdateDatabaseSnapshot(DatabaseHealthSnapshot snapshot)
    {
        lock (_snapshotGate)
        {
            _lastDatabase = snapshot;
        }
    }

    public DatabaseHealthSnapshot ProbeDatabaseNow() =>
        UpdateDatabaseFromProbe(_store.ProbeDatabase());

    internal DatabaseHealthSnapshot UpdateDatabaseFromProbe(DatabaseHealthSnapshot snapshot)
    {
        lock (_snapshotGate)
        {
            _lastDatabase = snapshot;
        }

        return snapshot;
    }

    public SystemHealthLiveSnapshot GetLiveSnapshot()
    {
        DatabaseHealthSnapshot database;
        lock (_snapshotGate)
        {
            database = _lastDatabase;
        }

        var workers = _heartbeats.SnapshotAll(_options.EffectiveWorkerHeartbeatTimeoutSeconds);
        var runtime = BuildRuntimeSnapshot();
        var overall = ResolveOverall(database, workers, _probes.LastBroker, _probes.LastMarket, _probes.LastSignalR);
        return new SystemHealthLiveSnapshot(
            overall,
            _heartbeats.InstanceId,
            "Healthy",
            database,
            workers,
            runtime,
            _store.LastComprehensiveReportAt,
            _store.LastComprehensiveReportId);
    }

    public BrokerConnectivityHealthSnapshot GetBrokerHealth() => _probes.LastBroker;

    public MarketDataHealthSnapshot GetMarketDataHealth() => _probes.LastMarket;

    public SignalRServerHealthSnapshot GetSignalRHealth() => _probes.LastSignalR;

    public IReadOnlyList<WorkerHealthDetailSnapshot> GetWorkerHealth() =>
        _heartbeats.SnapshotDetailed(
            _options,
            _live.Enabled,
            _brokers.HasSession,
            NseCashSession.IsLiveIntradayWindowNow());

    public IReadOnlyList<SystemHealthComponentSummary> GetComponentSummaries()
    {
        DatabaseHealthSnapshot database;
        lock (_snapshotGate)
        {
            database = _lastDatabase;
        }

        var broker = _probes.LastBroker;
        var market = _probes.LastMarket;
        var signalR = _probes.LastSignalR;
        var workers = GetWorkerHealth();
        var workerWorst = ComponentHealthState.Healthy;
        if (workers.Count == 0)
        {
            workerWorst = ComponentHealthState.Unknown;
        }
        else
        {
            foreach (var worker in workers)
            {
                if ((int)worker.CurrentState > (int)workerWorst)
                {
                    workerWorst = worker.CurrentState;
                }
            }
        }

        return
        [
            ToSummary(SystemHealthComponents.Database, database.CanConnect && database.CanWrite ? ComponentHealthState.Healthy : ComponentHealthState.Degraded, database.CheckedAt, database.Error, database),
            ToSummary(SystemHealthComponents.Broker, broker.State, broker.CheckedAt, broker.LastError, broker),
            ToSummary(SystemHealthComponents.MarketData, market.State, market.CheckedAt, market.LastError, market),
            ToSummary(SystemHealthComponents.SignalR, signalR.State, signalR.CheckedAt, signalR.LastError, signalR),
            ToSummary(SystemHealthComponents.Workers, workerWorst, DateTimeOffset.UtcNow, null, workers),
            ToSummary(SystemHealthComponents.Runtime, ComponentHealthState.Healthy, DateTimeOffset.UtcNow, null, BuildRuntimeSnapshot()),
        ];
    }

    public SystemHealthComponentSummary? GetComponentSummary(string component)
    {
        return GetComponentSummaries()
            .FirstOrDefault(c => string.Equals(c.Component, component, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BrokerConnectivityHealthSnapshot> ProbeBrokerNowAsync(CancellationToken ct) =>
        await _probes.ProbeBrokerAsync(ct);

    public async Task<MarketDataHealthSnapshot> ProbeMarketDataNowAsync(CancellationToken ct) =>
        await _probes.ProbeMarketDataAsync(ct);

    public async Task<SignalRServerHealthSnapshot> ProbeSignalRNowAsync(CancellationToken ct) =>
        await _probes.ProbeSignalRAsync(ct);

    public SystemHealthComprehensiveReport BuildComprehensiveReport()
    {
        var database = _store.ProbeDatabase();
        UpdateDatabaseFromProbe(database);
        return BuildComprehensiveFromCurrent(database);
    }

    public async Task<SystemHealthComprehensiveReport> BuildComprehensiveReportAsync(CancellationToken ct)
    {
        var database = _store.ProbeDatabase();
        UpdateDatabaseFromProbe(database);
        if (_options.BrokerMonitoringEnabled)
        {
            await _probes.ProbeBrokerAsync(ct);
        }

        if (_options.MarketDataMonitoringEnabled)
        {
            await _probes.ProbeMarketDataAsync(ct);
        }

        if (_options.SignalRMonitoringEnabled)
        {
            await _probes.ProbeSignalRAsync(ct);
        }

        return BuildComprehensiveFromCurrent(database);
    }

    public SystemHealthComprehensiveReport RunAndPersistComprehensiveReport()
    {
        var report = BuildComprehensiveReport();
        _store.SaveComprehensiveReport(report);
        return report;
    }

    public async Task<SystemHealthComprehensiveReport> RunAndPersistComprehensiveReportAsync(CancellationToken ct)
    {
        var report = await BuildComprehensiveReportAsync(ct);
        _store.SaveComprehensiveReport(report);
        return report;
    }

    public IReadOnlyList<SystemHealthReportEntry> ListHistoricalReports(int limit = 50) =>
        _store.ListReports("comprehensive", limit);

    public SystemHealthReportEntry? GetHistoricalReport(string id) => _store.GetReport(id);

    private SystemHealthComprehensiveReport BuildComprehensiveFromCurrent(DatabaseHealthSnapshot database)
    {
        var workers = _heartbeats.SnapshotAll(_options.EffectiveWorkerHeartbeatTimeoutSeconds);
        var workerDetails = GetWorkerHealth();
        var runtime = BuildRuntimeSnapshot();
        var broker = _probes.LastBroker;
        var market = _probes.LastMarket;
        var signalR = _probes.LastSignalR;
        var findings = BuildFindings(database, workers, workerDetails, broker, market, signalR);
        var overall = ResolveOverall(database, workers, broker, market, signalR);
        var probes = BuildProbeAudit(database, broker, market, signalR);
        return new SystemHealthComprehensiveReport(
            overall,
            _heartbeats.InstanceId,
            database,
            workers,
            runtime,
            findings,
            DateTimeOffset.UtcNow,
            probes,
            broker,
            market,
            signalR,
            workerDetails);
    }

    private ApplicationRuntimeSnapshot BuildRuntimeSnapshot()
    {
        ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.GetMaxThreads(out var maxWorker, out _);
        var pending = Math.Max(0, maxWorker - workerThreads);
        return new ApplicationRuntimeSnapshot(
            Environment.WorkingSet,
            GC.GetTotalMemory(false),
            workerThreads,
            completionPortThreads,
            pending,
            _exceptions.UnobservedTaskExceptionCount,
            _processStartedAt);
    }

    private static SystemHealthOverallStatus ResolveOverall(
        DatabaseHealthSnapshot database,
        IReadOnlyList<WorkerHeartbeatSnapshot> workers,
        BrokerConnectivityHealthSnapshot broker,
        MarketDataHealthSnapshot market,
        SignalRServerHealthSnapshot signalR)
    {
        if (!database.CanConnect)
        {
            return SystemHealthOverallStatus.Critical;
        }

        if (workers.Any(w => w.Status == WorkerRunStatus.Stopped || w.IsStale))
        {
            return SystemHealthOverallStatus.Critical;
        }

        if (!database.CanWrite
            || workers.Any(w => w.Status == WorkerRunStatus.Degraded)
            || broker.State is ComponentHealthState.Degraded or ComponentHealthState.Stalled
            || market.State is ComponentHealthState.Degraded or ComponentHealthState.Stalled
            || signalR.State is ComponentHealthState.Degraded)
        {
            return SystemHealthOverallStatus.Degraded;
        }

        if (workers.Count == 0)
        {
            return SystemHealthOverallStatus.Unknown;
        }

        return SystemHealthOverallStatus.Healthy;
    }

    private static List<string> BuildFindings(
        DatabaseHealthSnapshot database,
        IReadOnlyList<WorkerHeartbeatSnapshot> workers,
        IReadOnlyList<WorkerHealthDetailSnapshot> workerDetails,
        BrokerConnectivityHealthSnapshot broker,
        MarketDataHealthSnapshot market,
        SignalRServerHealthSnapshot signalR)
    {
        var findings = new List<string>();
        if (!database.CanConnect)
        {
            findings.Add($"Database unreachable: {database.Error}");
        }
        else if (!database.CanWrite)
        {
            findings.Add($"Database write probe failed: {database.Error}");
        }

        foreach (var worker in workers.Where(w => w.IsStale || w.Status == WorkerRunStatus.Stopped))
        {
            findings.Add($"Worker {worker.WorkerName} heartbeat stale or stopped.");
        }

        foreach (var worker in workerDetails.Where(w =>
                     w.CurrentState is ComponentHealthState.Stalled or ComponentHealthState.Unresponsive))
        {
            findings.Add($"Worker {worker.WorkerName} state={worker.CurrentState}.");
        }

        foreach (var worker in workers.Where(w => w.ConsecutiveFailureCount > 0))
        {
            findings.Add(
                $"Worker {worker.WorkerName} consecutive failures: {worker.ConsecutiveFailureCount} ({worker.LastError}).");
        }

        if (broker.LastError is not null)
        {
            findings.Add($"Broker: {broker.LastError}");
        }

        if (market.LastError is not null)
        {
            findings.Add($"Market data: {market.LastError}");
        }

        if (signalR.LastError is not null)
        {
            findings.Add($"SignalR: {signalR.LastError}");
        }

        if (findings.Count == 0)
        {
            findings.Add("No critical findings in comprehensive verification.");
        }

        return findings;
    }

    private static List<ComponentHealthProbeResult> BuildProbeAudit(
        DatabaseHealthSnapshot database,
        BrokerConnectivityHealthSnapshot broker,
        MarketDataHealthSnapshot market,
        SignalRServerHealthSnapshot signalR)
    {
        return
        [
            new(
                SystemHealthComponents.Database,
                "sqlite-read-write",
                database.CanConnect && database.CanWrite ? ComponentHealthState.Healthy : ComponentHealthState.Degraded,
                database.CheckedAt,
                database.LatencyMs,
                database.Error,
                null),
            new(
                SystemHealthComponents.Broker,
                "broker-connectivity",
                broker.State,
                broker.CheckedAt,
                broker.ProbeDurationMs,
                broker.LastError,
                null),
            new(
                SystemHealthComponents.MarketData,
                "market-feed-freshness",
                market.State,
                market.CheckedAt,
                market.ProbeDurationMs,
                market.LastError,
                market.FeedSource),
            new(
                SystemHealthComponents.SignalR,
                "signalr-server",
                signalR.State,
                signalR.CheckedAt,
                signalR.ProbeDurationMs,
                signalR.LastError,
                signalR.Note),
        ];
    }

    private static SystemHealthComponentSummary ToSummary(
        string component,
        ComponentHealthState status,
        DateTimeOffset? checkedAt,
        string? lastError,
        object? metrics)
    {
        return new SystemHealthComponentSummary(
            component,
            status,
            status == ComponentHealthState.Healthy ? checkedAt ?? DateTimeOffset.UtcNow : null,
            string.IsNullOrWhiteSpace(lastError) ? null : checkedAt,
            string.IsNullOrWhiteSpace(lastError) ? 0 : 1,
            lastError,
            checkedAt ?? DateTimeOffset.UtcNow,
            metrics);
    }
}