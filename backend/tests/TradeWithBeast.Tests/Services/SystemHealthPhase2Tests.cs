using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class SystemHealthPhase2Tests
{
    [Fact]
    public async Task Broker_probe_without_session_does_not_place_orders()
    {
        var harness = SystemHealthTestHarness.Create(new SystemMonitoringOptions { BrokerMonitoringEnabled = true });
        var result = await harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        Assert.DoesNotContain(result.Connections, c => c.CommunicationVerified);
        Assert.Equal(0, harness.OrderClient.PlaceOrderCalls);
    }

    [Fact]
    public async Task Broker_probe_timeout_increments_consecutive_failures()
    {
        var options = new SystemMonitoringOptions
        {
            BrokerMonitoringEnabled = true,
            BrokerTimeoutSeconds = 1,
            BrokerFailureThreshold = 2,
        };
        var harness = SystemHealthTestHarness.Create(options, slowQuote: true);
        harness.MarketClient.SetSessionForTests("token");
        _ = await harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        var second = await harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        Assert.True(second.ConsecutiveFailures >= 1);
        Assert.Equal(ComponentHealthState.Degraded, second.State);
    }

    [Fact]
    public async Task Market_data_probe_ignores_stale_when_market_closed()
    {
        if (NseCashSession.IsLiveIntradayWindowNow())
        {
            return;
        }

        var harness = SystemHealthTestHarness.Create(
            new SystemMonitoringOptions { MarketDataMonitoringEnabled = true, MarketDataStaleThresholdSeconds = 10 },
            staleBoard: true);
        var snapshot = await harness.Probes.ProbeMarketDataAsync(CancellationToken.None);
        Assert.Equal(ComponentHealthState.Healthy, snapshot.State);
    }

    [Fact]
    public void Worker_evaluator_detects_stalled_progress()
    {
        var options = new SystemMonitoringOptions
        {
            WorkerProgressTimeoutSeconds = 1,
            WorkerHeartbeatStaleSeconds = 60,
        };
        var registry = new WorkerHeartbeatRegistry(options);
        registry.Register(MonitoredWorkerNames.PaperTradingAgent);
        registry.Pulse(MonitoredWorkerNames.PaperTradingAgent, WorkerRunStatus.Running, markSuccessfulExecution: true);
        Thread.Sleep(1200);
        registry.Pulse(MonitoredWorkerNames.PaperTradingAgent, WorkerRunStatus.Running);
        var detail = registry.SnapshotDetailed(options, liveTradingEnabled: false, brokerConnected: false, marketIntradayOpen: true).Single();
        Assert.Equal(ComponentHealthState.Stalled, detail.CurrentState);
        Assert.True(detail.IsProgressStale);
    }

    [Fact]
    public void Worker_evaluator_marks_unresponsive_when_heartbeat_missing()
    {
        var options = new SystemMonitoringOptions { WorkerHeartbeatStaleSeconds = 1 };
        var registry = new WorkerHeartbeatRegistry(options);
        registry.Register(MonitoredWorkerNames.MockMarketService);
        registry.Pulse(MonitoredWorkerNames.MockMarketService, WorkerRunStatus.Running, markSuccessfulExecution: true);
        Thread.Sleep(1200);
        var detail = registry.SnapshotDetailed(options, false, false, false).Single(w =>
            w.WorkerName == MonitoredWorkerNames.MockMarketService);
        Assert.Equal(ComponentHealthState.Unresponsive, detail.CurrentState);
    }

    [Fact]
    public void SignalR_tracker_records_disconnect_and_recovery()
    {
        var tracker = new SignalRServerHealthTracker();
        tracker.RecordConnected();
        tracker.RecordDisconnected(null);
        tracker.RecordConnected();
        var snapshot = tracker.BuildSnapshot(DateTimeOffset.UtcNow, enabled: true);
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.Equal(2, snapshot.TotalConnectionsObserved);
        Assert.Equal(1, snapshot.DisconnectCount);
    }

    [Fact]
    public async Task Concurrent_broker_probes_do_not_overlap()
    {
        var harness = SystemHealthTestHarness.Create(new SystemMonitoringOptions { BrokerMonitoringEnabled = true }, slowQuote: true);
        harness.MarketClient.SetSessionForTests("token");
        var first = harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        var second = harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        await Task.WhenAll(first, second);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public void Comprehensive_report_backward_compatible_with_phase1_fields()
    {
        var report = new SystemHealthComprehensiveReport(
            SystemHealthOverallStatus.Healthy,
            "instance",
            new DatabaseHealthSnapshot(true, true, true, null, DateTimeOffset.UtcNow, 1),
            [],
            new ApplicationRuntimeSnapshot(1, 1, 1, 1, 0, 0, DateTimeOffset.UtcNow),
            ["ok"],
            DateTimeOffset.UtcNow);
        Assert.Empty(report.ComponentProbes);
        Assert.Empty(report.WorkerDetails);
        Assert.Null(report.Broker);
    }

    [Fact]
    public async Task Monitoring_disabled_components_return_unknown_states()
    {
        var options = new SystemMonitoringOptions
        {
            BrokerMonitoringEnabled = false,
            MarketDataMonitoringEnabled = false,
            SignalRMonitoringEnabled = false,
        };
        var harness = SystemHealthTestHarness.Create(options);
        var broker = await harness.Probes.ProbeBrokerAsync(CancellationToken.None);
        var market = await harness.Probes.ProbeMarketDataAsync(CancellationToken.None);
        var signalR = await harness.Probes.ProbeSignalRAsync(CancellationToken.None);
        Assert.Equal(ComponentHealthState.Unknown, broker.State);
        Assert.Equal(ComponentHealthState.Unknown, market.State);
        Assert.Equal(ComponentHealthState.Unknown, signalR.State);
    }

    private sealed class SystemHealthTestHarness
    {
        public SystemHealthInfrastructureProbes Probes { get; }
        public HealthTestMarketClient MarketClient { get; }
        public HealthTestOrderClient OrderClient { get; }

        private SystemHealthTestHarness(
            SystemHealthInfrastructureProbes probes,
            HealthTestMarketClient marketClient,
            HealthTestOrderClient orderClient)
        {
            Probes = probes;
            MarketClient = marketClient;
            OrderClient = orderClient;
        }

        public static SystemHealthTestHarness Create(
            SystemMonitoringOptions options,
            bool slowQuote = false,
            bool staleBoard = false)
        {
            options.ClampIntervals();
            var marketClient = new HealthTestMarketClient(slowQuote);
            var orderClient = new HealthTestOrderClient();
            var brokers = new BrokerConnectionManager(marketClient, orderClient);
            var board = new HealthTestBoardSource(staleBoard);
            var signalR = new SignalRServerHealthTracker();
            var chartStreams = new ChartLiveStreamService(null!, signalR, NullLogger<ChartLiveStreamService>.Instance);
            var probes = new SystemHealthInfrastructureProbes(
                options,
                brokers,
                board,
                signalR,
                chartStreams,
                new MStockOptions(),
                NullLogger<SystemHealthInfrastructureProbes>.Instance);
            return new SystemHealthTestHarness(probes, marketClient, orderClient);
        }
    }

    private sealed class HealthTestBoardSource : ISystemHealthBoardProbeSource
    {
        private readonly bool _stale;

        public HealthTestBoardSource(bool stale) => _stale = stale;

        public BoardResponse Snapshot()
        {
            var updatedAt = _stale
                ? DateTimeOffset.UtcNow.AddMinutes(-30)
                : DateTimeOffset.UtcNow;
            var row = new SymbolSnapshot(
                "NIFTY",
                100,
                100,
                101,
                99,
                100,
                98,
                102,
                101,
                99,
                98,
                97,
                "Buying",
                false,
                100,
                DataTimestamp: updatedAt,
                QuoteVerified: true);
            return new BoardResponse(new BoardCounts(1, 0, 0), [row], updatedAt, "test");
        }
    }

    private sealed class HealthTestMarketClient : IBrokerMarketClient
    {
        private readonly bool _slow;
        private string _token = "";

        public HealthTestMarketClient(bool slow = false) => _slow = slow;

        public string ProviderId => BrokerIds.MStock;
        public string DisplayName => "m.Stock";
        public bool HasSession => !string.IsNullOrWhiteSpace(_token);

        public void SetSessionForTests(string token) => _token = token;

        public void ClearSession() => _token = "";

        public Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct)
        {
            if (_slow)
            {
                return Task.Run<QuoteSnap?>(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    return new QuoteSnap(1, 1, 1, 1, "NSE");
                }, ct);
            }

            return _token.Length == 0
                ? Task.FromResult<QuoteSnap?>(null)
                : Task.FromResult<QuoteSnap?>(new QuoteSnap(1, 100, 100, 100, "NSE", Timestamp: DateTimeOffset.UtcNow));
        }

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

    private sealed class HealthTestOrderClient : IBrokerOrderClient
    {
        public int PlaceOrderCalls { get; private set; }

        public string ProviderId => BrokerIds.MStock;

        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct)
        {
            PlaceOrderCalls++;
            throw new NotSupportedException();
        }

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