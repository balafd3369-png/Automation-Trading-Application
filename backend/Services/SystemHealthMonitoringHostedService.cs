using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Scheduled database, infrastructure, and comprehensive health verification.</summary>
public sealed class SystemHealthMonitoringHostedService : IHostedService, IDisposable
{
    private readonly SystemMonitoringOptions _options;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly SystemHealthService _health;
    private readonly SystemHealthInfrastructureProbes _probes;
    private readonly ILogger<SystemHealthMonitoringHostedService> _log;
    private Timer? _databaseTimer;
    private Timer? _comprehensiveTimer;
    private Timer? _heartbeatTimer;
    private Timer? _brokerTimer;
    private Timer? _marketTimer;
    private Timer? _signalRTimer;

    public SystemHealthMonitoringHostedService(
        SystemMonitoringOptions options,
        WorkerHeartbeatRegistry heartbeats,
        SystemHealthService health,
        SystemHealthInfrastructureProbes probes,
        ILogger<SystemHealthMonitoringHostedService> log)
    {
        _options = options;
        _heartbeats = heartbeats;
        _health = health;
        _probes = probes;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RegisterWorkers();
        _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);

        if (!_options.Enabled)
        {
            _log.LogInformation("System health monitoring is disabled via configuration.");
            return Task.CompletedTask;
        }

        _options.ClampIntervals();
        _databaseTimer = new Timer(_ => RunDatabaseProbe(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(_options.DatabaseCheckSeconds));
        _comprehensiveTimer = new Timer(_ => _ = RunComprehensiveAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(_options.ComprehensiveCheckSeconds));
        _heartbeatTimer = new Timer(_ => RunHeartbeatSelfCheck(), null, TimeSpan.FromSeconds(_options.WorkerHeartbeatCheckSeconds), TimeSpan.FromSeconds(_options.WorkerHeartbeatCheckSeconds));

        if (_options.BrokerMonitoringEnabled)
        {
            _brokerTimer = new Timer(
                _ => _ = RunBrokerProbeAsync(),
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(_options.BrokerProbeIntervalSeconds));
        }

        if (_options.MarketDataMonitoringEnabled)
        {
            _marketTimer = new Timer(
                _ => _ = RunMarketProbeAsync(),
                null,
                TimeSpan.FromSeconds(6),
                TimeSpan.FromSeconds(_options.MarketDataProbeIntervalSeconds));
        }

        if (_options.SignalRMonitoringEnabled)
        {
            _signalRTimer = new Timer(
                _ => _ = RunSignalRProbeAsync(),
                null,
                TimeSpan.FromSeconds(7),
                TimeSpan.FromSeconds(_options.SignalRProbeIntervalSeconds));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _databaseTimer?.Change(Timeout.Infinite, 0);
        _comprehensiveTimer?.Change(Timeout.Infinite, 0);
        _heartbeatTimer?.Change(Timeout.Infinite, 0);
        _brokerTimer?.Change(Timeout.Infinite, 0);
        _marketTimer?.Change(Timeout.Infinite, 0);
        _signalRTimer?.Change(Timeout.Infinite, 0);
        _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Stopped);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _databaseTimer?.Dispose();
        _comprehensiveTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _brokerTimer?.Dispose();
        _marketTimer?.Dispose();
        _signalRTimer?.Dispose();
    }

    private void RegisterWorkers()
    {
        _heartbeats.Register(MonitoredWorkerNames.PaperTradingAgent);
        _heartbeats.Register(MonitoredWorkerNames.LiveExecutionCoordinator);
        _heartbeats.Register(MonitoredWorkerNames.BrokerReconciliation);
        _heartbeats.Register(MonitoredWorkerNames.MockMarketService);
        _heartbeats.Register(MonitoredWorkerNames.SystemHealthMonitor);
    }

    private void RunDatabaseProbe()
    {
        try
        {
            var snapshot = _health.ProbeDatabaseNow();
            _heartbeats.Pulse(
                MonitoredWorkerNames.SystemHealthMonitor,
                snapshot.CanConnect ? WorkerRunStatus.Running : WorkerRunStatus.Degraded,
                snapshot.Error,
                markSuccessfulExecution: snapshot.CanConnect && snapshot.CanWrite);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Scheduled database health probe failed.");
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Degraded, ex.Message);
        }
    }

    private async Task RunBrokerProbeAsync()
    {
        try
        {
            await _probes.ProbeBrokerAsync(CancellationToken.None);
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Scheduled broker health probe failed.");
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Degraded, ex.Message);
        }
    }

    private async Task RunMarketProbeAsync()
    {
        try
        {
            await _probes.ProbeMarketDataAsync(CancellationToken.None);
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Scheduled market data health probe failed.");
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Degraded, ex.Message);
        }
    }

    private async Task RunSignalRProbeAsync()
    {
        try
        {
            await _probes.ProbeSignalRAsync(CancellationToken.None);
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Scheduled SignalR health probe failed.");
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Degraded, ex.Message);
        }
    }

    private async Task RunComprehensiveAsync()
    {
        try
        {
            var report = await _health.RunAndPersistComprehensiveReportAsync(CancellationToken.None);
            _heartbeats.Pulse(
                MonitoredWorkerNames.SystemHealthMonitor,
                report.OverallStatus == SystemHealthOverallStatus.Critical
                    ? WorkerRunStatus.Degraded
                    : WorkerRunStatus.Running,
                markSuccessfulExecution: true);
            if (report.OverallStatus != SystemHealthOverallStatus.Healthy)
            {
                _log.LogWarning(
                    "Comprehensive health status {Status}: {Findings}",
                    report.OverallStatus,
                    string.Join("; ", report.Findings));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Comprehensive health verification failed.");
            _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Degraded, ex.Message);
        }
    }

    private void RunHeartbeatSelfCheck()
    {
        _heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);
    }
}