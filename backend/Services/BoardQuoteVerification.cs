using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>Detects demo seed prices and whether a symbol has a broker-verified live quote.</summary>
public static class BoardQuoteVerification
{
    public static bool IsDefaultDemoSeed(LiveSymbolFields symbol) =>
        !IndexUniverse.IsIndex(symbol.Name)
        && symbol.Last == 100m
        && symbol.TodayOpen == 100m
        && symbol.YesterdayHigh == 108m
        && symbol.YesterdayLow == 92m;

    /// <summary>Placeholder catalog / ensure-symbol price (e.g. 100.00 on MARUTI) even after zone OHLC loaded.</summary>
    public static bool IsCatalogPlaceholderPrice(LiveSymbolFields symbol)
    {
        if (IndexUniverse.IsIndex(symbol.Name))
        {
            return false;
        }

        if (IsDefaultDemoSeed(symbol))
        {
            return true;
        }

        if (symbol.Last <= 0)
        {
            return true;
        }

        if (symbol.Last <= 101m
            && symbol.TodayOpen <= 101m
            && symbol.YesterdayHigh == 108m
            && symbol.YesterdayLow == 92m)
        {
            return true;
        }

        var anchor = PriceAnchor(symbol);
        if (anchor > 250m && symbol.Last <= 101m)
        {
            return true;
        }

        if (anchor > 0 && symbol.Last > 0)
        {
            var deviation = Math.Abs(symbol.Last - anchor) / anchor;
            if (deviation > 0.35m && symbol.Last <= 101m)
            {
                return true;
            }
        }

        return false;
    }

    public static decimal PriceAnchor(LiveSymbolFields symbol)
    {
        if (symbol.PreviousClose > 0)
        {
            return symbol.PreviousClose;
        }

        if (symbol.YesterdayHigh > 0 && symbol.YesterdayLow > 0)
        {
            return (symbol.YesterdayHigh + symbol.YesterdayLow) / 2m;
        }

        return 0;
    }

    public static bool HasVerifiedLiveQuote(LiveSymbolFields symbol, bool brokerHasSession)
    {
        if (!brokerHasSession)
        {
            return symbol.Last > 0 && !IsCatalogPlaceholderPrice(symbol);
        }

        if (symbol.InstrumentToken <= 0 || symbol.DataTimestamp is null || symbol.Last <= 0)
        {
            return false;
        }

        if (IsCatalogPlaceholderPrice(symbol))
        {
            return false;
        }

        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return PriceAnchor(symbol) > 0;
        }

        var decision = BrokerQuoteGate.EvaluateLastPrice(
            symbol.Name,
            symbol.Last,
            symbol.Last,
            symbol.TodayOpen,
            symbol.PreviousClose);
        return decision.Accept;
    }

    public readonly record struct LiveSymbolFields(
        string Name,
        decimal Last,
        decimal TodayOpen,
        decimal YesterdayHigh,
        decimal YesterdayLow,
        decimal PreviousClose,
        int InstrumentToken,
        DateTimeOffset? DataTimestamp);
}
