using TradeWithBeast.Broker;
using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class MockMarketService : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private readonly Random _rng = new(1270);
    private readonly List<LiveSymbol> _symbols = [];
    private readonly SeedStore _store;
    private readonly MStockAuthClient _broker;
    private readonly ILogger<MockMarketService> _log;
    private Timer? _timer;
    private CancellationTokenSource? _historyCts;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private string _feed = "mock-1s";
    private int _busy;

    public MockMarketService(IWebHostEnvironment env, MStockAuthClient broker, ILogger<MockMarketService> log)
    {
        _store = new SeedStore(Path.Combine(env.ContentRootPath, "data", "mock-board.json"));
        _broker = broker;
        _log = log;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _historyCts?.Cancel();
        _historyCts?.Dispose();
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
                    HighTwoDaysAgo = seed.HighTwoDaysAgo,
                    LowTwoDaysAgo = seed.LowTwoDaysAgo,
                    YesterdayHigh = seed.YesterdayHigh,
                    YesterdayLow = seed.YesterdayLow,
                    TodayOpen = seed.TodayOpen,
                    Last = seed.LastSeed,
                });
            }

            _updatedAt = DateTimeOffset.UtcNow;
        }

        _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _historyCts = new CancellationTokenSource();
        _ = Task.Run(() => LoadHistoryLoopAsync(_historyCts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
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

    public async Task<ChartResponse?> ChartAsync(string name, string interval, CancellationToken ct)
    {
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

        IReadOnlyList<DayCandle> live = [];
        var token = symbol.InstrumentToken;
        var exchange = symbol.Exchange;
        if (_broker.HasSession && token != 0)
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

            foreach (var ex in exchanges)
            {
                try
                {
                    live = await _broker.GetCandlesAsync(ex, token, mapped, ct);
                    if (live.Count > 0)
                    {
                        break;
                    }
                }
                catch (Exception exn)
                {
                    _log.LogWarning(exn, "Chart candles failed for {Name} {Exchange} {Interval}", name, ex, mapped);
                }
            }

            _log.LogInformation("Chart {Name} {Interval} token={Token} bars={Bars}", name, mapped, token, live.Count);
        }

        lock (_gate)
        {
            var snap = ToSnapshot(symbol);
            List<ChartBar> bars;
            if (live.Count > 0)
            {
                bars = live
                    .GroupBy(c => c.Time.ToUnixTimeSeconds())
                    .Select(g => g.Last())
                    .OrderBy(c => c.Time)
                    .Select(c => new ChartBar(c.Time.ToUnixTimeSeconds(), c.Open, c.High, c.Low, c.Close))
                    .ToList();
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
            else
            {
                bars = BuildSyntheticIntraday(snap, mapped);
            }

            bars = SanitizeBars(bars, snap.Last);
            if (bars.Count == 0)
            {
                bars = BuildSyntheticIntraday(snap, mapped);
            }

            return new ChartResponse(snap.Name, bars, snap.Last, snap.B2, snap.B1, snap.S, snap.S1, snap.S2);
        }
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
            if (_broker.HasSession)
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
            MockWalk();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task RefreshLiveAsync()
    {
        var quotes = await _broker.GetOhlcAsync(CancellationToken.None);
        if (quotes.Count == 0)
        {
            _log.LogWarning("m.Stock OHLC returned no mapped symbols.");
            var ltp = await _broker.GetLtpAsync(CancellationToken.None);
            if (ltp.Count == 0)
            {
                lock (_gate)
                {
                    _feed = "m.Stock (quotes empty)";
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
                    }
                }

                _feed = "m.Stock ltp";
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
            }

            _feed = "m.Stock live";
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task LoadHistoryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_broker.HasSession)
            {
                await Task.Delay(2000, ct);
                continue;
            }

            LiveSymbol? next;
            lock (_gate)
            {
                next = _symbols.FirstOrDefault(s => s.InstrumentToken > 0 && !s.HistoryLoaded);
            }

            if (next is null)
            {
                await Task.Delay(15000, ct);
                continue;
            }

            try
            {
                var candles = await _broker.GetDayCandlesAsync(next.Exchange, next.InstrumentToken, ct);
                ApplyHistory(next, candles);
                if (!next.HistoryLoaded)
                {
                    next.HistoryLoaded = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Historical load failed for {Name}", next.Name);
                next.HistoryLoaded = true;
            }

            await Task.Delay(1100, ct);
        }
    }

    private void ApplyHistory(LiveSymbol symbol, IReadOnlyList<DayCandle> candles)
    {
        if (candles.Count < 2)
        {
            lock (_gate)
            {
                symbol.HistoryLoaded = true;
            }

            return;
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).Date;
        var completed = candles
            .Where(c => TimeZoneInfo.ConvertTime(c.Time, ist).Date < today || c.Time.Date < today)
            .ToList();
        if (completed.Count < 2)
        {
            completed = candles.ToList();
        }

        var yesterday = completed[^1];
        var twoAgo = completed.Count >= 2 ? completed[^2] : yesterday;
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
            if (todayBar is not null && todayBar.Open > 0 && symbol.TodayOpen == 0)
            {
                symbol.TodayOpen = todayBar.Open;
            }
        }
    }

    private void MockWalk()
    {
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

            _feed = "mock-1s";
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static SymbolSnapshot ToSnapshot(LiveSymbol s)
    {
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
            NiftyZoneFormula.RoundLine(prev));
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
        public bool HistoryLoaded { get; set; }
        public List<DayCandle> Candles { get; set; } = [];
        public decimal B2 => NiftyZoneFormula.B2(HighTwoDaysAgo, YesterdayHigh);
        public decimal S2 => NiftyZoneFormula.S2(LowTwoDaysAgo, YesterdayLow);
    }
}
