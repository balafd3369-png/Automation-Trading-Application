using System.Globalization;
using TradeWithBeast.Broker;
using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class MockMarketService : IHostedService, IDisposable
{
    private static readonly TimeSpan ChartRequestBudget = TimeSpan.FromSeconds(12);

    private readonly object _gate = new();
    private readonly Random _rng = new(1270);
    private readonly List<LiveSymbol> _symbols = [];
    private readonly SeedStore _store;
    private readonly BrokerConnectionManager _brokers;
    private readonly EquitySymbolCatalog _catalog;
    private readonly ILogger<MockMarketService> _log;
    private readonly Dictionary<string, List<IntradayTickBuilder.Tick>> _intradayTicks = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private CancellationTokenSource? _historyCts;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private string _feed = "awaiting broker login";
    private int _busy;
    private bool _demoZoneSeedsApplied;

    private IBrokerMarketClient Broker => _brokers.Market;

    public MockMarketService(
        IWebHostEnvironment env,
        BrokerConnectionManager brokers,
        EquitySymbolCatalog catalog,
        ILogger<MockMarketService> log)
    {
        _store = new SeedStore(Path.Combine(env.ContentRootPath, "data", "mock-board.json"));
        _brokers = brokers;
        _catalog = catalog;
        _log = log;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        var cts = _historyCts;
        _historyCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        cts.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _symbols.Clear();
            foreach (var seed in _store.LoadOrSeed())
            {
                _symbols.Add(new LiveSymbol
                {
                    Name = seed.Name,
                    Last = seed.LastSeed,
                });
            }

            _updatedAt = DateTimeOffset.UtcNow;
        }

        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
        _historyCts = new CancellationTokenSource();
        _ = Task.Run(() => LoadHistoryLoopAsync(_historyCts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    /// <summary>Recompute B1/B2/S1/S2 from the active broker's daily OHLC (not demo seed).</summary>
    public void NotifyBrokerSessionEstablished()
    {
        lock (_gate)
        {
            foreach (var symbol in _symbols)
            {
                symbol.HistoryLoaded = false;
                symbol.YesterdayHigh = 0;
                symbol.YesterdayLow = 0;
                symbol.HighTwoDaysAgo = 0;
                symbol.LowTwoDaysAgo = 0;
            }

            _intradayTicks.Clear();
        }

        _ = Task.Run(() => PrimeBrokerZoneHistoryAsync(CancellationToken.None));
    }

    public BoardResponse Snapshot()
    {
        lock (_gate)
        {
            var rows = _symbols.Select(ToSnapshot).ToList();
            return new BoardResponse(
                new BoardCounts(
                    rows.Count(r => r.Mark == NiftyZoneFormula.Buying),
                    rows.Count(r => r.Mark == NiftyZoneFormula.Selling),
                    rows.Count(r => r.Mark == NiftyZoneFormula.RangeBound)),
                rows,
                _updatedAt,
                _feed);
        }
    }

    public ChartResponse? Chart(string name) => ChartAsync(name, "day", CancellationToken.None).GetAwaiter().GetResult();

    public IReadOnlyList<SymbolSearchHit> SearchSymbols(string query, int limit = 24) =>
        _catalog.Search(query, limit);

    public async Task<SymbolSnapshot?> EnsureSymbolAsync(string name, CancellationToken ct)
    {
        var normalized = name.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        LiveSymbol? existingSymbol;
        lock (_gate)
        {
            existingSymbol = _symbols.FirstOrDefault(s => s.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        if (existingSymbol is not null)
        {
            await TryLoadZoneHistoryAsync(existingSymbol, ct);
            lock (_gate)
            {
                existingSymbol = _symbols.FirstOrDefault(s => s.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                return existingSymbol is null ? null : ToSnapshot(existingSymbol);
            }
        }

        if (!IndexUniverse.IsIndex(normalized) && !_catalog.Contains(normalized))
        {
            return null;
        }

        IndexUniverse.TryRegisterEquity(normalized, out _);
        QuoteSnap? quote = null;
        if (Broker.HasSession && !IndexUniverse.IsIndex(normalized))
        {
            try
            {
                quote = await Broker.GetBoardQuoteAsync(normalized, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Broker quote unavailable while adding {Symbol}", normalized);
            }
        }

        var live = new LiveSymbol
        {
            Name = normalized,
            Last = quote?.Last > 0 ? quote.Last : 100m,
            TodayOpen = quote?.Open > 0 ? quote.Open : 100m,
            PreviousClose = quote?.Close > 0 ? quote.Close : 100m,
            InstrumentToken = quote?.InstrumentToken ?? 0,
            Exchange = string.IsNullOrWhiteSpace(quote?.Exchange) ? "NSE" : quote!.Exchange,
            Bid = quote?.Bid,
            Ask = quote?.Ask,
            Volume = quote?.Volume,
            OpenInterest = quote?.OpenInterest,
            DataTimestamp = quote?.Timestamp ?? DateTimeOffset.UtcNow,
        };

        lock (_gate)
        {
            var again = _symbols.FirstOrDefault(s => s.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (again is not null)
            {
                return ToSnapshot(again);
            }

            _symbols.Add(live);
            _updatedAt = DateTimeOffset.UtcNow;
        }

        await TryLoadZoneHistoryAsync(live, ct);
        lock (_gate)
        {
            var added = _symbols.FirstOrDefault(s => s.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            return added is null ? null : ToSnapshot(added);
        }
    }

    public async Task<ChartResponse?> ChartAsync(string name, string interval, CancellationToken ct)
    {
        LiveSymbol? symbol;
        lock (_gate)
        {
            symbol = _symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (symbol is null)
        {
            var ensured = await EnsureSymbolAsync(name, ct);
            if (ensured is null)
            {
                return null;
            }

            lock (_gate)
            {
                symbol = _symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (symbol is null)
        {
            return null;
        }

        await TryLoadZoneHistoryAsync(symbol, ct);
        lock (_gate)
        {
            symbol = _symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (symbol is null)
        {
            return null;
        }

        var mapped = interval switch
        {
            "1m" or "minute" => "minute",
            "15m" or "15minute" => "15minute",
            "1h" or "60minute" => "60minute",
            "1D" or "day" => "day",
            _ => "5minute",
        };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ChartRequestBudget);
        try
        {
            return await ChartAsyncCore(symbol, name, mapped, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Chart {Name} {Interval} timed out after {Seconds}s; using fallback bars.", name, mapped, ChartRequestBudget.TotalSeconds);
            return BuildChartFallback(symbol, mapped);
        }
    }

    private async Task<ChartResponse> ChartAsyncCore(LiveSymbol symbol, string name, string mapped, CancellationToken ct)
    {
        IReadOnlyList<DayCandle> live = [];
        var token = symbol.InstrumentToken;
        var exchange = symbol.Exchange;
        var resolvedExchange = exchange;
        if (Broker.HasSession && (token != 0 || IndexUniverse.IsIndex(name)))
        {
            var exchanges = new List<string>();
            if (!string.IsNullOrWhiteSpace(exchange))
            {
                exchanges.Add(exchange);
            }

            if (!exchanges.Contains("NSE", StringComparer.OrdinalIgnoreCase))
            {
                exchanges.Add("NSE");
            }

            if (symbol.Name.Equals("SENSEX", StringComparison.OrdinalIgnoreCase)
                && !exchanges.Contains("BSE", StringComparer.OrdinalIgnoreCase))
            {
                exchanges.Insert(0, "BSE");
            }

            var loaded = await LoadBrokerIntradayCandlesAsync(symbol.Name, mapped, exchanges, token, ct);
            live = loaded.Candles;
            if (live.Count > 0)
            {
                resolvedExchange = loaded.Exchange;
            }

            _log.LogInformation("Chart {Name} {Interval} token={Token} bars={Bars}", name, mapped, token, live.Count);
        }

        SymbolSnapshot snap;
        lock (_gate)
        {
            snap = ToSnapshot(symbol);
        }

        List<ChartBar> bars;
        if (live.Count > 0)
        {
            bars = ChartSessionMerger.ToBars(live);
            if (Broker.HasSession && mapped != "day")
            {
                var nowIst = ChartSessionMerger.IstNow();
                var expectedToday = ChartSessionMerger.MinExpectedTodayBars(nowIst, mapped);
                var todayInHistorical = CountTodayBars(bars);
                var skipMerge = todayInHistorical + 1 >= expectedToday || bars.Count > 180;
                if (!skipMerge)
                {
                    bars = await MergeBrokerTodaySessionAsync(
                        symbol.Name,
                        snap,
                        mapped,
                        resolvedExchange,
                        token,
                        bars,
                        ct);
                }
            }
        }
        else if (mapped == "day" && symbol.Candles.Count > 0)
        {
            bars = symbol.Candles
                .GroupBy(c => c.Time.ToUnixTimeSeconds())
                .Select(g => g.Last())
                .OrderBy(c => c.Time)
                .Select(c => new ChartBar(c.Time.ToUnixTimeSeconds(), c.Open, c.High, c.Low, c.Close))
                .ToList();
        }
        else if (Broker.HasSession)
        {
            bars = await ResolveTodayBarsAsync(name, snap, mapped, exchange, token, [], ct);
        }
        else
        {
            bars = BuildSyntheticIntraday(snap, mapped);
        }

        if (mapped != "day")
        {
            bars = ChartSessionMerger.TrimIntradayBarsBeforeSessionOpen(bars);
            bars = TrimPostMarketIntradayBars(bars);
        }

        bars = SanitizeBars(bars, snap.Last);
        if (mapped != "day" && Broker.HasSession)
        {
            var nowIst = ChartSessionMerger.IstNow();
            var expectedToday = ChartSessionMerger.MinExpectedTodayBars(nowIst, mapped);
            var todayCount = CountTodayBars(bars);
            if (expectedToday > 1 && todayCount + 1 < expectedToday && todayCount < 8)
            {
                var sessionBars = await TryLoadTodaySessionBarsAsync(
                    symbol.Name,
                    resolvedExchange.Length > 0 ? resolvedExchange : exchange,
                    token,
                    mapped,
                    ct);
                if (sessionBars.Count > todayCount)
                {
                    _log.LogWarning(
                        "Chart {Name} {Interval} backfilled today: had {Had}, expected ~{Expected}, loaded {Loaded}.",
                        name,
                        mapped,
                        todayCount,
                        expectedToday,
                        sessionBars.Count);
                    bars = ChartSessionMerger.MergeHistoricalWithToday(bars, sessionBars);
                    bars = SanitizeBars(bars, snap.Last);
                }
            }
        }

        if (mapped != "day" && bars.Count < 8 && !Broker.HasSession)
        {
            var synthetic = BuildSyntheticIntraday(snap, mapped);
            if (synthetic.Count > bars.Count)
            {
                bars = synthetic;
            }
        }

        if (bars.Count == 0)
        {
            return BuildChartFallback(symbol, mapped);
        }

        lock (_gate)
        {
            snap = ToSnapshot(symbol);
            return new ChartResponse(snap.Name, bars, snap.Last, snap.B2, snap.B1, snap.S, snap.S1, snap.S2);
        }
    }

    private ChartResponse BuildChartFallback(LiveSymbol symbol, string mapped)
    {
        lock (_gate)
        {
            var snap = ToSnapshot(symbol);
            List<ChartBar> bars;
            if (_intradayTicks.TryGetValue(symbol.Name, out var ticks) && ticks.Count > 0)
            {
                bars = IntradayTickBuilder.BuildBars(ticks, mapped, snap.TodayOpen);
            }
            else
            {
                bars = BuildSyntheticIntraday(snap, mapped);
            }

            if (mapped != "day")
            {
                bars = ChartSessionMerger.TrimIntradayBarsBeforeSessionOpen(bars);
                bars = TrimPostMarketIntradayBars(bars);
            }

            bars = SanitizeBars(bars, snap.Last);
            if (bars.Count == 0)
            {
                bars = BuildSyntheticIntraday(snap, mapped);
            }

            return new ChartResponse(snap.Name, bars, snap.Last, snap.B2, snap.B1, snap.S, snap.S1, snap.S2);
        }
    }

    /// <summary>Extended history for backtests (default chart API only keeps ~8 sessions on 5m).</summary>
    public async Task<ChartResponse?> ChartForBacktestAsync(
        string name,
        string interval,
        int lookbackCalendarDays,
        CancellationToken ct)
    {
        if (lookbackCalendarDays <= 0)
        {
            return await ChartAsync(name, interval, ct);
        }

        LiveSymbol? symbol;
        lock (_gate)
        {
            symbol = _symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (symbol is null)
        {
            return null;
        }

        var mapped = interval switch
        {
            "1m" or "minute" => "minute",
            "15m" or "15minute" => "15minute",
            "1h" or "60minute" => "60minute",
            "1D" or "day" => "day",
            _ => "5minute",
        };

        if (mapped == "day")
        {
            return await ChartAsync(name, interval, ct);
        }

        var token = symbol.InstrumentToken;
        var exchange = symbol.Exchange;
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
        var toIst = new DateTimeOffset(
            nowIst.Date.AddHours(15).AddMinutes(30),
            ist.GetUtcOffset(nowIst.Date.AddHours(15).AddMinutes(30)));
        var fromIst = new DateTimeOffset(
            nowIst.Date.AddDays(-lookbackCalendarDays).AddHours(9).AddMinutes(15),
            ist.GetUtcOffset(nowIst.Date.AddDays(-lookbackCalendarDays).AddHours(9).AddMinutes(15)));

        IReadOnlyList<DayCandle> live = [];
        var resolvedExchange = exchange;
        if (Broker.HasSession && token != 0)
        {
            var exchanges = new List<string>();
            if (!string.IsNullOrWhiteSpace(exchange))
            {
                exchanges.Add(exchange);
            }

            if (!exchanges.Contains("NSE", StringComparer.OrdinalIgnoreCase))
            {
                exchanges.Add("NSE");
            }

            if (symbol.Name.Equals("SENSEX", StringComparison.OrdinalIgnoreCase)
                && !exchanges.Contains("BSE", StringComparer.OrdinalIgnoreCase))
            {
                exchanges.Insert(0, "BSE");
            }

            var chunkDays = mapped is "minute" ? 3 : mapped.Contains("5") ? 5 : 10;
            foreach (var ex in exchanges)
            {
                try
                {
                    var attempt = await HistoricalChartLoader.LoadChunkedForBoardAsync(
                        Broker,
                        name,
                        ex,
                        token,
                        mapped,
                        fromIst,
                        toIst,
                        chunkDays,
                        ct);
                    if (attempt.Count > 0)
                    {
                        live = attempt;
                        resolvedExchange = ex;
                        break;
                    }
                }
                catch (Exception exn)
                {
                    _log.LogWarning(exn, "Backtest chart failed for {Name} {Exchange} {Interval}", name, ex, mapped);
                }
            }

            _log.LogInformation(
                "Backtest chart {Name} {Interval} lookback={Days}d token={Token} bars={Bars}",
                name,
                mapped,
                lookbackCalendarDays,
                token,
                live.Count);
        }

        SymbolSnapshot snap;
        lock (_gate)
        {
            snap = ToSnapshot(symbol);
        }

        List<ChartBar> bars;
        if (live.Count > 0)
        {
            bars = ChartSessionMerger.ToBars(live);
        }
        else
        {
            return await ChartAsync(name, interval, ct);
        }

        bars = ChartSessionMerger.TrimIntradayBarsBeforeSessionOpen(bars);
        bars = SanitizeBars(bars, snap.Last);

        lock (_gate)
        {
            snap = ToSnapshot(symbol);
            return new ChartResponse(snap.Name, bars, snap.Last, snap.B2, snap.B1, snap.S, snap.S1, snap.S2);
        }
    }

    public async Task<OptionChartResponse?> OptionChartAsync(
        string underlying,
        DateTime expiry,
        decimal strike,
        string side,
        string interval,
        CancellationToken ct)
    {
        LiveSymbol? symbol;
        lock (_gate)
        {
            symbol = _symbols.FirstOrDefault(s => s.Name.Equals(underlying, StringComparison.OrdinalIgnoreCase));
        }
        if (symbol is null || !Broker.HasSession || strike <= 0 || expiry == default)
        {
            return null;
        }

        var quote = await Broker.GetOptionQuoteAsync(underlying, expiry, strike, side, ct);
        if (quote is null || quote.InstrumentToken <= 0)
        {
            return null;
        }

        var optionLast = quote.Last > 0 ? quote.Last : quote.Close;
        if (optionLast <= 0)
        {
            return null;
        }

        var mapped = interval switch
        {
            "1m" or "minute" => "minute",
            "15m" or "15minute" => "15minute",
            "1h" or "60minute" => "60minute",
            "1D" or "day" => "day",
            _ => "5minute",
        };
        IReadOnlyList<DayCandle> candles = [];
        try
        {
            candles = await Broker.GetCandlesAsync(
                quote.Exchange,
                quote.InstrumentToken,
                mapped,
                ct);
        }
        catch (Exception exn)
        {
            _log.LogWarning(exn, "Option chart failed for {TradingSymbol} {Interval}", quote.TradingSymbol, mapped);
        }

        var bars = ChartSessionMerger.ToBars(candles);
        if (mapped != "day" && !ChartSessionMerger.LastBarIsToday(bars))
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            var sessionDate = nowIst.Date;
            var fromIst = new DateTimeOffset(
                sessionDate.AddHours(9).AddMinutes(15),
                ist.GetUtcOffset(sessionDate.AddHours(9).AddMinutes(15)));
            var toIst = new DateTimeOffset(
                sessionDate.AddHours(15).AddMinutes(30),
                ist.GetUtcOffset(sessionDate.AddHours(15).AddMinutes(30)));
            IReadOnlyList<DayCandle> todayCandles = [];
            try
            {
                todayCandles = await Broker.GetIntradayCandlesAsync(
                    quote.Exchange,
                    quote.InstrumentToken,
                    mapped,
                    ct);
                if (todayCandles.Count == 0 && mapped != "minute")
                {
                    todayCandles = await Broker.GetIntradayCandlesAsync(
                        quote.Exchange,
                        quote.InstrumentToken,
                        "minute",
                        ct);
                }

                if (todayCandles.Count == 0)
                {
                    todayCandles = await Broker.GetCandlesAsync(
                        quote.Exchange,
                        quote.InstrumentToken,
                        mapped,
                        fromIst,
                        toIst,
                        ct);
                }
            }
            catch (Exception exn)
            {
                _log.LogWarning(exn, "Today's option candles failed for {TradingSymbol}", quote.TradingSymbol);
            }

            var todayBars = mapped == "minute"
                ? ChartSessionMerger.ToBars(todayCandles)
                : ChartSessionMerger.ResampleToday(todayCandles, mapped);
            if (todayBars.Count > 0)
            {
                bars = ChartSessionMerger.MergeHistoricalWithToday(bars, todayBars);
            }
            else if (nowIst <= toIst)
            {
                var bucket = ChartSessionMerger.BucketStartIst(nowIst, ChartSessionMerger.StepMinutes(mapped));
                var open = quote.Open > 0 ? quote.Open : optionLast;
                var current = new ChartBar(
                    bucket.ToUnixTimeSeconds(),
                    open,
                    Math.Max(open, optionLast),
                    Math.Min(open, optionLast),
                    optionLast);
                bars = ChartSessionMerger.MergeHistoricalWithToday(bars, [current]);
            }
        }
        if (mapped != "day")
        {
            bars = ChartSessionMerger.TrimIntradayBarsBeforeSessionOpen(bars);
        }

        bars = SanitizeBars(bars, optionLast);

        SymbolSnapshot snap;
        lock (_gate)
        {
            snap = ToSnapshot(symbol);
        }
        var expiryLabel = expiry.ToString("dd MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        var right = side.Equals("PE", StringComparison.OrdinalIgnoreCase) ? "PE" : "CE";
        var name = $"{underlying.ToUpperInvariant()} {expiryLabel} {strike:0.##} {right}";
        return new OptionChartResponse(
            name,
            quote.TradingSymbol,
            bars,
            optionLast,
            quote.Open,
            quote.Close,
            snap.B2,
            snap.B1,
            snap.S,
            snap.S1,
            snap.S2);
    }

    public async Task<IReadOnlyList<OptionChainItem>> OptionChainAsync(
        string underlying,
        DateTime expiry,
        CancellationToken ct)
    {
        LiveSymbol? symbol;
        lock (_gate)
        {
            symbol = _symbols.FirstOrDefault(s => s.Name.Equals(underlying, StringComparison.OrdinalIgnoreCase));
        }
        if (symbol is null || !Broker.HasSession || expiry == default || symbol.Last <= 0)
        {
            return [];
        }

        var step = underlying.ToUpperInvariant() switch
        {
            "BANKNIFTY" => 100m,
            "SENSEX" => 100m,
            _ => 50m,
        };
        var atm = Math.Round(symbol.Last / step, 0, MidpointRounding.AwayFromZero) * step;
        var requests = new List<OptionQuoteRequest>();
        for (var offset = -8; offset <= 8; offset++)
        {
            var strike = atm + offset * step;
            if (strike <= 0)
            {
                continue;
            }
            requests.Add(new OptionQuoteRequest(underlying, expiry, strike, "CE"));
            requests.Add(new OptionQuoteRequest(underlying, expiry, strike, "PE"));
        }

        var quotes = await Broker.GetOptionQuotesAsync(requests, ct);
        return quotes
            .Select(quote =>
            {
                var changePercent = quote.PreviousClose > 0
                    ? (quote.Last - quote.PreviousClose) / quote.PreviousClose * 100m
                    : 0m;
                return new OptionChainItem(
                    quote.Strike,
                    quote.Side,
                    quote.TradingSymbol,
                    quote.Last,
                    quote.Open,
                    quote.PreviousClose,
                    Math.Round(changePercent, 2),
                    quote.Bid,
                    quote.Ask,
                    quote.Volume,
                    quote.OpenInterest,
                    quote.Timestamp);
            })
            .OrderBy(quote => quote.Strike)
            .ThenBy(quote => quote.Side)
            .ToList();
    }

    private static List<ChartBar> TrimPostMarketIntradayBars(IReadOnlyList<ChartBar> bars)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var sessionEndMinutes = 15 * 60 + 30;
        return bars
            .Where(b =>
            {
                var t = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist);
                var minutes = t.Hour * 60 + t.Minute;
                return minutes >= 9 * 60 + 15 && minutes < sessionEndMinutes;
            })
            .ToList();
    }

    private static List<ChartBar> SanitizeBars(IReadOnlyList<ChartBar> bars, decimal last)
    {
        var clean = new List<ChartBar>(bars.Count);
        foreach (var bar in bars.OrderBy(b => b.Time))
        {
            var open = bar.Open > 0 ? bar.Open : last;
            var close = bar.Close > 0 ? bar.Close : open;
            var high = Math.Max(bar.High, Math.Max(open, close));
            var low = Math.Min(bar.Low > 0 ? bar.Low : Math.Min(open, close), Math.Min(open, close));
            if (high <= low)
            {
                high = Math.Max(open, close) + 0.05m;
                low = Math.Min(open, close) - 0.05m;
            }

            clean.Add(new ChartBar(bar.Time, open, high, low, close));
        }

        if (clean.Count > 0 && last > 0)
        {
            var tail = clean[^1];
            clean[^1] = tail with
            {
                Close = last,
                High = Math.Max(tail.High, last),
                Low = Math.Min(tail.Low, last),
            };
        }

        return clean;
    }

    private async Task<List<ChartBar>> MergeBrokerTodaySessionAsync(
        string symbolName,
        SymbolSnapshot snap,
        string mapped,
        string exchange,
        int token,
        List<ChartBar> bars,
        CancellationToken ct)
    {
        var nowIst = ChartSessionMerger.IstNow();
        var fromIst = ChartSessionMerger.SessionStartIst(nowIst.Date);
        IReadOnlyList<DayCandle> minuteToday = [];
        try
        {
            var toIst = ChartSessionMerger.SessionEndIst(nowIst.Date);
            minuteToday = await LoadSessionCandlesAsync(exchange, token, "minute", fromIst, toIst, ct);
            if (minuteToday.Count == 0)
            {
                minuteToday = await LoadSessionCandlesAsync(exchange, token, "1minute", fromIst, toIst, ct);
            }
        }
        catch (Exception exn)
        {
            _log.LogWarning(exn, "Today minute candles failed for {Name}", symbolName);
        }

        var todayInHistorical = CountTodayBars(bars);
        var todayBars = await ResolveTodayBarsAsync(symbolName, snap, mapped, exchange, token, minuteToday, ct);
        if (todayBars.Count == 0)
        {
            return bars;
        }

        if (todayBars.Count > todayInHistorical)
        {
            _log.LogInformation(
                "Chart {Name} {Interval} today merge: broker history had {Had} today bar(s), session load {Have}.",
                symbolName,
                mapped,
                todayInHistorical,
                todayBars.Count);
        }

        return ChartSessionMerger.MergeHistoricalWithToday(bars, todayBars);
    }

    private static int CountTodayBars(IReadOnlyList<ChartBar> bars)
    {
        var today = ChartSessionMerger.IstToday();
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        return bars.Count(b => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).Date == today);
    }

    private async Task<List<ChartBar>> ResolveTodayBarsAsync(
        string name,
        SymbolSnapshot snap,
        string mapped,
        string exchange,
        int token,
        IReadOnlyList<DayCandle> minuteToday,
        CancellationToken ct)
    {
        var nowIst = ChartSessionMerger.IstNow();
        var expected = ChartSessionMerger.MinExpectedTodayBars(nowIst, mapped);
        var canLoad = Broker.HasSession && (token != 0 || IndexUniverse.IsIndex(name));
        List<ChartBar> best = [];

        void Consider(List<ChartBar> candidate, string source)
        {
            if (candidate.Count == 0)
            {
                return;
            }

            if (candidate.Count > best.Count)
            {
                best = candidate;
                _log.LogDebug(
                    "Chart {Name} {Interval} today candidate {Source}: {Count} bar(s).",
                    name,
                    mapped,
                    source,
                    candidate.Count);
            }
        }

        if (canLoad)
        {
            Consider(await TryLoadIntradayApiTodayBarsAsync(exchange, token, mapped, ct), "intraday-api");
            Consider(await TryLoadTodaySessionBarsAsync(name, exchange, token, mapped, ct), "session-historical");
        }

        Consider(ChartSessionMerger.ResampleToday(minuteToday, mapped), "minute-range");

        if (canLoad && best.Count < expected)
        {
            try
            {
                var fromIst = ChartSessionMerger.SessionStartIst(nowIst.Date);
                var chunked = await HistoricalChartLoader.LoadChunkedForBoardAsync(
                    Broker,
                    name,
                    exchange,
                    token,
                    "minute",
                    fromIst,
                    nowIst,
                    1,
                    ct);
                Consider(ChartSessionMerger.ResampleToday(chunked, mapped), "chunked-minute");
            }
            catch (Exception exn)
            {
                _log.LogDebug(exn, "Chunked minute session failed for {Name}", name);
            }
        }

        if (canLoad && best.Count < expected)
        {
            try
            {
                var intraday = await BrokerCandlesAsync(name, exchange, token, "minute", null, null, ct);
                Consider(ChartSessionMerger.ResampleToday(intraday, mapped), "historical-minute");
            }
            catch (Exception exn)
            {
                _log.LogDebug(exn, "Intraday candles failed for {Name}", name);
            }
        }

        if (best.Count > 0)
        {
            if (expected > 0 && best.Count < expected)
            {
                _log.LogWarning(
                    "Chart {Name} {Interval} today bars short: have {Have}, expected ~{Expected}.",
                    name,
                    mapped,
                    best.Count,
                    expected);
            }
            else
            {
                _log.LogInformation(
                    "Chart {Name} {Interval} using {Count} today bar(s) (expected ~{Expected}).",
                    name,
                    mapped,
                    best.Count,
                    expected);
            }

            return best;
        }

        List<ChartBar> todayBars;

        List<IntradayTickBuilder.Tick> ticks;
        lock (_gate)
        {
            if (!_intradayTicks.TryGetValue(name, out ticks!))
            {
                ticks = [];
                _intradayTicks[name] = ticks;
            }
        }

        todayBars = IntradayTickBuilder.BuildBars(ticks, mapped, snap.TodayOpen);
        if (todayBars.Count > 0)
        {
            _log.LogInformation("Chart {Name} {Interval} built {Count} today bars from live ticks.", name, mapped, todayBars.Count);
            return todayBars;
        }

        todayBars = ChartSessionMerger.BuildCurrentBar(snap, mapped, snap.Last);
        if (todayBars.Count > 0)
        {
            _log.LogWarning("Chart {Name} {Interval} using current bar only; intraday history unavailable.", name, mapped);
        }

        return todayBars;
    }

    private async Task<List<ChartBar>> TryLoadTodaySessionBarsAsync(
        string name,
        string exchange,
        int token,
        string mapped,
        CancellationToken ct)
    {
        var nowIst = ChartSessionMerger.IstNow();
        var fromIst = ChartSessionMerger.SessionStartIst(nowIst.Date);
        var toIst = ChartSessionMerger.IsCashSessionOpen(nowIst)
            ? nowIst
            : ChartSessionMerger.SessionEndIst(nowIst.Date);

        var expected = ChartSessionMerger.MinExpectedTodayBars(nowIst, mapped);
        List<ChartBar>? best = null;
        foreach (var alt in new[] { "minute", "1minute", mapped, "5minute" })
        {
            if (string.IsNullOrWhiteSpace(alt))
            {
                continue;
            }

            try
            {
                var raw = await LoadSessionCandlesAsync(exchange, token, alt, fromIst, toIst, ct);
                if (raw.Count == 0)
                {
                    continue;
                }

                var bars = string.Equals(alt, mapped, StringComparison.OrdinalIgnoreCase)
                    ? ChartSessionMerger.ToBars(raw)
                    : ChartSessionMerger.ResampleToday(raw, mapped);
                if (bars.Count == 0)
                {
                    continue;
                }

                if (best is null || bars.Count > best.Count)
                {
                    best = bars;
                }

                if (best.Count >= expected)
                {
                    break;
                }
            }
            catch (Exception exn)
            {
                _log.LogDebug(exn, "Session {Alt} candles failed for {Name}", alt, name);
            }
        }

        if (best is { Count: > 0 })
        {
            _log.LogInformation(
                "Chart {Name} {Interval} session load: {Count} today bar(s) (expected ~{Expected}).",
                name,
                mapped,
                best.Count,
                expected);
        }

        return best ?? [];
    }

    private async Task<List<ChartBar>> TryLoadIntradayApiTodayBarsAsync(
        string exchange,
        int token,
        string mapped,
        CancellationToken ct)
    {
        if (!Broker.HasSession)
        {
            return [];
        }

        List<ChartBar>? best = null;
        foreach (var alt in new[] { "minute", mapped, "5minute" })
        {
            if (string.IsNullOrWhiteSpace(alt))
            {
                continue;
            }

            try
            {
                var raw = await Broker.GetIntradayCandlesAsync(exchange, token, alt, ct);
                if (raw.Count == 0)
                {
                    continue;
                }

                var bars = string.Equals(alt, mapped, StringComparison.OrdinalIgnoreCase)
                    ? FilterTodayChartBars(ChartSessionMerger.ToBars(raw))
                    : ChartSessionMerger.ResampleToday(raw, mapped);
                if (bars.Count == 0)
                {
                    continue;
                }

                if (best is null || bars.Count > best.Count)
                {
                    best = bars;
                }
            }
            catch (Exception exn)
            {
                _log.LogDebug(exn, "Intraday API {Alt} failed for token {Token}", alt, token);
            }
        }

        return best ?? [];
    }

    private static List<ChartBar> FilterTodayChartBars(IReadOnlyList<ChartBar> bars)
    {
        var today = ChartSessionMerger.IstToday();
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        return bars
            .Where(b => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(b.Time), ist).Date == today)
            .ToList();
    }

    private void RecordIntradayTick(string name, decimal last, decimal todayOpen)
    {
        if (last <= 0)
        {
            return;
        }

        if (!ChartSessionMerger.IsCashSessionOpen(ChartSessionMerger.IstNow()))
        {
            return;
        }

        lock (_gate)
        {
            if (!_intradayTicks.TryGetValue(name, out var ticks))
            {
                ticks = [];
                _intradayTicks[name] = ticks;
            }

            if (ticks.Count == 0 && todayOpen > 0 && !Broker.HasSession)
            {
                IntradayTickBuilder.BootstrapFromOpen(ticks, todayOpen, last);
            }

            IntradayTickBuilder.Record(ticks, last);
        }
    }

    private async Task<(IReadOnlyList<DayCandle> Candles, string Exchange)> LoadBrokerIntradayCandlesAsync(
        string name,
        string mapped,
        IReadOnlyList<string> exchanges,
        int token,
        CancellationToken ct)
    {
        if (!Broker.HasSession || (token == 0 && !IndexUniverse.IsIndex(name)))
        {
            return ([], exchanges.FirstOrDefault() ?? "NSE");
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
        var sessionEndLocal = nowIst.Date.AddHours(15).AddMinutes(30);
        var toIst = nowIst > new DateTimeOffset(
            DateTime.SpecifyKind(sessionEndLocal, DateTimeKind.Unspecified),
            ist.GetUtcOffset(sessionEndLocal))
            ? new DateTimeOffset(
                DateTime.SpecifyKind(sessionEndLocal, DateTimeKind.Unspecified),
                ist.GetUtcOffset(sessionEndLocal))
            : nowIst;

        var lookbackDays = mapped switch
        {
            "minute" => 1,
            "5minute" => 5,
            "15minute" => 8,
            "60minute" => 14,
            "day" => 80,
            _ => 5,
        };
        var fromLocal = nowIst.Date.AddDays(-lookbackDays).AddHours(9).AddMinutes(15);
        var fromIst = new DateTimeOffset(
            DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified),
            ist.GetUtcOffset(fromLocal));

        var chunkDays = mapped is "minute" ? 1 : 4;
        foreach (var ex in exchanges)
        {
            try
            {
                if (mapped == "day")
                {
                    var day = await BrokerCandlesAsync(name, ex, token, mapped, null, null, ct);
                    if (day.Count > 0)
                    {
                        return (day, ex);
                    }

                    continue;
                }

                // Broker "intraday" endpoint is today-only; use it only for 1-day lookback (1m).
                if (mapped != "day" && lookbackDays <= 1)
                {
                    var fastInterval = mapped;
                    try
                    {
                        var intraday = await Broker.GetIntradayCandlesAsync(ex, token, fastInterval, ct);
                        if (intraday.Count >= 12)
                        {
                            return (intraday, ex);
                        }
                    }
                    catch (Exception exFast)
                    {
                        _log.LogDebug(exFast, "Fast intraday chart failed for {Name} {Exchange} {Interval}", name, ex, fastInterval);
                    }
                }

                var attempt = await HistoricalChartLoader.LoadChunkedForBoardAsync(
                    Broker,
                    name,
                    ex,
                    token,
                    mapped,
                    fromIst,
                    toIst,
                    chunkDays,
                    ct);
                if (attempt.Count > 0)
                {
                    return (attempt, ex);
                }
            }
            catch (Exception exn)
            {
                _log.LogWarning(exn, "Chart candles failed for {Name} {Exchange} {Interval}", name, ex, mapped);
            }
        }

        return ([], exchanges.FirstOrDefault() ?? "NSE");
    }

    private static List<ChartBar> BuildSyntheticIntraday(SymbolSnapshot snap, string interval)
    {
        var stepMinutes = interval switch
        {
            "minute" => 1,
            "15minute" => 15,
            "60minute" => 60,
            "day" => 1440,
            _ => 5,
        };

        if (interval == "day")
        {
            var day = DateTimeOffset.UtcNow;
            var open = snap.TodayOpen > 0 ? snap.TodayOpen : snap.Last;
            return
            [
                new ChartBar(day.AddDays(-1).ToUnixTimeSeconds(), open, Math.Max(open, snap.Last), Math.Min(open, snap.Last), snap.Last),
                new ChartBar(day.ToUnixTimeSeconds(), open, Math.Max(open, snap.Last), Math.Min(open, snap.Last), snap.Last),
            ];
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
        var sessionStart = now.Date.AddHours(9).AddMinutes(15);
        if (now < sessionStart)
        {
            sessionStart = sessionStart.AddDays(-1);
        }

        var tick = Math.Max(0.05m, snap.Last * 0.00035m);
        var price = snap.TodayOpen > 0 ? snap.TodayOpen : snap.Last;
        var rng = new Random(HashCode.Combine(snap.Name, interval, now.Date));
        var bars = new List<ChartBar>();
        for (var t = sessionStart; t <= now; t = t.AddMinutes(stepMinutes))
        {
            var move = ((decimal)rng.NextDouble() * 2m - 1m) * tick;
            var open = price;
            var close = Math.Clamp(open + move, snap.S2 * 0.995m, snap.B2 * 1.005m);
            var wick = tick * 0.6m;
            var high = Math.Max(open, close) + wick;
            var low = Math.Min(open, close) - wick;
            var utc = new DateTimeOffset(t, ist.GetUtcOffset(t));
            bars.Add(new ChartBar(utc.ToUnixTimeSeconds(), open, high, low, close));
            price = close;
        }

        if (bars.Count == 0)
        {
            var utcNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bars.Add(new ChartBar(utcNow - 300, snap.Last, snap.Last + tick, snap.Last - tick, snap.Last));
            bars.Add(new ChartBar(utcNow, snap.Last, snap.Last + tick, snap.Last - tick, snap.Last));
        }

        return bars;
    }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (Broker.HasSession)
            {
                await RefreshLiveAsync();
            }
            else
            {
                MockWalk();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Board tick failed; keeping last snapshot.");
            lock (_gate)
            {
                _feed = $"{_brokers.ActiveDisplayName} error";
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task RefreshLiveAsync()
    {
        using var refreshBudget = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        IReadOnlyDictionary<string, QuoteSnap> quotes;
        try
        {
            quotes = await Broker.GetOhlcAsync(refreshBudget.Token);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Live OHLC refresh timed out; keeping last snapshot.");
            return;
        }
        if (quotes.Count == 0)
        {
            _log.LogWarning("{Broker} OHLC returned no mapped symbols.", _brokers.ActiveDisplayName);
            if (!Broker.HasSession)
            {
                lock (_gate)
                {
                    _feed = "session expired — request OTP";
                }

                return;
            }

            var ltp = await Broker.GetLtpAsync(refreshBudget.Token);
            if (ltp.Count == 0)
            {
                lock (_gate)
                {
                    _feed = !Broker.HasSession ? "session expired — reconnect broker" : $"{_brokers.ActiveDisplayName} (quotes empty)";
                }

                return;
            }

            lock (_gate)
            {
                foreach (var symbol in _symbols)
                {
                    if (ltp.TryGetValue(symbol.Name, out var last) && last > 0)
                    {
                        symbol.Last = last;
                        RecordIntradayTick(symbol.Name, last, symbol.TodayOpen);
                    }
                }

                _feed = $"{_brokers.ActiveDisplayName} ltp";
                _updatedAt = DateTimeOffset.UtcNow;
            }

            return;
        }

        lock (_gate)
        {
            foreach (var symbol in _symbols)
            {
                if (!quotes.TryGetValue(symbol.Name, out var q))
                {
                    continue;
                }

                if (q.Last > 0)
                {
                    symbol.Last = q.Last;
                    RecordIntradayTick(symbol.Name, q.Last, q.Open > 0 ? q.Open : symbol.TodayOpen);
                }

                if (q.Open > 0)
                {
                    symbol.TodayOpen = q.Open;
                }

                if (q.Close > 0 && q.Close != q.Last)
                {
                    symbol.PreviousClose = q.Close;
                }

                if (q.InstrumentToken > 0)
                {
                    symbol.InstrumentToken = q.InstrumentToken;
                    symbol.Exchange = string.IsNullOrWhiteSpace(q.Exchange) ? "NSE" : q.Exchange;
                }
                symbol.Bid = q.Bid;
                symbol.Ask = q.Ask;
                symbol.Volume = q.Volume;
                symbol.OpenInterest = q.OpenInterest;
                symbol.DataTimestamp = q.Timestamp ?? DateTimeOffset.UtcNow;
            }

            _feed = $"{_brokers.ActiveDisplayName} live";
            _updatedAt = DateTimeOffset.UtcNow;
        }

        await RefreshIndexZoneHistoryIfNeededAsync(CancellationToken.None);
    }

    private async Task RefreshIndexZoneHistoryIfNeededAsync(CancellationToken ct)
    {
        if (!Broker.HasSession)
        {
            return;
        }

        List<LiveSymbol> pending;
        lock (_gate)
        {
            pending = _symbols
                .Where(s => IndexUniverse.IsIndex(s.Name) && !s.HistoryLoaded)
                .ToList();
        }

        foreach (var symbol in pending)
        {
            try
            {
                var candles = await BrokerDayCandlesAsync(symbol, ct);
                ApplyHistory(symbol, candles);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "Index zone refresh failed for {Name}", symbol.Name);
            }
        }
    }

    private async Task LoadHistoryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Broker.HasSession)
            {
                await Task.Delay(2000, ct);
                continue;
            }

            LiveSymbol? next;
            lock (_gate)
            {
                next = _symbols
                .Where(s => !s.HistoryLoaded && (s.InstrumentToken > 0 || IndexUniverse.IsIndex(s.Name)))
                .OrderBy(s => IndexUniverse.IsIndex(s.Name) ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            }

            if (next is null)
            {
                await Task.Delay(15000, ct);
                continue;
            }

            try
            {
                var candles = await BrokerDayCandlesAsync(next, ct);
                ApplyHistory(next, candles);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Historical load failed for {Name}", next.Name);
            }

            await Task.Delay(1100, ct);
        }
    }

    private async Task PrimeBrokerZoneHistoryAsync(CancellationToken ct)
    {
        await Task.Delay(800, ct);
        for (var round = 0; round < 40 && !ct.IsCancellationRequested; round++)
        {
            if (!Broker.HasSession)
            {
                return;
            }

            List<LiveSymbol> batch;
            lock (_gate)
            {
                batch = _symbols
                    .Where(s => !s.HistoryLoaded && (s.InstrumentToken > 0 || IndexUniverse.IsIndex(s.Name)))
                    .OrderBy(s => IndexUniverse.IsIndex(s.Name) ? 0 : 1)
                    .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();
            }

            if (batch.Count == 0)
            {
                return;
            }

            foreach (var symbol in batch)
            {
                try
                {
                    var candles = await BrokerDayCandlesAsync(symbol, ct);
                    ApplyHistory(symbol, candles);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Zone history prime failed for {Name}", symbol.Name);
                }
            }

            await Task.Delay(1200, ct);
        }
    }

    private async Task TryLoadZoneHistoryAsync(LiveSymbol symbol, CancellationToken ct)
    {
        if (symbol.HistoryLoaded)
        {
            return;
        }

        if (!Broker.HasSession)
        {
            return;
        }

        if (symbol.InstrumentToken <= 0 && !IndexUniverse.IsIndex(symbol.Name))
        {
            return;
        }

        try
        {
            var candles = await BrokerDayCandlesAsync(symbol, ct);
            ApplyHistory(symbol, candles);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Zone history load failed for {Name}", symbol.Name);
        }
    }

    private bool ApplyHistory(LiveSymbol symbol, IReadOnlyList<DayCandle> candles)
    {
        if (candles.Count < 2)
        {
            _log.LogDebug("Zone history for {Name}: only {Count} day candles; will retry.", symbol.Name, candles.Count);
            return false;
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).Date;
        var completed = AggregateCompletedTradingDays(candles, ist, today);
        if (completed.Count < 2)
        {
            _log.LogDebug("Zone history for {Name}: only {Count} completed sessions; will retry.", symbol.Name, completed.Count);
            return false;
        }

        var yesterday = completed[^1];
        var twoAgo = completed[^2];
        decimal todayOpen;
        lock (_gate)
        {
            symbol.YesterdayHigh = yesterday.High;
            symbol.YesterdayLow = yesterday.Low;
            symbol.HighTwoDaysAgo = twoAgo.High;
            symbol.LowTwoDaysAgo = twoAgo.Low;
            if (symbol.PreviousClose == 0 && yesterday.Close > 0)
            {
                symbol.PreviousClose = yesterday.Close;
            }
            symbol.Candles = candles.ToList();
            symbol.HistoryLoaded = true;
            var todayBar = candles.LastOrDefault(c => TimeZoneInfo.ConvertTime(c.Time, ist).Date == today);
            if (todayBar is not null && todayBar.Open > 0)
            {
                symbol.TodayOpen = todayBar.Open;
            }

            todayOpen = symbol.TodayOpen;
        }

        var b1 = NiftyZoneFormula.B1(yesterday.High, todayOpen);
        var b2 = NiftyZoneFormula.B2(twoAgo.High, yesterday.High);
        _log.LogInformation(
            "Zone levels for {Name}: yH={YH} yL={YL} open={Open} B1={B1} B2={B2} S1={S1} S2={S2}",
            symbol.Name,
            yesterday.High,
            yesterday.Low,
            todayOpen,
            NiftyZoneFormula.RoundLine(b1),
            NiftyZoneFormula.RoundLine(b2),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.S1(yesterday.Low, todayOpen)),
            NiftyZoneFormula.RoundLine(NiftyZoneFormula.S2(twoAgo.Low, yesterday.Low)));
        return true;
    }

    private async Task<IReadOnlyList<DayCandle>> BrokerDayCandlesAsync(LiveSymbol symbol, CancellationToken ct)
    {
        var day = await Broker.GetDayCandlesAsync(symbol.Exchange, symbol.InstrumentToken, ct);

        if (day.Count >= 3)
        {
            return day;
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var nowIst = ChartSessionMerger.IstNow();
        var fromIst = ChartSessionMerger.SessionStartIst(nowIst.Date.AddDays(-25));
        var toIst = ChartSessionMerger.IsCashSessionOpen(nowIst)
            ? nowIst
            : ChartSessionMerger.SessionEndIst(nowIst.Date);
        var exchange = string.IsNullOrWhiteSpace(symbol.Exchange) ? "NSE" : symbol.Exchange;
        if (symbol.Name.Equals("SENSEX", StringComparison.OrdinalIgnoreCase))
        {
            exchange = "BSE";
        }

        var intraday = await HistoricalChartLoader.LoadChunkedForBoardAsync(
            Broker,
            symbol.Name,
            exchange,
            symbol.InstrumentToken,
            "5minute",
            fromIst,
            toIst,
            5,
            ct);
        if (intraday.Count == 0)
        {
            return day;
        }

        var aggregated = AggregateCalendarDays(intraday, ist);
        _log.LogInformation(
            "Zone day candles for {Name}: daily API={Daily}, aggregated from 5m={Agg}",
            symbol.Name,
            day.Count,
            aggregated.Count);
        return aggregated.Count > day.Count ? aggregated : day;
    }

    private static List<DayCandle> AggregateCalendarDays(IReadOnlyList<DayCandle> candles, TimeZoneInfo ist) =>
        candles
            .GroupBy(c => TimeZoneInfo.ConvertTime(c.Time, ist).Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rows = g.OrderBy(c => c.Time).ToList();
                return new DayCandle(
                    rows[0].Time,
                    rows[0].Open,
                    rows.Max(r => r.High),
                    rows.Min(r => r.Low),
                    rows[^1].Close);
            })
            .ToList();

    private Task<IReadOnlyList<DayCandle>> BrokerCandlesAsync(
        string boardName,
        string exchange,
        int token,
        string interval,
        DateTimeOffset? fromIst,
        DateTimeOffset? toIst,
        CancellationToken ct)
    {
        return Broker.GetCandlesAsync(exchange, token, interval, fromIst, toIst, ct);
    }

    /// <summary>m.Stock intraday feed has full today session; historical often returns only a few today bars.</summary>
    private async Task<IReadOnlyList<DayCandle>> LoadSessionCandlesAsync(
        string exchange,
        int token,
        string interval,
        DateTimeOffset fromIst,
        DateTimeOffset toIst,
        CancellationToken ct)
    {
        if (!Broker.HasSession || token == 0)
        {
            return [];
        }

        try
        {
            var intraday = await Broker.GetIntradayCandlesAsync(exchange, token, interval, ct);
            if (intraday.Count > 0)
            {
                return intraday;
            }
        }
        catch (Exception exn)
        {
            _log.LogDebug(exn, "Intraday {Interval} failed for {Exchange}/{Token}", interval, exchange, token);
        }

        return await Broker.GetCandlesAsync(exchange, token, interval, fromIst, toIst, ct);
    }

    private static List<DayCandle> AggregateCompletedTradingDays(
        IReadOnlyList<DayCandle> candles,
        TimeZoneInfo ist,
        DateTime todayIst)
    {
        return candles
            .GroupBy(c => TimeZoneInfo.ConvertTime(c.Time, ist).Date)
            .Where(g => g.Key < todayIst)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rows = g.OrderBy(c => c.Time).ToList();
                return new DayCandle(
                    rows[0].Time,
                    rows[0].Open,
                    rows.Max(r => r.High),
                    rows.Min(r => r.Low),
                    rows[^1].Close);
            })
            .ToList();
    }

    private void MockWalk()
    {
        EnsureDemoZoneSeeds();
        lock (_gate)
        {
            foreach (var symbol in _symbols)
            {
                var step = symbol.Name switch
                {
                    "NIFTY" or "FINNIFTY" => 8m,
                    "BANKNIFTY" => 18m,
                    "SENSEX" => 40m,
                    _ => Math.Max(0.05m, symbol.TodayOpen * 0.0006m),
                };
                var next = symbol.Last + ((decimal)_rng.NextDouble() * 2m - 1m) * step;
                var floor = Math.Min(symbol.S2, symbol.TodayOpen) * 0.992m;
                var ceil = Math.Max(symbol.B2, symbol.TodayOpen) * 1.008m;
                symbol.Last = Math.Clamp(next, floor, ceil);
            }

            _feed = "awaiting broker login (demo prices)";
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnsureDemoZoneSeeds()
    {
        if (_demoZoneSeedsApplied || Broker.HasSession)
        {
            return;
        }

        lock (_gate)
        {
            if (_demoZoneSeedsApplied || Broker.HasSession)
            {
                return;
            }

            foreach (var seed in _store.LoadOrSeed())
            {
                var symbol = _symbols.FirstOrDefault(s => s.Name.Equals(seed.Name, StringComparison.OrdinalIgnoreCase));
                if (symbol is null)
                {
                    continue;
                }

                symbol.HighTwoDaysAgo = seed.HighTwoDaysAgo;
                symbol.LowTwoDaysAgo = seed.LowTwoDaysAgo;
                symbol.YesterdayHigh = seed.YesterdayHigh;
                symbol.YesterdayLow = seed.YesterdayLow;
                symbol.TodayOpen = seed.TodayOpen;
            }

            _demoZoneSeedsApplied = true;
        }
    }

    private SymbolSnapshot ToSnapshot(LiveSymbol s)
    {
        if (Broker.HasSession && !s.HistoryLoaded && s.YesterdayHigh > 0 && s.Last > 0
            && s.YesterdayHigh > s.Last * 1.04m)
        {
            lock (_gate)
            {
                if (!s.HistoryLoaded)
                {
                    s.YesterdayHigh = 0;
                    s.YesterdayLow = 0;
                    s.HighTwoDaysAgo = 0;
                    s.LowTwoDaysAgo = 0;
                }
            }
        }

        var b1 = NiftyZoneFormula.B1(s.YesterdayHigh, s.TodayOpen);
        var s1 = NiftyZoneFormula.S1(s.YesterdayLow, s.TodayOpen);
        var b2 = NiftyZoneFormula.B2(s.HighTwoDaysAgo, s.YesterdayHigh);
        var s2 = NiftyZoneFormula.S2(s.LowTwoDaysAgo, s.YesterdayLow);
        var mid = NiftyZoneFormula.S(s.YesterdayHigh, s.YesterdayLow, s.TodayOpen);
        var prev = s.PreviousClose;
        if (prev == 0m || prev == s.Last)
        {
            prev = s.TodayOpen;
        }

        return new SymbolSnapshot(
            s.Name,
            NiftyZoneFormula.RoundLine(s.Last),
            NiftyZoneFormula.RoundLine(s.TodayOpen),
            NiftyZoneFormula.RoundLine(s.YesterdayHigh),
            NiftyZoneFormula.RoundLine(s.YesterdayLow),
            NiftyZoneFormula.RoundLine(s.HighTwoDaysAgo),
            NiftyZoneFormula.RoundLine(s.LowTwoDaysAgo),
            NiftyZoneFormula.RoundLine(b2),
            NiftyZoneFormula.RoundLine(b1),
            NiftyZoneFormula.RoundLine(mid),
            NiftyZoneFormula.RoundLine(s1),
            NiftyZoneFormula.RoundLine(s2),
            NiftyZoneFormula.Mark(s.Name, s.Last, b1, s1, s.TodayOpen),
            NiftyZoneFormula.IsTightRange(s.Name, b1, s1, s.TodayOpen),
            NiftyZoneFormula.RoundLine(prev),
            s.Exchange,
            s.InstrumentToken > 0 ? s.InstrumentToken : null,
            s.Bid,
            s.Ask,
            s.Volume,
            s.OpenInterest,
            s.DataTimestamp);
    }

    private sealed class LiveSymbol
    {
        public required string Name { get; init; }
        public decimal HighTwoDaysAgo { get; set; }
        public decimal LowTwoDaysAgo { get; set; }
        public decimal YesterdayHigh { get; set; }
        public decimal YesterdayLow { get; set; }
        public decimal TodayOpen { get; set; }
        public decimal PreviousClose { get; set; }
        public decimal Last { get; set; }
        public int InstrumentToken { get; set; }
        public string Exchange { get; set; } = "NSE";
        public decimal? Bid { get; set; }
        public decimal? Ask { get; set; }
        public long? Volume { get; set; }
        public long? OpenInterest { get; set; }
        public DateTimeOffset? DataTimestamp { get; set; }
        public bool HistoryLoaded { get; set; }
        public List<DayCandle> Candles { get; set; } = [];
        public decimal B2 => NiftyZoneFormula.B2(HighTwoDaysAgo, YesterdayHigh);
        public decimal S2 => NiftyZoneFormula.S2(LowTwoDaysAgo, YesterdayLow);
    }
}
