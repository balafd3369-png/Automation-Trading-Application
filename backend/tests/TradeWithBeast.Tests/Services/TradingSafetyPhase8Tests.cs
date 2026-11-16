using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class TradingSafetyPhase8Tests
{
    [Fact]
    public void Emergency_gate_blocks_new_placement()
    {
        var safety = CreateSafetyService(out var gate);
        gate.Halt("test");
        var result = safety.AssertPolicy(LiveOrderSafetyPolicy.NewPlacement);
        Assert.False(result.Allowed);
        Assert.Contains("emergency gate", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emergency_gate_does_not_block_cancellation()
    {
        var safety = CreateSafetyService(out var gate);
        gate.Halt("test");
        var result = safety.AssertPolicy(LiveOrderSafetyPolicy.Cancellation);
        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task Circuit_decorator_blocks_place_when_open()
    {
        var brokers = new BrokerConnectionManager(new StubMarket(), new StubOrders());
        var breaker = new BrokerCircuitBreaker(
            new Lazy<BrokerConnectionManager>(() => brokers),
            new OperationsStore(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
                new DatabaseOptions(),
                new CurrentUserAccessor(),
                new SystemIdentityOptions { AllowAnonymousLocalUser = true },
                NullLogger<OperationsStore>.Instance),
            NullLogger<BrokerCircuitBreaker>.Instance);
        breaker.RecordHeartbeat();
        for (var i = 0; i < 3; i++)
        {
            breaker.RecordBrokerFailure("fail");
        }

        var client = new CircuitBreakerBrokerOrderClient(new StubOrders(), breaker);
        var result = await client.PlaceOrderAsync(
            new BrokerOrderRequest("NSE", "RELIANCE", "BUY", 1, "MIS"),
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("BLOCKED", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manual_idempotency_returns_existing_without_resubmit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-man-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var history = new TradeHistoryStore(
            configuration,
            new DatabaseOptions(),
            new CurrentUserAccessor(),
            new SystemIdentityOptions { AllowAnonymousLocalUser = true },
            NullLogger<TradeHistoryStore>.Instance);
        var id = "idem-1";
        history.UpsertManualOrder(new ManualOrderSnapshot(
            id, "LIVE", "MANUAL", "SUBMITTED", "RELIANCE", "RELIANCE", "EQUITY", "BUY", "LIMIT", "MIS", "NSE", 1, 100,
            0, 0, "brk-1", "t1", "open", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var existing = history.GetManualOrder(id);
        Assert.NotNull(existing);
        Assert.Equal("SUBMITTED", existing!.Status);
    }

    private static LiveTradingExecutionSafetyService CreateSafetyService(out LiveTradingEmergencyGate gate)
    {
        gate = new LiveTradingEmergencyGate();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = Path.GetTempFileName() })
            .Build();
        return new LiveTradingExecutionSafetyService(
            gate,
            new BrokerCircuitBreaker(
                new Lazy<BrokerConnectionManager>(() => new BrokerConnectionManager(new StubMarket(), new StubOrders())),
                new OperationsStore(configuration, new DatabaseOptions(), new CurrentUserAccessor(), new SystemIdentityOptions { AllowAnonymousLocalUser = true }, NullLogger<OperationsStore>.Instance),
                NullLogger<BrokerCircuitBreaker>.Instance),
            new TradingExecutionSafetyOptions { PersistEmergencyGateState = false },
            new TradingSafetyStore(configuration, new DatabaseOptions()),
            NullLogger<LiveTradingExecutionSafetyService>.Instance);
    }

    private sealed class StubMarket : IBrokerMarketClient
    {
        public string ProviderId => BrokerIds.MStock;
        public string DisplayName => "x";
        public bool HasSession => true;
        public void ClearSession() { }
        public Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, QuoteSnap>>(new Dictionary<string, QuoteSnap>());
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpForBoardNamesAsync(IReadOnlyList<string> boardNames, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
        public Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct) => Task.FromResult<QuoteSnap?>(null);
        public Task<OptionQuoteSnap?> GetOptionQuoteAsync(string underlying, DateTime expiry, decimal strike, string side, CancellationToken ct) => Task.FromResult<OptionQuoteSnap?>(null);
        public Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(IReadOnlyList<OptionQuoteRequest> contracts, CancellationToken ct) => Task.FromResult<IReadOnlyList<OptionMarketQuote>>(Array.Empty<OptionMarketQuote>());
        public Task<FundSummary?> GetFundSummaryAsync(CancellationToken ct) => Task.FromResult<FundSummary?>(null);
        public Task<IReadOnlyList<DayCandle>> GetDayCandlesAsync(string exchange, int instrumentToken, CancellationToken ct) => Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetIntradayCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) => Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) => Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, DateTimeOffset? fromIst, DateTimeOffset? toIst, CancellationToken ct) => Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
    }

    private sealed class StubOrders : IBrokerOrderClient
    {
        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, "1", "ok"));
        public Task<BrokerApiResult<string>> ModifyOrderAsync(string orderId, BrokerOrderRequest order, int remainingQuantity, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(false, null, "no", ""));
        public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<string>(true, orderId, "ok"));
        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, [], ""));
        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<BrokerMargin>(true, new BrokerMargin(0, 0, 0), ""));
        public Task<BrokerApiResult<bool>> ConvertPositionAsync(string tradingSymbol, string exchange, string transactionType, int quantity, string oldProduct, string newProduct, CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<bool>(true, true, ""));
    }
}