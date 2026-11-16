using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class SystemHealthMonitoringTests
{
    [Fact]
    public void WorkerHeartbeatRegistry_marks_worker_stale_after_threshold()
    {
        var options = new SystemMonitoringOptions { WorkerHeartbeatStaleSeconds = 2, InstanceId = "test-instance" };
        var registry = new WorkerHeartbeatRegistry(options);
        registry.Register(MonitoredWorkerNames.PaperTradingAgent);
        registry.Pulse(MonitoredWorkerNames.PaperTradingAgent, WorkerRunStatus.Running, markSuccessfulExecution: true);

        Thread.Sleep(2500);

        var snapshot = registry.SnapshotAll(options.WorkerHeartbeatStaleSeconds).Single();
        Assert.True(snapshot.IsStale);
        Assert.Equal(WorkerRunStatus.Stopped, snapshot.Status);
    }

    [Fact]
    public void SystemHealthStore_persists_and_loads_comprehensive_report()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-health-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:SqlitePath"] = path,
            })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var store = new SystemHealthStore(configuration, database, NullLogger<SystemHealthStore>.Instance);

        var report = new SystemHealthComprehensiveReport(
            SystemHealthOverallStatus.Healthy,
            "test-instance",
            new DatabaseHealthSnapshot(true, true, true, null, DateTimeOffset.UtcNow, 1),
            [],
            new ApplicationRuntimeSnapshot(1, 1, 1, 1, 0, 0, DateTimeOffset.UtcNow),
            ["ok"],
            DateTimeOffset.UtcNow);

        store.SaveComprehensiveReport(report);
        var loaded = store.ListReports("comprehensive", 10);
        Assert.Single(loaded);
        Assert.Equal(SystemHealthOverallStatus.Healthy, loaded[0].OverallStatus);
        Assert.Contains("ok", loaded[0].Payload.Findings);
    }

    [Fact]
    public void SystemHealthService_detects_critical_when_database_snapshot_unhealthy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-health-live-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var store = new SystemHealthStore(configuration, new DatabaseOptions { SqlitePath = path }, NullLogger<SystemHealthStore>.Instance);
        var options = new SystemMonitoringOptions { WorkerHeartbeatStaleSeconds = 120 };
        var heartbeats = new WorkerHeartbeatRegistry(options);
        heartbeats.Register(MonitoredWorkerNames.SystemHealthMonitor);
        heartbeats.Pulse(MonitoredWorkerNames.SystemHealthMonitor, WorkerRunStatus.Running, markSuccessfulExecution: true);
        var service = CreateHealthService(options, heartbeats, store);

        service.UpdateDatabaseFromProbe(
            new DatabaseHealthSnapshot(false, false, false, "simulated outage", DateTimeOffset.UtcNow, 0));
        var live = service.GetLiveSnapshot();
        Assert.Equal(SystemHealthOverallStatus.Critical, live.OverallStatus);
    }

    [Fact]
    public void Public_health_database_probe_succeeds_on_valid_sqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-health-probe-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var store = new SystemHealthStore(configuration, new DatabaseOptions { SqlitePath = path }, NullLogger<SystemHealthStore>.Instance);
        var probe = store.ProbeDatabase();
        Assert.True(probe.CanConnect);
        Assert.True(probe.CanRead);
        Assert.True(probe.CanWrite);
    }

    private static SystemHealthService CreateHealthService(
        SystemMonitoringOptions options,
        WorkerHeartbeatRegistry heartbeats,
        SystemHealthStore store)
    {
        var market = new Phase1HealthMarketClient();
        var brokers = new BrokerConnectionManager(market, new Phase1HealthOrderClient());
        var board = new Phase1HealthBoardSource();
        var probes = new SystemHealthInfrastructureProbes(
            options,
            brokers,
            board,
            new SignalRServerHealthTracker(),
            new ChartLiveStreamService(null!, new SignalRServerHealthTracker(), NullLogger<ChartLiveStreamService>.Instance),
            new MStockOptions(),
            NullLogger<SystemHealthInfrastructureProbes>.Instance);
        return new SystemHealthService(
            options,
            new LiveExecutionOptions(),
            brokers,
            heartbeats,
            store,
            new SystemHealthExceptionTracker(),
            probes);
    }

    private sealed class Phase1HealthBoardSource : ISystemHealthBoardProbeSource
    {
        public BoardResponse Snapshot() =>
            new(new BoardCounts(0, 0, 0), [], DateTimeOffset.UtcNow, "test");
    }

    private sealed class Phase1HealthMarketClient : IBrokerMarketClient
    {
        public string ProviderId => BrokerIds.MStock;
        public string DisplayName => "m.Stock";
        public bool HasSession => false;
        public void ClearSession() { }
        public Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct) => Task.FromResult<QuoteSnap?>(null);
        public Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpForBoardNamesAsync(IReadOnlyList<string> boardNames, CancellationToken ct) => throw new NotSupportedException();
        public Task<OptionQuoteSnap?> GetOptionQuoteAsync(string underlying, DateTime expiry, decimal strike, string side, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(IReadOnlyList<OptionQuoteRequest> contracts, CancellationToken ct) => throw new NotSupportedException();
        public Task<FundSummary?> GetFundSummaryAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DayCandle>> GetDayCandlesAsync(string exchange, int instrumentToken, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DayCandle>> GetIntradayCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, DateTimeOffset? fromIst, DateTimeOffset? toIst, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Phase1HealthOrderClient : IBrokerOrderClient
    {
        public string ProviderId => BrokerIds.MStock;
        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<string>> ModifyOrderAsync(string orderId, BrokerOrderRequest order, int remainingQuantity, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, [], ""));
        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<bool>> ConvertPositionAsync(string tradingSymbol, string exchange, string transactionType, int quantity, string oldProduct, string newProduct, CancellationToken ct) => throw new NotSupportedException();
    }
}