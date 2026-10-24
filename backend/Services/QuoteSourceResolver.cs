using TradeWithBeast.Broker;
using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public readonly record struct QuoteDisplay(
    decimal Last,
    QuoteSourceQuality Quality,
    DateTimeOffset? QuoteTimestamp);

/// <summary>Maps internal symbol state to display price + quality indicator.</summary>
public static class QuoteSourceResolver
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public static QuoteDisplay Resolve(
        LiveSymbolState symbol,
        bool brokerHasSession,
        decimal? indexSessionClose = null,
        DateTimeOffset? indexQuoteTimestamp = null)
    {
        var fields = symbol.ToFields();
        var placeholder = BoardQuoteVerification.IsCatalogPlaceholderPrice(fields);
        var anchor = BoardQuoteVerification.PriceAnchor(fields);

        if (!brokerHasSession)
        {
            if (placeholder && anchor <= 0)
            {
                return Pack(symbol, 0, QuoteSourceQuality.Placeholder);
            }

            if (placeholder && anchor > 0)
            {
                return Pack(
                    symbol,
                    NiftyZoneFormula.RoundLine(anchor),
                    QuoteSourceQuality.SeedData,
                    LastSessionCloseInstant(symbol));
            }

            return Pack(
                symbol,
                NiftyZoneFormula.RoundLine(symbol.Last),
                QuoteSourceQuality.SeedData,
                LastSessionCloseInstant(symbol));
        }

        var sessionLive = NseCashSession.IsLiveIntradayWindowNow();
        var anchorLast = symbol.PreviousClose > 0 ? symbol.PreviousClose : symbol.Last;
        var liveDecision = BrokerQuoteGate.EvaluateLastPrice(
            symbol.Name,
            symbol.Last,
            anchorLast,
            symbol.TodayOpen,
            symbol.PreviousClose);

        if (sessionLive && liveDecision.Accept && !placeholder)
        {
            return Pack(
                symbol,
                NiftyZoneFormula.RoundLine(symbol.Last),
                QuoteSourceQuality.VerifiedLive,
                symbol.DataTimestamp ?? DateTimeOffset.UtcNow);
        }

        if (sessionLive && !liveDecision.Accept)
        {
            var rejectedFallback = ResolveCloseFallback(symbol, fields);
            if (rejectedFallback > 0)
            {
                return Pack(
                    symbol,
                    NiftyZoneFormula.RoundLine(rejectedFallback),
                    QuoteSourceQuality.Rejected,
                    LastSessionCloseInstant(symbol) ?? symbol.DataTimestamp);
            }

            return Pack(symbol, 0, QuoteSourceQuality.Rejected);
        }

        if (IndexUniverse.IsIndex(symbol.Name))
        {
            var indexLast = indexSessionClose > 0 ? indexSessionClose.Value : ResolveIndexClose(symbol);
            if (indexLast > 0)
            {
                var quality = symbol.HistoryLoaded || symbol.PreviousClose > 0
                    ? QuoteSourceQuality.VerifiedClose
                    : QuoteSourceQuality.FallbackClose;
                var ts = indexQuoteTimestamp ?? LastSessionCloseInstant(symbol);
                return Pack(symbol, NiftyZoneFormula.RoundLine(indexLast), quality, ts);
            }
        }

        if (placeholder)
        {
            var close = ResolveCloseFallback(symbol, fields);
            if (close > 0)
            {
                var quality = symbol.HistoryLoaded
                    ? QuoteSourceQuality.VerifiedClose
                    : QuoteSourceQuality.FallbackClose;
                return Pack(
                    symbol,
                    NiftyZoneFormula.RoundLine(close),
                    quality,
                    LastSessionCloseInstant(symbol));
            }

            return Pack(symbol, 0, QuoteSourceQuality.Placeholder);
        }

        if (symbol.PreviousClose > 0)
        {
            return Pack(
                symbol,
                NiftyZoneFormula.RoundLine(symbol.PreviousClose),
                symbol.HistoryLoaded ? QuoteSourceQuality.VerifiedClose : QuoteSourceQuality.FallbackClose,
                LastSessionCloseInstant(symbol));
        }

        var fallbackClose = ResolveCloseFallback(symbol, fields);
        if (fallbackClose > 0)
        {
            return Pack(
                symbol,
                NiftyZoneFormula.RoundLine(fallbackClose),
                QuoteSourceQuality.FallbackClose,
                LastSessionCloseInstant(symbol));
        }

        return Pack(
            symbol,
            NiftyZoneFormula.RoundLine(symbol.Last),
            QuoteSourceQuality.SeedData,
            LastSessionCloseInstant(symbol));
    }

    public static bool IsDisplayVerified(QuoteSourceQuality quality) =>
        quality is QuoteSourceQuality.VerifiedLive
            or QuoteSourceQuality.VerifiedClose
            or QuoteSourceQuality.FallbackClose
            or QuoteSourceQuality.Rejected
            or QuoteSourceQuality.SeedData;

    /// <summary>15:30 IST on the last available daily candle (reference close "as of" time).</summary>
    public static DateTimeOffset? LastSessionCloseInstant(LiveSymbolState symbol)
    {
        if (symbol.Candles.Count > 0)
        {
            var last = symbol.Candles[^1];
            var istDate = TimeZoneInfo.ConvertTime(last.Time, Ist).Date;
            return SessionEndIst(istDate);
        }

        return null;
    }

    public static DateTimeOffset SessionEndIst(DateTime istCalendarDate) =>
        new DateTimeOffset(
            istCalendarDate.Year,
            istCalendarDate.Month,
            istCalendarDate.Day,
            15,
            30,
            0,
            TimeSpan.FromHours(5.5));

    private static QuoteDisplay Pack(
        LiveSymbolState symbol,
        decimal last,
        QuoteSourceQuality quality,
        DateTimeOffset? quoteTimestamp = null) =>
        new(last, quality, quoteTimestamp ?? FallbackTimestamp(symbol, quality));

    private static DateTimeOffset? FallbackTimestamp(LiveSymbolState symbol, QuoteSourceQuality quality)
    {
        if (quality == QuoteSourceQuality.VerifiedLive)
        {
            return symbol.DataTimestamp ?? DateTimeOffset.UtcNow;
        }

        if (quality is QuoteSourceQuality.VerifiedClose
            or QuoteSourceQuality.FallbackClose
            or QuoteSourceQuality.Rejected
            or QuoteSourceQuality.SeedData)
        {
            return LastSessionCloseInstant(symbol) ?? symbol.DataTimestamp;
        }

        return null;
    }

    private static decimal ResolveCloseFallback(
        LiveSymbolState symbol,
        BoardQuoteVerification.LiveSymbolFields fields)
    {
        if (symbol.PreviousClose > 0)
        {
            return symbol.PreviousClose;
        }

        var priceAnchor = BoardQuoteVerification.PriceAnchor(fields);
        if (priceAnchor > 0)
        {
            return priceAnchor;
        }

        if (symbol.Candles.Count > 0 && symbol.Candles[^1].Close > 0)
        {
            return symbol.Candles[^1].Close;
        }

        return 0;
    }

    private static decimal ResolveIndexClose(LiveSymbolState symbol)
    {
        if (symbol.PreviousClose > 0)
        {
            return symbol.PreviousClose;
        }

        if (symbol.Candles.Count > 0 && symbol.Candles[^1].Close > 0)
        {
            return symbol.Candles[^1].Close;
        }

        if (symbol.YesterdayHigh > 0 && symbol.YesterdayLow > 0)
        {
            return (symbol.YesterdayHigh + symbol.YesterdayLow) / 2m;
        }

        return symbol.Last;
    }
}

/// <summary>Immutable view of board symbol fields for quote resolution.</summary>
public readonly record struct LiveSymbolState(
    string Name,
    decimal Last,
    decimal TodayOpen,
    decimal YesterdayHigh,
    decimal YesterdayLow,
    decimal PreviousClose,
    int InstrumentToken,
    DateTimeOffset? DataTimestamp,
    bool HistoryLoaded,
    IReadOnlyList<DayCandle> Candles)
{
    public BoardQuoteVerification.LiveSymbolFields ToFields() =>
        new(Name, Last, TodayOpen, YesterdayHigh, YesterdayLow, PreviousClose, InstrumentToken, DataTimestamp);
}
