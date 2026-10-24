using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class BrokerReconciliationEngineTests
{
    [Fact]
    public void Position_match_when_quantities_align()
    {
        var trades = new[]
        {
            LiveTrade(
                "t1",
                "OPEN",
                "MIS",
                [Order("o1", "RELIANCE", "BUY", "ENTRY", "COMPLETE", 100, 100)]),
        };
        var broker = new[] { BrokerPos("RELIANCE", "MIS", 100) };

        var result = BrokerReconciliationEngine.Compare(new(trades, broker, []));
        Assert.Equal(1, result.PositionMatches);
        Assert.Equal(0, result.PositionMismatches);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public void Position_missing_at_broker()
    {
        var trades = new[]
        {
            LiveTrade(
                "t1",
                "OPEN",
                "MIS",
                [Order("o1", "NIFTY26SEP25000CE", "BUY", "ENTRY", "COMPLETE", 75, 75)]),
        };

        var result = BrokerReconciliationEngine.Compare(new(trades, [], []));
        Assert.Equal(0, result.PositionMatches);
        Assert.Equal(1, result.PositionMismatches);
        Assert.Contains(result.Mismatches, m => m.Reason.Contains("Missing broker position"));
    }

    [Fact]
    public void Extra_broker_position()
    {
        var broker = new[] { BrokerPos("NIFTY26SEP25000CE", "MIS", 75) };
        var result = BrokerReconciliationEngine.Compare(new([], broker, []));
        Assert.Equal(1, result.PositionMismatches);
        Assert.Contains(result.Mismatches, m => m.Reason.Contains("Extra broker position"));
    }

    [Fact]
    public void Order_status_mismatch()
    {
        var trades = new[]
        {
            LiveTrade(
                "t1",
                "OPEN",
                "MIS",
                [Order("broker-1", "RELIANCE", "BUY", "ENTRY", "COMPLETE", 10, 10, "broker-1")]),
        };
        var brokerOrders = new[]
        {
            BrokerOrder("broker-1", "RELIANCE", "REJECTED", 10, 0),
        };

        var result = BrokerReconciliationEngine.Compare(new(trades, [], brokerOrders));
        Assert.Equal(1, result.OrderMismatches);
        Assert.Contains(result.Mismatches, m => m.Kind == "ORDER" && m.Reason.Contains("status"));
    }

    [Fact]
    public void Partial_fill_mismatch()
    {
        var trades = new[]
        {
            LiveTrade(
                "t1",
                "OPEN",
                "MIS",
                [Order("broker-1", "RELIANCE", "BUY", "ENTRY", "COMPLETE", 100, 100, "broker-1")]),
        };
        var brokerOrders = new[]
        {
            BrokerOrder("broker-1", "RELIANCE", "COMPLETE", 100, 40),
        };

        var result = BrokerReconciliationEngine.Compare(new(trades, [], brokerOrders));
        Assert.Contains(result.Mismatches, m => m.Reason.Contains("Partial fill"));
    }

    [Fact]
    public async Task Broker_disconnect_skips_reconciliation()
    {
        var service = CreateService(connected: false);
        var snapshot = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(BrokerReconciliationHealth.Skipped, snapshot.Health);
        Assert.False(snapshot.BrokerConnected);
    }

    [Fact]
    public async Task Reconnect_recovery_runs_comparison()
    {
        var broker = new FakeReconciliationBroker
        {
            Positions = [BrokerPos("RELIANCE", "MIS", 10)],
        };
        var history = CreateHistoryStore();
        var proposal = Proposal("t1", "RELIANCE", 10);
        history.SaveLiveTrade(proposal, "MIS");
        history.UpsertLiveOrder(Order("o1", "leg-1", proposal.Id, "RELIANCE", "BUY", "ENTRY", "COMPLETE", 10, 10));
        history.SetLiveTradeStatus(proposal.Id, "OPEN");
        var service = CreateService(connected: true, broker: broker, history: history);
        var snapshot = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(BrokerReconciliationHealth.Healthy, snapshot.Health);
        Assert.Equal(1, snapshot.PositionMatches);
    }

    private static BrokerReconciliationService CreateService(
        bool connected,
        FakeReconciliationBroker? broker = null,
        TradeHistoryStore? history = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        history ??= CreateHistoryStore();
        var ops = new OperationsStore(
            config,
            new CurrentUserAccessor(),
            new SystemIdentityOptions { AllowAnonymousLocalUser = true },
            NullLogger<OperationsStore>.Instance);
        var auth = connected ? CreateAuth() : CreateAuthWithoutSession();
        var manager = new BrokerConnectionManager(auth, broker ?? new FakeReconciliationBroker());
        return new BrokerReconciliationService(manager, history, ops, NullLogger<BrokerReconciliationService>.Instance);
    }

    private static TradeHistoryStore CreateHistoryStore()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"recon-{Guid.NewGuid():N}.db");
        return new TradeHistoryStore(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["TradeHistory:DatabasePath"] = historyPath }).Build(),
            NullLogger<TradeHistoryStore>.Instance);
    }

    private static MStockAuthClient CreateAuth()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recon-auth-{Guid.NewGuid():N}.json");
        var sessions = new MStockSessionStore(path);
        sessions.SaveToken("test-token");
        return new(
            new StubFactory(new HttpClient()),
            new MStockOptions { ApiKey = "key", RestBaseUrl = "https://api.mstock.trade" },
            sessions,
            new MStockLoginContext(),
            NullLogger<MStockAuthClient>.Instance);
    }

    private static MStockAuthClient CreateAuthWithoutSession() =>
        new(
            new StubFactory(new HttpClient()),
            new MStockOptions { ApiKey = "key", RestBaseUrl = "https://api.mstock.trade" },
            new MStockSessionStore(Path.Combine(Path.GetTempPath(), $"recon-empty-{Guid.NewGuid():N}.json")),
            new MStockLoginContext(),
            NullLogger<MStockAuthClient>.Instance);

    private static PaperTradeProposal Proposal(string id, string symbol, int qty) =>
        new(
            id,
            "OPTION",
            symbol,
            "LONG",
            qty,
            100m,
            90m,
            110m,
            "test",
            DateTimeOffset.UtcNow.AddHours(1),
            "TEST",
            symbol,
            0,
            0,
            0,
            0,
            0,
            null,
            [new("leg-1", symbol, symbol, "STOCK", "BUY", 1, qty, 100m)]);

    private static TradeHistoryStore.StoredLiveTrade LiveTrade(
        string id,
        string status,
        string product,
        LiveOrderSnapshot[] orders) =>
        new(id, Proposal(id, orders.First().Symbol, orders.First().Quantity), product, status, null, DateTimeOffset.UtcNow, orders);

    private static LiveOrderSnapshot Order(
        string id,
        string legId,
        string tradeId,
        string symbol,
        string side,
        string purpose,
        string status,
        int qty,
        int filled,
        string? brokerOrderId = null) =>
        new(
            id,
            tradeId,
            legId,
            brokerOrderId ?? "",
            "NSE",
            symbol,
            side,
            "MIS",
            purpose,
            status,
            qty,
            filled,
            100,
            "",
            "",
            DateTimeOffset.UtcNow);

    private static LiveOrderSnapshot Order(
        string id,
        string symbol,
        string side,
        string purpose,
        string status,
        int qty,
        int filled,
        string? brokerOrderId = null) =>
        Order(id, "leg-1", "trade", symbol, side, purpose, status, qty, filled, brokerOrderId);

    private static BrokerPosition BrokerPos(string symbol, string product, int qty) =>
        new("NSE", symbol, product, qty, 0, 100, 101, 0);

    private static BrokerOrder BrokerOrder(string id, string symbol, string status, int qty, int filled) =>
        new(id, "", "NSE", symbol, "BUY", "MIS", "LIMIT", status, "", "", qty, filled, 0, 0, 100, 0, 100, "", DateTimeOffset.UtcNow);

    private sealed class FakeReconciliationBroker : IBrokerOrderClient
    {
        public IReadOnlyList<BrokerPosition> Positions { get; set; } = [];
        public IReadOnlyList<BrokerOrder> Orders { get; set; } = [];

        public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, orderId, "ok"));

        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<BrokerMargin>(true, new(0, null, null), "ok"));

        public Task<BrokerApiResult<bool>> ConvertPositionAsync(
            string tradingSymbol,
            string exchange,
            string transactionType,
            int quantity,
            string oldProduct,
            string newProduct,
            CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<bool>(true, true, "ok"));

        public Task<BrokerApiResult<string>> ModifyOrderAsync(
            string orderId,
            BrokerOrderRequest order,
            int remainingQuantity,
            CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, orderId, "ok"));

        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, "id", "ok"));

        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, Orders, "ok"));

        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, Positions, "ok"));

        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], "ok"));
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
