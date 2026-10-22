using Microsoft.Extensions.Configuration;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class TradeHistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "trade-with-beast-tests",
        $"history-{Guid.NewGuid():N}.db");

    [Fact]
    public void Persists_complete_trade_lifecycle_and_profit_summary()
    {
        var store = CreateStore();
        var proposal = new PaperTradeProposal(
            "trade-1",
            "STOCK",
            "INFY",
            "LONG",
            10,
            100m,
            95m,
            110m,
            "Price crossed above B1",
            DateTimeOffset.UtcNow.AddMinutes(1));

        store.SaveProposal(proposal);
        store.MarkOpened(proposal.Id, 10, 101m, DateTimeOffset.UtcNow);
        store.MarkFinal(proposal.Id, "CLOSED", "target reached", 110m, 90m);

        var history = store.Query("3m", DateTimeOffset.UtcNow.AddSeconds(1));

        var trade = Assert.Single(history.Trades);
        Assert.Equal("CLOSED", trade.Status);
        Assert.Equal(101m, trade.EntryPrice);
        Assert.Equal(110m, trade.ExitPrice);
        Assert.Equal(90m, trade.Pnl);
        Assert.Equal(1, history.Summary.WinningTrades);
        Assert.Equal(90m, history.Summary.RealizedPnl);
    }

    [Fact]
    public void Persists_rejected_proposal_without_counting_it_as_closed_trade()
    {
        var store = CreateStore();
        var proposal = new PaperTradeProposal(
            "trade-2",
            "STOCK",
            "TCS",
            "SHORT",
            5,
            200m,
            205m,
            190m,
            "Price crossed below S1",
            DateTimeOffset.UtcNow.AddMinutes(1));

        store.SaveProposal(proposal);
        store.MarkFinal(proposal.Id, "REJECTED", "rejected by user");

        var history = store.Query("1m", DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal("REJECTED", Assert.Single(history.Trades).Status);
        Assert.Equal(0, history.Summary.ClosedTrades);
    }

    [Fact]
    public void Multi_leg_fill_is_atomic_and_recovers_after_restart()
    {
        var store = CreateStore();
        var expiry = DateTimeOffset.UtcNow.AddDays(2);
        var proposal = new PaperTradeProposal(
            "spread-1", "MULTI_LEG", "NIFTY", "BULLISH", 65, 40m, 20m, 80m,
            "defined risk debit spread", DateTimeOffset.UtcNow.AddMinutes(1),
            "VERTICAL_DEBIT_SPREAD", "NIFTY", 75m, 2_600m, 3_900m, 50_000m, 1.5m,
            ["trend=UP", "defined risk"],
            [
                new("spread-1-1", "NIFTY 25000 CE", "NIFTY", "OPTION", "BUY", 1, 65, 100m, 25000m, "CE", expiry),
                new("spread-1-2", "NIFTY 25100 CE", "NIFTY", "OPTION", "SELL", 1, 65, 60m, 25100m, "CE", expiry),
            ],
            DateTimeOffset.UtcNow);
        store.SaveMultiProposal(proposal);
        var fills = new[]
        {
            new PaperPositionLegSnapshot("spread-1-1", "NIFTY 25000 CE", "BUY", 65, 101m, 101m, 0, 25000m, "CE", expiry),
            new PaperPositionLegSnapshot("spread-1-2", "NIFTY 25100 CE", "SELL", 65, 61m, 61m, 0, 25100m, "CE", expiry),
        };
        store.OpenMultiLegAtomic(proposal.Id, fills, DateTimeOffset.UtcNow);

        var recovered = Assert.Single(CreateStore().RecoverOpenTrades());

        Assert.Equal("VERTICAL_DEBIT_SPREAD", recovered.Strategy);
        Assert.Equal(3_900m, recovered.MaxReward);
        Assert.Equal(2, recovered.Legs.Count);
        Assert.All(recovered.Legs, leg => Assert.True(leg.EntryPrice > 0));
        Assert.Contains(CreateStore().RecentEvents(), x => x.TradeId == proposal.Id && x.Type == "OPENED");
    }

    [Fact]
    public void Multi_leg_close_persists_each_exit_and_no_longer_recovers()
    {
        var store = CreateStore();
        var proposal = new PaperTradeProposal(
            "close-1", "OPTION", "NIFTY", "BULLISH", 65, 100m, 70m, 160m,
            "option long", DateTimeOffset.UtcNow.AddMinutes(1),
            "INDEX_OPTION_LONG", "NIFTY", 60m, 6_500m, 3_900m, 6_500m, 0.6m, ["trend=UP"],
            [new("close-1-1", "NIFTY 25000 CE", "NIFTY", "OPTION", "BUY", 1, 65, 100m, 25000m, "CE")]);
        store.SaveMultiProposal(proposal);
        var open = new[]
        {
            new PaperPositionLegSnapshot("close-1-1", "NIFTY 25000 CE", "BUY", 65, 100m, 100m, 0, 25000m, "CE"),
        };
        store.OpenMultiLegAtomic(proposal.Id, open, DateTimeOffset.UtcNow);
        var closed = open.Select(x => x with { CurrentPrice = 110m, Pnl = 650m }).ToList();
        store.CloseMultiLegAtomic(proposal.Id, closed, "target reached", 650m, DateTimeOffset.UtcNow);

        Assert.Empty(CreateStore().RecoverOpenTrades());
        Assert.Contains(CreateStore().RecentEvents(), x => x.TradeId == proposal.Id && x.Type == "CLOSED" && x.Pnl == 650m);
        var history = CreateStore().QueryPaperTrades("1m", DateTimeOffset.UtcNow.AddSeconds(1));
        var persisted = Assert.Single(history.Trades);
        Assert.Equal("INDEX_OPTION_LONG", persisted.Strategy);
        Assert.Equal(650m, persisted.Pnl);
        Assert.Equal(650m, Assert.Single(persisted.Legs).Pnl);
        Assert.Equal(1, history.Summary.WinningTrades);
    }

    [Fact]
    public void Live_trade_orders_and_exact_broker_failure_survive_restart()
    {
        var store = CreateStore();
        var proposal = new PaperTradeProposal(
            "live-1", "OPTION", "NIFTY", "BEARISH", 65, -100m, 150m, 50m,
            "range naked call", DateTimeOffset.UtcNow.AddMinutes(1),
            "NAKED_INDEX_OPTION_SHORT", "NIFTY", 72m, 3_250m, 3_250m, 80_000m, 1m,
            ["gap risk"],
            [new("live-1-1", "NIFTY 25000 CE", "NIFTY", "OPTION", "SELL", 1, 65, 100m, 25000m, "CE")]);
        store.SaveLiveTrade(proposal, "MIS");
        store.UpsertLiveOrder(new(
            "local-1", proposal.Id, "live-1-1", "broker-123", "NFO", "NIFTY 25000 CE",
            "SELL", "MIS", "ENTRY", "REJECTED", 65, 0, 0,
            "Insufficient collateral margin", "TWB-live", DateTimeOffset.UtcNow));
        store.SetLiveTradeStatus(proposal.Id, "FAILED", "Insufficient collateral margin");

        var persisted = Assert.Single(CreateStore().ReadLiveTrades());

        Assert.Equal("FAILED", persisted.Status);
        Assert.Equal("Insufficient collateral margin", persisted.FailureReason);
        Assert.Equal("broker-123", Assert.Single(persisted.Orders).BrokerOrderId);
        Assert.Contains(CreateStore().RecentLiveEvents(), item =>
            item.TradeId == proposal.Id && item.Message.Contains("Insufficient collateral margin"));
    }

    private TradeHistoryStore CreateStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TradeHistory:DatabasePath"] = _path,
            })
            .Build();
        return new TradeHistoryStore(configuration);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
