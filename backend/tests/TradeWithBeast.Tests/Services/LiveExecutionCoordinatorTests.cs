using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class LiveExecutionCoordinatorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"live-execution-{Guid.NewGuid():N}.db");

    [Fact]
    public void ComputeBrokerPnl_splits_open_and_closed_positions()
    {
        var pnl = InvokeComputeBrokerPnl(
        [
            new("NFO", "NIFTY22SEP23400PE", "MIS", 65, 0, 137.65m, 143m, 347.75m),
            new("NFO", "NIFTY22SEP23300PE", "MIS", 0, 0, 0m, 98.55m, -263.25m),
        ]);

        Assert.Equal(84.50m, pnl.TotalPnl);
        Assert.Equal(-263.25m, pnl.RealisedPnl);
        Assert.Equal(347.75m, pnl.UnrealisedPnl);
    }

    [Fact]
    public async Task Hedge_places_buy_first_and_compensates_when_sell_is_rejected()
    {
        var proposal = Proposal(
            "hedge-1",
            "VERTICAL_DEBIT_SPREAD",
            [
                Leg("buy-leg", "NIFTY26SEP25000CE", "BUY", 100m),
                Leg("sell-leg", "NIFTY26SEP25100CE", "SELL", 60m),
            ]);
        var broker = new FakeBroker
        {
            Place = order =>
            {
                if (order.Tag.Contains("ENT") && order.TransactionType == "SELL")
                    return BrokerApiResult<string>.Fail("sell leg rejected by exchange", "InputException");
                return new(true, order.TransactionType == "BUY" ? "buy-order" : "comp-order", "accepted");
            },
            Orders =
            [
                new("buy-order", "", "NFO", proposal.Legs![0].Symbol, "BUY", "MIS", "LIMIT",
                    "COMPLETE", "", "", 65, 65, 0, 0, 101m, 0, 101m, "", DateTimeOffset.UtcNow),
            ],
        };
        var coordinator = Create(proposal, broker);
        coordinator.Arm(LiveExecutionCoordinator.ArmAcknowledgement);

        var result = await coordinator.ConfirmAsync(proposal.Id, "MIS", CancellationToken.None);

        Assert.Equal(["BUY", "SELL", "SELL"], broker.Placed.Select(order => order.TransactionType));
        var trade = Assert.Single(result.Trades);
        Assert.Equal("FAILED", trade.Status);
        Assert.Contains("sell leg rejected by exchange", trade.FailureReason);
        Assert.Contains(trade.Orders, order => order.Purpose == "COMPENSATION");
    }

    [Fact]
    public async Task Naked_sell_preserves_margin_rejection_and_places_no_order()
    {
        var proposal = Proposal(
            "naked-1",
            "NAKED_INDEX_OPTION_SHORT",
            [Leg("short-leg", "NIFTY26SEP25100CE", "SELL", 100m)]);
        var broker = new FakeBroker
        {
            Margin = BrokerApiResult<BrokerMargin>.Fail("Insufficient collateral margin", "InputException"),
        };
        var coordinator = Create(proposal, broker);
        coordinator.Arm(LiveExecutionCoordinator.ArmAcknowledgement);

        var result = await coordinator.ConfirmAsync(proposal.Id, "MIS", CancellationToken.None);

        Assert.Empty(broker.Placed);
        Assert.Contains("Insufficient collateral margin", Assert.Single(result.Trades).FailureReason);
    }

    private LiveExecutionCoordinator Create(PaperTradeProposal proposal, FakeBroker broker)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TradeHistory:DatabasePath"] = _path })
            .Build();
        var auth = CreateAuth();
        var brokers = new BrokerConnectionManager(auth, broker);
        brokers.OnMStockSessionEstablished();
        return new(
            brokers,
            new FakeProposalSource(proposal),
            new TradeHistoryStore(configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<TradeHistoryStore>.Instance),
            new LiveExecutionOptions { Enabled = true, OrderTimeoutSeconds = 3, TreatMarketAsOpen = true },
            new TradingPlatformOptions { FuturesEnabled = false },
            NullLogger<LiveExecutionCoordinator>.Instance);
    }

    [Fact]
    public async Task Future_leg_blocked_from_live_when_futures_disabled()
    {
        var proposal = Proposal(
            "future-1",
            "NAKED_INDEX_FUTURE",
            [
                new(
                    "f1",
                    "NIFTY FUT",
                    "NIFTY",
                    "FUTURE",
                    "BUY",
                    1,
                    65,
                    24_500m,
                    null,
                    null,
                    null,
                    new("test", "NFO", 123, DateTimeOffset.UtcNow, 0, true, 24_499m, 24_501m, 1000, 1000, [])),
            ]);
        var broker = new FakeBroker();
        var coordinator = Create(proposal, broker);
        coordinator.Arm(LiveExecutionCoordinator.ArmAcknowledgement);

        var result = await coordinator.ConfirmAsync(proposal.Id, "MIS", CancellationToken.None);

        Assert.Empty(broker.Placed);
        Assert.Empty(result.Trades);
        Assert.Contains(
            InstrumentExecutionPolicy.FuturesDisabledLiveMessage,
            result.ReconciliationStatus);
    }

    private static LiveBrokerPnlSnapshot InvokeComputeBrokerPnl(IReadOnlyList<BrokerPosition> positions)
    {
        var method = typeof(LiveExecutionCoordinator).GetMethod(
            "ComputeBrokerPnl",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);
        return (LiveBrokerPnlSnapshot)method!.Invoke(null, [positions])!;
    }

    private static MStockAuthClient CreateAuth()
    {
        var path = Path.Combine(Path.GetTempPath(), $"live-auth-{Guid.NewGuid():N}.json");
        var sessions = new MStockSessionStore(path);
        sessions.SaveToken("test-token");
        return new(
            new StubFactory(new HttpClient()),
            new MStockOptions { ApiKey = "key", RestBaseUrl = "https://api.mstock.trade" },
            sessions,
            new MStockLoginContext(),
            NullLogger<MStockAuthClient>.Instance);
    }

    private static PaperTradeProposal Proposal(
        string id,
        string strategy,
        IReadOnlyList<PaperTradeLegProposal> legs) =>
        new(
            id, legs.Count == 1 ? "OPTION" : "MULTI_LEG", "NIFTY", "BEARISH", 65,
            legs.Sum(leg => (leg.Side == "BUY" ? 1 : -1) * leg.IndicativePrice),
            150m, 50m, "test evidence", DateTimeOffset.UtcNow.AddMinutes(2),
            strategy,
            "NIFTY",
            80m,
            3_250m,
            3_250m,
            3_250m,
            1m,
            ["test"],
            legs,
            DateTimeOffset.UtcNow);

    private static PaperTradeLegProposal Leg(string id, string symbol, string side, decimal price) =>
        new(
            id, symbol, "NIFTY", "OPTION", side, 1, 65, price, 25_000m, "CE",
            DateTimeOffset.UtcNow.AddDays(2),
            new("test", "NFO", 123, DateTimeOffset.UtcNow, 0, true, price - 1, price + 1, 1000, 1000, []));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class FakeProposalSource(PaperTradeProposal proposal) : IPaperProposalSource
    {
        public PaperAgentSnapshot Snapshot() => new(
            true, true, "AWAITING CONFIRMATION", 100_000m, 0,
            new(100_000m, 1_000m, 500m, 3), 0, 0, 0, proposal, [], [],
            DateTimeOffset.UtcNow);

        public void CompleteExternalProposal(string proposalId, string reason) { }

        public void ReleasePendingProposal(string proposalId, string reason) { }

        public void SetAutoAdmitTrades(bool enabled) { }
    }

    private sealed class FakeBroker : IBrokerOrderClient
    {
        public Func<BrokerOrderRequest, BrokerApiResult<string>> Place { get; init; } =
            _ => new(true, "order", "accepted");
        public BrokerApiResult<BrokerMargin> Margin { get; init; } =
            new(true, new(10_000m, null, null), "ok");
        public IReadOnlyList<BrokerOrder> Orders { get; init; } = [];
        public List<BrokerOrderRequest> Placed { get; } = [];

        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct)
        {
            Placed.Add(order);
            return Task.FromResult(Place(order));
        }

        public Task<BrokerApiResult<string>> ModifyOrderAsync(string orderId, BrokerOrderRequest order, int remainingQuantity, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, orderId, "ok"));
        public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, orderId, "ok"));
        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, Orders, "ok"));
        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], "ok"));
        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, [], "ok"));
        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) =>
            Task.FromResult(Margin);
        public Task<BrokerApiResult<bool>> ConvertPositionAsync(
            string tradingSymbol, string exchange, string transactionType, int quantity,
            string oldProduct, string newProduct, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<bool>(true, true, "ok"));
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
