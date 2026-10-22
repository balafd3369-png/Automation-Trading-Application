namespace TradeWithBeast.Broker;

/// <summary>Live quotes, candles, and funds for the active broker session.</summary>
public interface IBrokerMarketClient
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool HasSession { get; }
    void ClearSession();

    Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct);
    Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct);
    Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct);
    Task<OptionQuoteSnap?> GetOptionQuoteAsync(string underlying, DateTime expiry, decimal strike, string side, CancellationToken ct);
    Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(IReadOnlyList<OptionQuoteRequest> contracts, CancellationToken ct);
    Task<FundSummary?> GetFundSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DayCandle>> GetDayCandlesAsync(string exchange, int instrumentToken, CancellationToken ct);
    Task<IReadOnlyList<DayCandle>> GetIntradayCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct);
    Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct);
    Task<IReadOnlyList<DayCandle>> GetCandlesAsync(
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset? fromIst,
        DateTimeOffset? toIst,
        CancellationToken ct);
}
