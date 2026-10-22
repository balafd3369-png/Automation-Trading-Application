namespace TradeWithBeast.Broker;

/// <summary>Routes market data and order execution to m.Stock.</summary>
public sealed class BrokerConnectionManager
{
    private readonly MStockAuthClient _mstockMarket;
    private readonly IBrokerOrderClient _mstockOrders;

    public BrokerConnectionManager(MStockAuthClient mstockMarket, IBrokerOrderClient mstockOrders)
    {
        _mstockMarket = mstockMarket;
        _mstockOrders = mstockOrders;
    }

    public IReadOnlyList<BrokerProviderInfo> Providers =>
    [
        new(
            BrokerIds.MStock,
            "m.Stock",
            BrokerAuthKind.Otp,
            ["NSE", "NFO", "BSE", "BFO"]),
    ];

    public string? ActiveProviderId => HasSession ? BrokerIds.MStock : null;

    public bool HasSession => _mstockMarket.HasSession;

    public IBrokerMarketClient Market => _mstockMarket;

    public IBrokerOrderClient Orders => _mstockOrders;

    public string ActiveDisplayName => HasSession ? _mstockMarket.DisplayName : "broker";

    public void SetActiveProvider(string providerId)
    {
        if (!string.Equals(providerId, BrokerIds.MStock, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Only m.Stock is supported (got '{providerId}').", nameof(providerId));
        }
    }

    public void OnMStockSessionEstablished()
    {
    }

    public void ClearActiveSession() => _mstockMarket.ClearSession();

    public void AlignActiveProviderWithSession()
    {
    }
}
