using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class MarketContextService
{
    private static readonly string[] Indices = ["NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX"];
    private readonly object _gate = new();
    private readonly MockMarketService _market;
    private readonly MStockAuthClient _broker;
    private readonly Dictionary<string, Queue<decimal>> _prices = new(StringComparer.OrdinalIgnoreCase);

    public MarketContextService(MockMarketService market, MStockAuthClient broker)
    {
        _market = market;
        _broker = broker;
    }

    public async Task<IReadOnlyList<MarketContextSnapshot>> BuildAsync(CancellationToken ct)
    {
        var board = _market.Snapshot();
        var contexts = board.Symbols.Select(symbol => BuildUnderlying(board, symbol)).ToList();
        AppendFutureSnapshots(contexts);
        if (!_broker.HasSession)
        {
            return contexts;
        }

        var requests = new List<OptionQuoteRequest>();
        foreach (var index in board.Symbols.Where(x => Indices.Contains(x.Name, StringComparer.OrdinalIgnoreCase)))
        {
            requests.AddRange(BuildAtmOptionRequests(index.Name, index.Last));
        }

        foreach (var stock in board.Symbols
            .Where(x => IndexUniverse.Nifty50.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
            .Take(12))
        {
            requests.AddRange(BuildAtmOptionRequests(stock.Name, stock.Last));
        }

        IReadOnlyList<OptionMarketQuote> quotes;
        try
        {
            using var optionBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            optionBudget.CancelAfter(TimeSpan.FromSeconds(8));
            quotes = await _broker.GetOptionQuotesAsync(requests, optionBudget.Token);
        }
        catch (OperationCanceledException)
        {
            return contexts;
        }

        foreach (var quote in quotes)
        {
            var request = requests.First(x => x.Strike == quote.Strike && x.Side == quote.Side
                && quote.TradingSymbol.Contains(x.Underlying, StringComparison.OrdinalIgnoreCase));
            contexts.Add(BuildOption(request, quote, NextWeeklyExpiry(request.Underlying, DateTimeOffset.UtcNow)));
        }
        return contexts;
    }

    private static IEnumerable<OptionQuoteRequest> BuildAtmOptionRequests(string underlying, decimal last)
    {
        var expiry = NextWeeklyExpiry(underlying, DateTimeOffset.UtcNow);
        var step = IndexUniverse.IsIndex(underlying)
            ? underlying is "BANKNIFTY" or "SENSEX" ? 100m : 50m
            : StockStrikeStep(last);
        var atm = Math.Round(last / step, MidpointRounding.AwayFromZero) * step;
        for (var offset = -1; offset <= 1; offset++)
        {
            yield return new(underlying, expiry.Date, atm + offset * step, "CE");
            yield return new(underlying, expiry.Date, atm + offset * step, "PE");
        }
    }

    private static decimal StockStrikeStep(decimal last) =>
        last switch
        {
            < 500m => 5m,
            < 2000m => 10m,
            < 5000m => 20m,
            _ => 50m,
        };

    private static void AppendFutureSnapshots(List<MarketContextSnapshot> contexts)
    {
        foreach (var underlying in contexts.Where(x => x.InstrumentType is "INDEX" or "STOCK").ToList())
        {
            contexts.Add(new(
                $"{underlying.Symbol} FUT",
                "FUTURE",
                underlying.Last,
                underlying.Quote,
                underlying.Evidence));
        }
    }

    public static MarketEvidence CalculateEvidence(
        IReadOnlyList<decimal> prices,
        decimal last,
        decimal todayOpen,
        decimal b1,
        decimal s,
        decimal s1,
        decimal b2,
        decimal s2)
    {
        var clean = prices.Where(x => x > 0).ToList();
        decimal? fast = clean.Count >= 3 ? Ema(clean, 3) : null;
        decimal? slow = clean.Count >= 5 ? Ema(clean, 5) : null;
        decimal? momentum = clean.Count >= 2 ? (last / clean[0] - 1m) * 100m : null;
        decimal? volatility = RealizedVolatility(clean);
        var trend = fast is null || slow is null ? "UNKNOWN" : fast > slow ? "UP" : fast < slow ? "DOWN" : "FLAT";
        var zone = last >= b1 ? "ABOVE_B1" : last <= s1 ? "BELOW_S1" : last >= s ? "UPPER_RANGE" : "LOWER_RANGE";
        var regime = volatility is null
            ? "UNKNOWN"
            : volatility >= 35m ? "VOLATILE"
            : trend is "UP" or "DOWN" && Math.Abs(momentum ?? 0) >= 0.15m ? "TRENDING"
            : volatility <= 8m ? "QUIET"
            : "RANGE";
        var facts = new List<string>
        {
            $"todayOpen={todayOpen.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"B1={b1.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"S={s.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"S1={s1.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"B2={b2.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"S2={s2.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        };
        if (fast is not null) facts.Add($"EMA3={fast:0.####}");
        if (slow is not null) facts.Add($"EMA5={slow:0.####}");
        if (momentum is not null) facts.Add($"momentumPct={momentum:0.####}");
        if (volatility is not null) facts.Add($"realizedVolPct={volatility:0.####}");
        return new(fast, slow, momentum, volatility, trend, zone, regime, facts);
    }

    public static DateTimeOffset NextWeeklyExpiry(string underlying, DateTimeOffset now)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(now, ist);
        var expiryDay = underlying.ToUpperInvariant() switch
        {
            "BANKNIFTY" => DayOfWeek.Wednesday,
            "SENSEX" => DayOfWeek.Friday,
            _ => DayOfWeek.Tuesday,
        };
        var days = ((int)expiryDay - (int)local.DayOfWeek + 7) % 7;
        if (days == 0 && local.TimeOfDay >= new TimeSpan(15, 20, 0)) days = 7;
        var date = local.Date.AddDays(days);
        return new DateTimeOffset(date, ist.GetUtcOffset(date));
    }

    private MarketContextSnapshot BuildUnderlying(BoardResponse board, SymbolSnapshot symbol)
    {
        var timestamp = symbol.DataTimestamp ?? board.UpdatedAt;
        var quote = Metadata(
            board.Feed,
            symbol.Exchange,
            symbol.InstrumentToken,
            timestamp,
            symbol.Bid,
            symbol.Ask,
            symbol.Volume,
            symbol.OpenInterest,
            board.Feed.Contains("live", StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<decimal> prices;
        lock (_gate)
        {
            if (!_prices.TryGetValue(symbol.Name, out var queue))
            {
                queue = new();
                foreach (var seed in new[] { symbol.PreviousClose, symbol.TodayOpen, symbol.Last }.Where(x => x > 0))
                {
                    queue.Enqueue(seed);
                }
                _prices[symbol.Name] = queue;
            }
            else if (queue.Count == 0 || queue.Last() != symbol.Last)
            {
                queue.Enqueue(symbol.Last);
                while (queue.Count > 60) queue.Dequeue();
            }
            prices = queue.ToList();
        }
        return new(
            symbol.Name,
            IndexUniverse.IsIndex(symbol.Name) ? "INDEX" : "STOCK",
            symbol.Last,
            quote,
            CalculateEvidence(prices, symbol.Last, symbol.TodayOpen, symbol.B1, symbol.S, symbol.S1, symbol.B2, symbol.S2));
    }

    private static MarketContextSnapshot BuildOption(
        OptionQuoteRequest request,
        OptionMarketQuote quote,
        DateTimeOffset expiry)
    {
        var timestamp = quote.Timestamp ?? DateTimeOffset.UtcNow;
        var metadata = Metadata(
            "m.Stock Type A",
            quote.TradingSymbol.Split(':')[0],
            null,
            timestamp,
            quote.Bid,
            quote.Ask,
            quote.Volume,
            quote.OpenInterest,
            true);
        var change = quote.PreviousClose > 0 ? (quote.Last / quote.PreviousClose - 1m) * 100m : (decimal?)null;
        var evidence = new MarketEvidence(
            null, null, change, null, change > 0 ? "UP" : change < 0 ? "DOWN" : "UNKNOWN",
            "OPTION_PREMIUM", "DERIVATIVE",
            [$"strike={quote.Strike:0.##}", $"right={quote.Side}", $"expiry={expiry:O}"]);
        return new(
            $"{request.Underlying} {expiry:yyyyMMdd} {quote.Strike:0.##} {quote.Side}",
            "OPTION",
            quote.Last,
            metadata,
            evidence);
    }

    private static QuoteMetadata Metadata(
        string source,
        string exchange,
        int? token,
        DateTimeOffset timestamp,
        decimal? bid,
        decimal? ask,
        long? volume,
        long? oi,
        bool live)
    {
        var age = Math.Max(0, (int)(DateTimeOffset.UtcNow - timestamp).TotalSeconds);
        var missing = new List<string>();
        if (bid is null) missing.Add("bid");
        if (ask is null) missing.Add("ask");
        if (volume is null) missing.Add("volume");
        if (oi is null) missing.Add("openInterest");
        if (!live) missing.Add("liveQuote");
        return new(source, exchange, token, timestamp, age, live && age <= 15, bid, ask, volume, oi, missing);
    }

    private static decimal Ema(IReadOnlyList<decimal> values, int period)
    {
        var multiplier = 2m / (period + 1);
        var ema = values[0];
        for (var i = 1; i < values.Count; i++) ema = (values[i] - ema) * multiplier + ema;
        return decimal.Round(ema, 6);
    }

    private static decimal? RealizedVolatility(IReadOnlyList<decimal> prices)
    {
        if (prices.Count < 3) return null;
        var returns = new List<double>();
        for (var i = 1; i < prices.Count; i++) returns.Add(Math.Log((double)(prices[i] / prices[i - 1])));
        var mean = returns.Average();
        var variance = returns.Sum(x => Math.Pow(x - mean, 2)) / Math.Max(1, returns.Count - 1);
        return decimal.Round((decimal)(Math.Sqrt(variance) * Math.Sqrt(252d * 75d) * 100d), 4);
    }
}
