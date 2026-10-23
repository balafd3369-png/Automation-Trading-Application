using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>Detects demo seed prices and whether a symbol has a broker-verified live quote.</summary>
internal static class BoardQuoteVerification
{
    public static bool IsDefaultDemoSeed(LiveSymbolFields symbol) =>
        !IndexUniverse.IsIndex(symbol.Name)
        && symbol.Last == 100m
        && symbol.TodayOpen == 100m
        && symbol.YesterdayHigh == 108m
        && symbol.YesterdayLow == 92m;

    public static bool HasVerifiedLiveQuote(LiveSymbolFields symbol, bool brokerHasSession) =>
        !brokerHasSession
        || (symbol.Last > 0
            && symbol.InstrumentToken > 0
            && symbol.DataTimestamp is not null
            && !IsDefaultDemoSeed(symbol));

    internal readonly record struct LiveSymbolFields(
        string Name,
        decimal Last,
        decimal TodayOpen,
        decimal YesterdayHigh,
        decimal YesterdayLow,
        int InstrumentToken,
        DateTimeOffset? DataTimestamp);
}
