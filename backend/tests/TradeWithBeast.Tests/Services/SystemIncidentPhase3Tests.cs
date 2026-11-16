using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class SystemIncidentPhase3Tests
{
    [Fact]
    public void First_failure_creates_incident_with_expected_fingerprint()
    {
        var harness = CreateHarness();
        var candidate = SampleCandidate("workers", "PaperTradingAgent", SystemIncidentCategory.WorkerStalled);
        var created = harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow);
        Assert.Equal(SystemIncidentStatus.Detected, created.Status);
        Assert.Equal(candidate.Fingerprint, created.Fingerprint);
        Assert.Equal(1, created.OccurrenceCount);
    }

    [Fact]
    public void Repeated_failure_updates_same_incident()
    {
        var harness = CreateHarness();
        var candidate = SampleCandidate("workers", "PaperTradingAgent", SystemIncidentCategory.WorkerStalled);
        var first = harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow);
        var second = harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.OccurrenceCount);
    }

    [Fact]
    public void Different_workers_create_separate_incidents()
    {
        var harness = CreateHarness();
        var a = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("workers", "PaperTradingAgent", SystemIncidentCategory.WorkerStalled),
            DateTimeOffset.UtcNow);
        var b = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("workers", "MockMarketService", SystemIncidentCategory.WorkerStalled),
            DateTimeOffset.UtcNow);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Lifecycle_transitions_emit_audit_events()
    {
        var harness = CreateHarness();
        var incident = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("database", "primary", SystemIncidentCategory.DatabaseUnavailable),
            DateTimeOffset.UtcNow);
        harness.Service.Acknowledge(incident.Id, "admin-1");
        harness.Service.Investigate(incident.Id, "admin-1");
        var events = harness.Store.ListEvents(incident.Id, 50);
        Assert.Contains(events, e => e.EventType == SystemIncidentEventTypes.Detected);
        Assert.Contains(events, e => e.EventType == SystemIncidentEventTypes.Acknowledged);
        Assert.Contains(events, e => e.EventType == SystemIncidentEventTypes.Investigating);
    }

    [Fact]
    public void Invalid_transition_is_rejected()
    {
        var harness = CreateHarness();
        var incident = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("database", "primary", SystemIncidentCategory.DatabaseUnavailable),
            DateTimeOffset.UtcNow);
        harness.Service.Resolve(incident.Id, "admin-1");
        Assert.Throws<InvalidOperationException>(() => harness.Service.Acknowledge(incident.Id, "admin-1"));
    }

    [Fact]
    public void Recovery_resolves_without_closing()
    {
        var harness = CreateHarness();
        var unhealthy = SampleCandidate("workers", "PaperTradingAgent", SystemIncidentCategory.WorkerStalled);
        var incident = harness.Store.CreateOrUpdateOccurrence(unhealthy, DateTimeOffset.UtcNow);
        harness.Store.UpdateStatus(
            incident.Id,
            SystemIncidentStatus.Resolved,
            SystemIncidentEventTypes.Recovered,
            "Underlying health condition recovered.",
            null,
            DateTimeOffset.UtcNow);
        Assert.Null(harness.Store.GetActiveByFingerprint(unhealthy.Fingerprint));
        var loaded = harness.Store.GetById(incident.Id);
        Assert.Equal(SystemIncidentStatus.Resolved, loaded!.Status);
        Assert.Null(loaded.ClosedUtc);
    }

    [Fact]
    public async Task Alert_is_persisted_and_dispatched_without_external_delivery()
    {
        var harness = CreateHarness(new SystemMonitoringOptions { AlertEngineEnabled = true, AlertMinimumSeverity = SystemIncidentSeverity.Low });
        var incident = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("database", "primary", SystemIncidentCategory.DatabaseUnavailable, SystemIncidentSeverity.Critical),
            DateTimeOffset.UtcNow);
        var alert = await harness.AlertService.TryCreateAlertForIncidentAsync(incident, CancellationToken.None);
        Assert.NotNull(alert);
        Assert.Equal(SystemAlertStatus.Dispatched, alert!.Status);
        Assert.NotNull(alert.DispatchedUtc);
    }

    [Fact]
    public async Task Alert_cooldown_suppresses_duplicate()
    {
        var harness = CreateHarness(new SystemMonitoringOptions
        {
            AlertEngineEnabled = true,
            AlertMinimumSeverity = SystemIncidentSeverity.Low,
            AlertCooldownSeconds = 600,
        });
        var incident = harness.Store.CreateOrUpdateOccurrence(
            SampleCandidate("broker", "mstock", SystemIncidentCategory.BrokerDisconnected, SystemIncidentSeverity.High),
            DateTimeOffset.UtcNow);
        _ = await harness.AlertService.TryCreateAlertForIncidentAsync(incident, CancellationToken.None);
        var second = await harness.AlertService.TryCreateAlertForIncidentAsync(incident, CancellationToken.None);
        Assert.Equal(SystemAlertStatus.Suppressed, second!.Status);
    }

    [Fact]
    public async Task Concurrent_upserts_do_not_create_duplicate_active_incidents()
    {
        var harness = CreateHarness();
        var candidate = SampleCandidate("signalr", "market-chart-hub", SystemIncidentCategory.SignalRDegradation);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow)))
            .ToArray();
        await Task.WhenAll(tasks);
        var active = harness.Store.ListActive();
        Assert.Single(active, i => i.Fingerprint == candidate.Fingerprint);
    }

    [Fact]
    public void Incident_persistence_survives_store_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-incidents-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var store1 = new SystemIncidentStore(configuration, database, NullLogger<SystemIncidentStore>.Instance);
        var created = store1.CreateOrUpdateOccurrence(
            SampleCandidate("database", "primary", SystemIncidentCategory.DatabaseUnavailable),
            DateTimeOffset.UtcNow);
        var store2 = new SystemIncidentStore(configuration, database, NullLogger<SystemIncidentStore>.Instance);
        var loaded = store2.GetById(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal(created.Fingerprint, loaded!.Fingerprint);
    }

    private static IncidentEvaluationCandidate SampleCandidate(
        string component,
        string identifier,
        SystemIncidentCategory category,
        SystemIncidentSeverity severity = SystemIncidentSeverity.High)
    {
        var dedup = new SystemIncidentDeduplicationService();
        var fingerprint = dedup.BuildFingerprint(component, category.ToString(), identifier);
        return new IncidentEvaluationCandidate(
            fingerprint,
            $"{category} on {identifier}",
            "Test incident",
            component,
            identifier,
            category,
            severity,
            "test error",
            1,
            false,
            "{}");
    }

    private static SystemHealthService CreateHealthStub()
    {
        var options = new SystemMonitoringOptions();
        var heartbeats = new WorkerHeartbeatRegistry(options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = Path.GetTempFileName() })
            .Build();
        var store = new SystemHealthStore(configuration, new DatabaseOptions(), NullLogger<SystemHealthStore>.Instance);
        var brokers = new BrokerConnectionManager(new Phase1HealthMarketClient(), new Phase1HealthOrderClient());
        var board = new Phase1HealthBoardSource();
        var probes = new SystemHealthInfrastructureProbes(
            options,
            brokers,
            board,
            new SignalRServerHealthTracker(),
            new ChartLiveStreamService(null!, new SignalRServerHealthTracker(), NullLogger<ChartLiveStreamService>.Instance),
            new MStockOptions(),
            NullLogger<SystemHealthInfrastructureProbes>.Instance);
        return new SystemHealthService(options, new LiveExecutionOptions(), brokers, heartbeats, store, new SystemHealthExceptionTracker(), probes);
    }

    private static Harness CreateHarness(SystemMonitoringOptions? options = null)
    {
        options ??= new SystemMonitoringOptions();
        options.ClampIntervals();
        var path = Path.Combine(Path.GetTempPath(), $"twb-incidents-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var store = new SystemIncidentStore(configuration, database, NullLogger<SystemIncidentStore>.Instance);
        var dedup = new SystemIncidentDeduplicationService();
        var lifecycle = new SystemIncidentLifecycleService();
        var evaluator = new SystemIncidentEvaluator(options, dedup);
        var dispatcher = new DatabaseIncidentAlertDispatcher(store, NullLogger<DatabaseIncidentAlertDispatcher>.Instance);
        var alertService = new SystemIncidentAlertService(options, store, dispatcher);
        var notificationOptions = new IncidentNotificationOptions { NotificationsEnabled = false };
        notificationOptions.Clamp();
        var notificationStore = new SystemIncidentNotificationStore(configuration, database, NullLogger<SystemIncidentNotificationStore>.Instance);
        var notificationSettings = new SystemIncidentNotificationSettingsService(notificationOptions, notificationStore);
        var router = new SystemIncidentNotificationRouter();
        var content = new IncidentNotificationContentBuilder();
        var escalation = new SystemIncidentEscalationService(notificationStore, notificationSettings, lifecycle, NullLogger<SystemIncidentEscalationService>.Instance);
        var notifications = new SystemIncidentNotificationService(
            notificationStore,
            store,
            notificationSettings,
            router,
            content,
            escalation,
            NullLogger<SystemIncidentNotificationService>.Instance);
        var service = new SystemIncidentService(
            options,
            store,
            lifecycle,
            evaluator,
            alertService,
            notifications,
            new NoOpProductionSafetyIncidentEvaluator(),
            NullLogger<SystemIncidentService>.Instance);
        return new Harness(store, service, alertService);
    }

    private sealed record Harness(SystemIncidentStore Store, SystemIncidentService Service, SystemIncidentAlertService AlertService);

    private sealed class Phase1HealthBoardSource : ISystemHealthBoardProbeSource
    {
        public BoardResponse Snapshot() => new(new BoardCounts(0, 0, 0), [], DateTimeOffset.UtcNow, "test");
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
        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) => Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) => Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) => Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, [], ""));
        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<bool>> ConvertPositionAsync(string tradingSymbol, string exchange, string transactionType, int quantity, string oldProduct, string newProduct, CancellationToken ct) => throw new NotSupportedException();
    }
}