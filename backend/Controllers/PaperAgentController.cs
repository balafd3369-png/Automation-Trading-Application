using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/paper-agent")]
public sealed class PaperAgentController : ControllerBase
{
    private readonly PaperTradingAgent _agent;
    private readonly TradeHistoryStore _history;
    private readonly MockMarketService _market;
    private readonly ZoneBoardBrokerOptionPremiumService _optionPremiums;

    public PaperAgentController(
        PaperTradingAgent agent,
        TradeHistoryStore history,
        MockMarketService market,
        ZoneBoardBrokerOptionPremiumService optionPremiums)
    {
        _agent = agent;
        _history = history;
        _market = market;
        _optionPremiums = optionPremiums;
    }

    [HttpGet]
    public IActionResult Get() => Ok(_agent.Snapshot());

    [HttpPost("command")]
    public async Task<IActionResult> Command(
        [FromBody] PaperAgentCommandRequest request,
        CancellationToken ct)
    {
        return Ok(await _agent.HandleCommandAsync(request.Message, ct));
    }

    [HttpPost("take-strategy")]
    public IActionResult TakeStrategy([FromBody] TakeStrategyPaperTradeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Strategy) || string.IsNullOrWhiteSpace(request.Underlying))
        {
            return BadRequest(new { message = "Strategy and underlying are required." });
        }

        var catalogId = StrategyRecommendationEngine.MapStrategyCode(request.Strategy.Trim());
        var strategyCode = StrategyRecommendationEngine.StrategyCodeFromCatalog(catalogId);
        return Ok(_agent.RequestStrategyPaperTrade(strategyCode, request.Underlying.Trim()));
    }

    [HttpGet("history")]
    public IActionResult History([FromQuery] string range = "1m")
    {
        return Ok(_history.Query(range, DateTimeOffset.UtcNow));
    }

    [HttpGet("decisions")]
    public IActionResult Decisions()
    {
        var snapshot = _agent.Snapshot();
        return Ok(snapshot.RecentDecisions ?? []);
    }

    [HttpGet("market-context")]
    public IActionResult MarketContext()
    {
        var snapshot = _agent.Snapshot();
        return Ok(new { snapshot.MarketRegime, Symbols = snapshot.MarketContext ?? [] });
    }

    [HttpGet("positions")]
    public IActionResult Positions()
    {
        var snapshot = _agent.Snapshot();
        return Ok(new { snapshot.PendingTrade, snapshot.Positions });
    }

    [HttpGet("events")]
    public IActionResult Events([FromQuery] int limit = 50) =>
        Ok(_history.RecentEvents(limit));

    [HttpGet("trades")]
    public IActionResult Trades([FromQuery] string range = "1m", [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (!string.IsNullOrWhiteSpace(from))
        {
            return Ok(_history.QueryPaperTradesForIstDates(from, to, DateTimeOffset.UtcNow));
        }

        return Ok(_history.QueryPaperTrades(range, DateTimeOffset.UtcNow));
    }

    [HttpGet("strategy-track-record")]
    public IActionResult StrategyTrackRecord() =>
        Ok(_history.QueryStrategyTrackRecord(DateTimeOffset.UtcNow));

    [HttpGet("performance")]
    public IActionResult Performance() =>
        Ok(_agent.Snapshot().Performance);

    [HttpGet("backtest/zone-rejection/{name}")]
    public async Task<IActionResult> ZoneRejectionBacktest(string name, CancellationToken ct)
    {
        var board = _market.Snapshot();
        var symbol = board.Symbols.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (symbol is null)
        {
            return NotFound();
        }

            var chart = await _market.ChartAsync(name, "15minute", ct);
        if (chart is null)
        {
            return NotFound(new { message = "Chart data unavailable for backtest." });
        }

        var result = ZoneBoardPattern.BacktestB1RejectionShort(name, chart.Candles);
        return Ok(new
        {
            result,
            qualifies = ZoneRejectionPattern.QualifiesForUserRecommendation(
                new ZoneRejectionBacktestResult(
                    result.Symbol,
                    result.Pattern,
                    result.Samples,
                    result.Wins,
                    result.Losses,
                    result.WinRatePercent,
                    result.AvgRewardPoints,
                    result.AvgRiskPoints)),
            minWinRate = ZoneRejectionPattern.MinWinRateToRecommend,
            minSamples = ZoneRejectionPattern.MinBacktestSamples,
        });
    }

    [HttpGet("backtest/zone-board/{name}")]
    public async Task<IActionResult> ZoneBoardBacktest(string name, [FromQuery] int lookbackDays = 90, CancellationToken ct = default)
    {
        var board = _market.Snapshot();
        if (board.Symbols.All(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return NotFound();
        }

        lookbackDays = Math.Clamp(lookbackDays, 7, 365);
        var chart = await _market.ChartForBacktestAsync(name, ZoneBoardPattern.ChartInterval, lookbackDays, ct);
        if (chart is null)
        {
            return NotFound(new { message = "Chart data unavailable for zone board backtest." });
        }

        var candles = chart.Candles;
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var sessionDays = candles
            .Select(c => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(c.Time), ist).Date)
            .Distinct()
            .Count();

        static bool Qualifies(ZoneBoardPattern.ZoneBoardBacktestResult r) =>
            ZoneBoardPattern.Qualifies(r);

        var b1BreakLong = ZoneBoardPattern.BacktestB1BreakLong(name, candles);
        var s1BreakShort = ZoneBoardPattern.BacktestS1BreakShort(name, candles);
        var s2BounceLong = ZoneBoardPattern.BacktestS2BounceLong(name, candles);
        var b1RejectShort = ZoneBoardPattern.BacktestB1RejectionShort(name, candles);

        return Ok(new
        {
            interval = ZoneBoardPattern.ChartInterval,
            lookbackDays,
            candleCount = candles.Count,
            sessionDays,
            fromUtc = candles.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(candles[0].Time) : (DateTimeOffset?)null,
            toUtc = candles.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(candles[^1].Time) : (DateTimeOffset?)null,
            b1BreakLong,
            s1BreakShort,
            s2BounceLong,
            b1RejectShort,
            qualifies = new
            {
                b1BreakLong = Qualifies(b1BreakLong),
                s1BreakShort = Qualifies(s1BreakShort),
                s2BounceLong = Qualifies(s2BounceLong),
                b1RejectShort = Qualifies(b1RejectShort),
            },
            minWinRate = ZoneBoardPattern.MinWinRateToRecommend,
            minSamples = ZoneBoardPattern.MinBacktestSamples,
        });
    }

    [HttpGet("backtest/zone-board/{name}/trades")]
    public async Task<IActionResult> ZoneBoardTradeJournal(
        string name,
        [FromQuery] int lookbackDays = 60,
        [FromQuery] string pattern = "all",
        CancellationToken ct = default)
    {
        var journal = await LoadZoneBoardTradeJournalAsync(name, lookbackDays, pattern, ct);
        if (journal is null)
        {
            return NotFound();
        }

        var (trades, lookback, patternNorm) = journal.Value;
        return Ok(new
        {
            symbol = name,
            interval = ZoneBoardPattern.ChartInterval,
            lookbackDays = lookback,
            pattern = patternNorm,
            playbook = ZoneBoardTradePlaybook.Summary,
            tradeCount = trades.Count,
            trades,
        });
    }

    [HttpGet("backtest/zone-board/{name}/trades.csv")]
    public async Task<IActionResult> DownloadZoneBoardTradeJournalCsv(
        string name,
        [FromQuery] int lookbackDays = 60,
        [FromQuery] string pattern = "all",
        CancellationToken ct = default)
    {
        var journal = await LoadZoneBoardTradeJournalAsync(name, lookbackDays, pattern, ct);
        if (journal is null)
        {
            return NotFound();
        }

        var (trades, lookback, _) = journal.Value;
        var csv = await ZoneBoardTradeJournalCsv.ToCsvAsync(trades, _optionPremiums, ct);
        var fileName =
            $"zone-backtest-{name.ToUpperInvariant()}-{lookback}d-{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(
            new System.Text.UTF8Encoding(true).GetBytes(csv),
            "text/csv; charset=utf-8",
            fileName);
    }

    [HttpGet("backtest/zone-board/trades-all-indices.csv")]
    public async Task<IActionResult> DownloadZoneBoardTradeJournalAllIndicesCsv(
        [FromQuery] int lookbackDays = 60,
        [FromQuery] string pattern = "all",
        CancellationToken ct = default)
    {
        lookbackDays = Math.Clamp(lookbackDays, 7, 365);
        var all = new List<ZoneBoardPattern.ZoneBoardTradeJournalEntry>();
        foreach (var indexName in IndexUniverse.IndexNames)
        {
            var journal = await LoadZoneBoardTradeJournalAsync(indexName, lookbackDays, pattern, ct);
            if (journal is not null)
            {
                all.AddRange(journal.Value.Trades);
            }
        }

        all = all.OrderBy(t => t.EntryTime).ToList();
        var csv = await ZoneBoardTradeJournalCsv.ToCsvAsync(all, _optionPremiums, ct);
        var fileName = $"zone-backtest-ALL-INDICES-{lookbackDays}d-{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(
            new System.Text.UTF8Encoding(true).GetBytes(csv),
            "text/csv; charset=utf-8",
            fileName);
    }

    private async Task<(List<ZoneBoardPattern.ZoneBoardTradeJournalEntry> Trades, int LookbackDays, string Pattern)?> LoadZoneBoardTradeJournalAsync(
        string name,
        int lookbackDays,
        string pattern,
        CancellationToken ct)
    {
        var board = _market.Snapshot();
        if (board.Symbols.All(x => !x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        lookbackDays = Math.Clamp(lookbackDays, 7, 365);
        var chart = await _market.ChartForBacktestAsync(name, ZoneBoardPattern.ChartInterval, lookbackDays, ct);
        if (chart is null)
        {
            return null;
        }

        var patternNorm = string.IsNullOrWhiteSpace(pattern) ? "all" : pattern.Trim();
        var candles = chart.Candles;
        var includeB1 = patternNorm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || patternNorm.Equals("b1BreakLong", StringComparison.OrdinalIgnoreCase)
            || patternNorm.Equals(ZoneBoardPattern.B1BreakLong, StringComparison.OrdinalIgnoreCase);
        var includeS1 = patternNorm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || patternNorm.Equals("s1BreakShort", StringComparison.OrdinalIgnoreCase)
            || patternNorm.Equals(ZoneBoardPattern.S1BreakShort, StringComparison.OrdinalIgnoreCase);

        var trades = new List<ZoneBoardPattern.ZoneBoardTradeJournalEntry>();
        if (includeB1)
        {
            trades.AddRange(ZoneBoardPattern.JournalB1BreakLong(name, candles));
        }

        if (includeS1)
        {
            trades.AddRange(ZoneBoardPattern.JournalS1BreakShort(name, candles));
        }

        trades = trades.OrderBy(t => t.EntryTime).ToList();
        return (trades, lookbackDays, patternNorm);
    }
}
