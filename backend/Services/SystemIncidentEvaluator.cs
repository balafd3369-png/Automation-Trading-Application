using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentEvaluator
{
    private readonly SystemMonitoringOptions _options;
    private readonly SystemIncidentDeduplicationService _dedup;

    public SystemIncidentEvaluator(
        SystemMonitoringOptions options,
        SystemIncidentDeduplicationService dedup)
    {
        _options = options;
        _dedup = dedup;
    }

    public IReadOnlyList<IncidentEvaluationCandidate> Evaluate(SystemHealthService health)
    {
        var intraday = NseCashSession.IsLiveIntradayWindowNow();
        var tradingDay = NseCashSession.IsTradingDay(NseCashSession.IstToday());
        var candidates = new List<IncidentEvaluationCandidate>();

        EvaluateDatabase(health, candidates);
        EvaluateRuntime(health, candidates);
        EvaluateWorkers(health, candidates);
        EvaluateBroker(health, candidates, intraday && tradingDay);
        EvaluateMarketData(health, candidates, intraday && tradingDay);
        EvaluateSignalR(health, candidates);

        return candidates;
    }

    private void EvaluateDatabase(SystemHealthService health, List<IncidentEvaluationCandidate> candidates)
    {
        var live = health.GetLiveSnapshot();
        var db = live.Database;
        if (!db.CanConnect || !db.CanWrite)
        {
            var category = !db.CanConnect
                ? SystemIncidentCategory.DatabaseUnavailable
                : SystemIncidentCategory.DatabaseWriteFailure;
            var severity = SystemIncidentSeverity.Critical;
            var fingerprint = _dedup.BuildFingerprint(
                SystemIncidentComponents.Database,
                category.ToString(),
                "primary");
            candidates.Add(new IncidentEvaluationCandidate(
                fingerprint,
                "Database health probe failed",
                db.Error ?? "Database probe failed.",
                SystemIncidentComponents.Database,
                "primary",
                category,
                severity,
                db.Error,
                1,
                false,
                null));
        }
        else
        {
            candidates.Add(HealthyCandidate(
                SystemIncidentComponents.Database,
                SystemIncidentCategory.DatabaseUnavailable,
                "primary"));
        }
    }

    private void EvaluateRuntime(SystemHealthService health, List<IncidentEvaluationCandidate> candidates)
    {
        var live = health.GetLiveSnapshot();
        var count = live.Runtime.UnobservedTaskExceptionCount;
        if (count > 0)
        {
            var fingerprint = _dedup.BuildFingerprint(
                SystemIncidentComponents.Runtime,
                SystemIncidentCategory.RuntimeUnobservedExceptions.ToString(),
                "process");
            candidates.Add(new IncidentEvaluationCandidate(
                fingerprint,
                "Unobserved task exceptions detected",
                $"Unobserved task exception count is {count}.",
                SystemIncidentComponents.Runtime,
                "process",
                SystemIncidentCategory.RuntimeUnobservedExceptions,
                count >= 5 ? SystemIncidentSeverity.High : SystemIncidentSeverity.Medium,
                $"{count} unobserved task exceptions",
                count,
                false,
                null));
        }
        else
        {
            candidates.Add(HealthyCandidate(
                SystemIncidentComponents.Runtime,
                SystemIncidentCategory.RuntimeUnobservedExceptions,
                "process"));
        }
    }

    private void EvaluateWorkers(SystemHealthService health, List<IncidentEvaluationCandidate> candidates)
    {
        foreach (var worker in health.GetWorkerHealth())
        {
            if (!worker.ProgressExpected
                && worker.CurrentState is ComponentHealthState.Healthy or ComponentHealthState.Unknown)
            {
                continue;
            }

            var (unhealthy, category, severity, title) = worker.CurrentState switch
            {
                ComponentHealthState.Stopped => (true, SystemIncidentCategory.WorkerStopped, SystemIncidentSeverity.High, "Worker stopped"),
                ComponentHealthState.Unresponsive => (true, SystemIncidentCategory.WorkerUnresponsive, SystemIncidentSeverity.High, "Worker unresponsive"),
                ComponentHealthState.Stalled => (true, SystemIncidentCategory.WorkerStalled, SystemIncidentSeverity.Medium, "Worker stalled"),
                ComponentHealthState.Degraded => (true, SystemIncidentCategory.WorkerDegraded, SystemIncidentSeverity.Medium, "Worker degraded"),
                _ when worker.IsHeartbeatStale => (true, SystemIncidentCategory.WorkerHeartbeatTimeout, SystemIncidentSeverity.High, "Worker heartbeat timeout"),
                _ => (false, SystemIncidentCategory.WorkerDegraded, SystemIncidentSeverity.Low, "Worker"),
            };

            var fingerprint = _dedup.BuildFingerprint(
                SystemIncidentComponents.Workers,
                category.ToString(),
                worker.WorkerName);
            if (unhealthy && MeetsThreshold(worker.ConsecutiveFailureCount))
            {
                candidates.Add(new IncidentEvaluationCandidate(
                    fingerprint,
                    $"{title}: {worker.WorkerName}",
                    worker.LastError ?? $"{worker.WorkerName} reported state {worker.CurrentState}.",
                    SystemIncidentComponents.Workers,
                    worker.WorkerName,
                    category,
                    severity,
                    worker.LastError,
                    Math.Max(worker.ConsecutiveFailureCount, 1),
                    false,
                    null));
            }
            else
            {
                foreach (var workerCategory in WorkerIncidentCategories)
                {
                    candidates.Add(HealthyCandidate(
                        SystemIncidentComponents.Workers,
                        workerCategory,
                        worker.WorkerName));
                }
            }
        }
    }

    private static readonly SystemIncidentCategory[] WorkerIncidentCategories =
    [
        SystemIncidentCategory.WorkerStopped,
        SystemIncidentCategory.WorkerUnresponsive,
        SystemIncidentCategory.WorkerStalled,
        SystemIncidentCategory.WorkerDegraded,
        SystemIncidentCategory.WorkerHeartbeatTimeout,
    ];

    private void EvaluateBroker(
        SystemHealthService health,
        List<IncidentEvaluationCandidate> candidates,
        bool activeTradingContext)
    {
        var broker = health.GetBrokerHealth();
        foreach (var connection in broker.Connections)
        {
            var identifier = connection.BrokerId;
            if (!connection.SessionPresent)
            {
                if (!activeTradingContext)
                {
                    candidates.Add(HealthyCandidate(
                        SystemIncidentComponents.Broker,
                        SystemIncidentCategory.BrokerDisconnected,
                        identifier));
                    continue;
                }

                var category = connection.LastError?.StartsWith("AUTH:", StringComparison.OrdinalIgnoreCase) == true
                    ? SystemIncidentCategory.BrokerAuthFailure
                    : SystemIncidentCategory.BrokerDisconnected;
                var fingerprint = _dedup.BuildFingerprint(
                    SystemIncidentComponents.Broker,
                    category.ToString(),
                    identifier);
                candidates.Add(new IncidentEvaluationCandidate(
                    fingerprint,
                    category == SystemIncidentCategory.BrokerAuthFailure
                        ? $"Broker authentication failure: {connection.DisplayName}"
                        : $"Broker disconnected: {connection.DisplayName}",
                    connection.LastError ?? "No active broker session.",
                    SystemIncidentComponents.Broker,
                    identifier,
                    category,
                    SystemIncidentSeverity.High,
                    connection.LastError,
                    1,
                    false,
                    null));
                continue;
            }

            if (!connection.CommunicationVerified)
            {
                var category = connection.LastError?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true
                    ? SystemIncidentCategory.BrokerCommunicationTimeout
                    : SystemIncidentCategory.BrokerApiFailures;
                var severity = broker.ConsecutiveFailures >= _options.BrokerFailureThreshold
                    ? SystemIncidentSeverity.High
                    : SystemIncidentSeverity.Medium;
                if (!MeetsThreshold(broker.ConsecutiveFailures))
                {
                    continue;
                }

                var fingerprint = _dedup.BuildFingerprint(
                    SystemIncidentComponents.Broker,
                    category.ToString(),
                    identifier);
                candidates.Add(new IncidentEvaluationCandidate(
                    fingerprint,
                    $"Broker communication issue: {connection.DisplayName}",
                    connection.LastError ?? broker.LastError ?? "Broker quote probe failed.",
                    SystemIncidentComponents.Broker,
                    identifier,
                    category,
                    severity,
                    connection.LastError ?? broker.LastError,
                    broker.ConsecutiveFailures,
                    false,
                    null));
            }
            else
            {
                candidates.Add(HealthyCandidate(
                    SystemIncidentComponents.Broker,
                    SystemIncidentCategory.BrokerDisconnected,
                    identifier));
            }
        }
    }

    private void EvaluateMarketData(
        SystemHealthService health,
        List<IncidentEvaluationCandidate> candidates,
        bool updatesExpected)
    {
        var market = health.GetMarketDataHealth();
        foreach (var instrument in market.Instruments)
        {
            if (!instrument.UpdatesExpected || !updatesExpected)
            {
                candidates.Add(HealthyCandidate(
                    SystemIncidentComponents.MarketData,
                    SystemIncidentCategory.MarketDataStale,
                    instrument.Symbol));
                continue;
            }

            if (instrument.IsStale)
            {
                var fingerprint = _dedup.BuildFingerprint(
                    SystemIncidentComponents.MarketData,
                    SystemIncidentCategory.MarketDataStale.ToString(),
                    instrument.Symbol);
                candidates.Add(new IncidentEvaluationCandidate(
                    fingerprint,
                    $"Stale market data: {instrument.Symbol}",
                    $"Instrument {instrument.Symbol} has stale board prices for {instrument.StaleDurationSeconds}s.",
                    SystemIncidentComponents.MarketData,
                    instrument.Symbol,
                    SystemIncidentCategory.MarketDataStale,
                    SystemIncidentSeverity.High,
                    market.LastError,
                    market.ConsecutiveProbeFailures,
                    false,
                    null));
            }
            else
            {
                candidates.Add(HealthyCandidate(
                    SystemIncidentComponents.MarketData,
                    SystemIncidentCategory.MarketDataStale,
                    instrument.Symbol));
            }
        }

        if (updatesExpected && market.State is ComponentHealthState.Degraded or ComponentHealthState.Stalled)
        {
            var fp = _dedup.BuildFingerprint(
                SystemIncidentComponents.MarketData,
                SystemIncidentCategory.MarketDataFeedDisconnected.ToString(),
                "feed");
            candidates.Add(new IncidentEvaluationCandidate(
                fp,
                "Market data feed degraded",
                market.LastError ?? "Market data probe reported degraded feed.",
                SystemIncidentComponents.MarketData,
                "feed",
                SystemIncidentCategory.MarketDataFeedDisconnected,
                SystemIncidentSeverity.High,
                market.LastError,
                market.ConsecutiveProbeFailures,
                false,
                null));
        }
        else
        {
            candidates.Add(HealthyCandidate(
                SystemIncidentComponents.MarketData,
                SystemIncidentCategory.MarketDataFeedDisconnected,
                "feed"));
        }
    }

    private void EvaluateSignalR(SystemHealthService health, List<IncidentEvaluationCandidate> candidates)
    {
        var signalR = health.GetSignalRHealth();
        var fingerprint = _dedup.BuildFingerprint(
            SystemIncidentComponents.SignalR,
            SystemIncidentCategory.SignalRUnavailable.ToString(),
            "market-chart-hub");
        if (signalR.State is ComponentHealthState.Degraded or ComponentHealthState.Stopped or ComponentHealthState.Unresponsive)
        {
            candidates.Add(new IncidentEvaluationCandidate(
                fingerprint,
                "SignalR hub degradation",
                signalR.LastError ?? signalR.Note,
                SystemIncidentComponents.SignalR,
                "market-chart-hub",
                SystemIncidentCategory.SignalRDegradation,
                signalR.ConnectionFailureCount >= 3 ? SystemIncidentSeverity.Medium : SystemIncidentSeverity.Low,
                signalR.LastError,
                signalR.ConnectionFailureCount,
                false,
                null));
        }
        else
        {
            candidates.Add(HealthyCandidate(
                SystemIncidentComponents.SignalR,
                SystemIncidentCategory.SignalRUnavailable,
                "market-chart-hub"));
        }
    }

    private bool MeetsThreshold(int sourceCount) =>
        Math.Max(sourceCount, 1) >= _options.IncidentMinimumOccurrenceThreshold;

    private IncidentEvaluationCandidate HealthyCandidate(
        string component,
        SystemIncidentCategory category,
        string identifier) =>
        new(
            _dedup.BuildFingerprint(component, category.ToString(), identifier),
            string.Empty,
            string.Empty,
            component,
            identifier,
            category,
            SystemIncidentSeverity.Info,
            null,
            0,
            true,
            null);
}