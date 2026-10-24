using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

/// <summary>
/// Locks catalog / rollout identifiers so additive instrument work cannot drift strategy definitions.
/// Does not execute the paper agent loop — only static contracts used by automation.
/// </summary>
public sealed class StrategyExecutionRegressionTests
{
    private static readonly string[] LockedCatalogIds =
    [
        StrategyCatalog.ZoneB1BreakId,
        StrategyCatalog.ZoneS1BreakId,
        StrategyCatalog.ZoneS2BounceId,
        StrategyCatalog.StockB1RejectionId,
        StrategyCatalog.TenAmHighFadeId,
        StrategyCatalog.TenAmLowBreakToDayOpenId,
    ];

    [Fact]
    public void Strategy_catalog_count_and_ids_unchanged()
    {
        var all = StrategyCatalog.All();
        Assert.Equal(6, all.Count);
        Assert.Equal(LockedCatalogIds.OrderBy(x => x), all.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void Zone_rollout_policy_version_unchanged()
    {
        var snap = ZoneStrategyRollout.BuildSnapshot();
        Assert.Equal("2026-10-zone-90d-v1", snap.PolicyVersion);
        Assert.Equal(12, snap.Rows.Count);
    }

    [Fact]
    public void Futures_disabled_by_default_in_options_type()
    {
        var options = new TradingPlatformOptions();
        Assert.False(options.FuturesEnabled);
    }

    [Fact]
    public void Live_routing_allows_option_proposals_when_futures_disabled()
    {
        var proposal = new PaperTradeProposal(
            "p1",
            InstrumentTypeNormalizer.ApiOption,
            "NIFTY",
            "BULLISH",
            65,
            100m,
            90m,
            110m,
            "test",
            DateTimeOffset.UtcNow.AddMinutes(1),
            StrategyCatalog.ZoneB1BreakId,
            "NIFTY",
            75m,
            500m,
            1000m,
            6500m,
            2m,
            [],
            [
                new(
                    "l1",
                    "NIFTY26SEP25000CE",
                    "NIFTY",
                    InstrumentTypeNormalizer.ApiOption,
                    "BUY",
                    1,
                    65,
                    100m,
                    25_000m,
                    "CE",
                    DateTimeOffset.UtcNow.AddDays(1),
                    null),
            ],
            DateTimeOffset.UtcNow);

        Assert.True(InstrumentExecutionPolicy.LiveBrokerRoutingAllowed(proposal, futuresEnabled: false));
    }

    [Fact]
    public void Zone_auto_live_option_proposal_still_allowed_when_futures_disabled()
    {
        var proposal = new PaperTradeProposal(
            "zone-nifty",
            InstrumentTypeNormalizer.ApiMultiLeg,
            "NIFTY",
            "BULLISH",
            65,
            120m,
            100m,
            140m,
            "zone break",
            DateTimeOffset.UtcNow.AddMinutes(2),
            StrategyCatalog.ZoneB1BreakId,
            "NIFTY",
            80m,
            800m,
            1600m,
            7800m,
            2m,
            [],
            [
                new(
                    "z1",
                    "NIFTY26SEP25000CE",
                    "NIFTY",
                    InstrumentTypeNormalizer.ApiOption,
                    "BUY",
                    1,
                    65,
                    120m,
                    25_000m,
                    "CE",
                    DateTimeOffset.UtcNow.AddDays(2),
                    null),
            ],
            DateTimeOffset.UtcNow);

        Assert.True(InstrumentExecutionPolicy.LiveBrokerRoutingAllowed(proposal, futuresEnabled: false));
        Assert.Equal(ProductType.IndexOption, ProductTypeInference.Infer("OPTION", "NIFTY"));
    }

    [Fact]
    public void Future_proposal_blocked_live_but_kind_parses_for_paper()
    {
        var proposal = new PaperTradeProposal(
            "fut",
            InstrumentTypeNormalizer.ApiFuture,
            "NIFTY FUT",
            "BULLISH",
            65,
            24_500m,
            24_400m,
            24_600m,
            "test",
            DateTimeOffset.UtcNow.AddMinutes(1),
            "NAKED_INDEX_FUTURE",
            "NIFTY",
            70m,
            500m,
            1000m,
            1_592_500m,
            2m,
            [],
            [
                new(
                    "f1",
                    "NIFTY FUT",
                    "NIFTY",
                    InstrumentTypeNormalizer.ApiFuture,
                    "BUY",
                    1,
                    65,
                    24_500m,
                    null,
                    null,
                    null,
                    null),
            ],
            DateTimeOffset.UtcNow);

        Assert.False(InstrumentExecutionPolicy.LiveBrokerRoutingAllowed(proposal, futuresEnabled: false));
        Assert.Equal(InstrumentKind.Future, InstrumentTypeNormalizer.Parse("FUTURE"));
        Assert.Equal(ProductType.IndexFuture, ProductTypeInference.Infer("FUTURE", "NIFTY"));
    }
}
