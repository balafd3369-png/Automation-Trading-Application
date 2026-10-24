using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>
/// Shared market feed: admin broker/mock services publish once; all authenticated users consume the same cache.
/// </summary>
public sealed class MarketDataDistributor
{
    private readonly MockMarketService _mock;
    private readonly BrokerConnectionManager _brokers;
    public MarketDataDistributor(
        MockMarketService mock,
        BrokerConnectionManager brokers)
    {
        _mock = mock;
        _brokers = brokers;
    }

    public MarketFeedStatus GetStatus()
    {
        var connected = _brokers.HasSession;
        var board = _mock.Snapshot();
        return new MarketFeedStatus(
            connected,
            connected ? BrokerIds.MStock : "mock",
            board.UpdatedAt,
            "Shared platform feed (admin broker when connected).");
    }
}

public sealed record MarketFeedStatus(
    bool AdminBrokerConnected,
    string FeedSource,
    DateTimeOffset? LastUpdateAt,
    string Message);
