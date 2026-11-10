using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api")]
public sealed class BoardController : ControllerBase
{
    private readonly MockMarketService _market;
    private readonly ZoneStockAnalyzerService _zoneAnalyzer;
    private readonly ZoneAnalyzerDetailService _zoneDetail;
    private readonly GapAnalyzerService _gapAnalyzer;

    public BoardController(
        MockMarketService market,
        ZoneStockAnalyzerService zoneAnalyzer,
        ZoneAnalyzerDetailService zoneDetail,
        GapAnalyzerService gapAnalyzer)
    {
        _market = market;
        _zoneAnalyzer = zoneAnalyzer;
        _zoneDetail = zoneDetail;
        _gapAnalyzer = gapAnalyzer;
    }

    [HttpGet("board")]
    public IActionResult GetBoard() => Ok(_market.Snapshot());

    /// <summary>Today open vs B2/B1/S1/S2 — Select Stock board.</summary>
    [HttpGet("board/open-vs-zones")]
    public IActionResult GetOpenVsZones() =>
        Ok(OpenVsZonesBoardBuilder.Build(_market.Snapshot()));

    /// <summary>Live buy/sell/watch zone analyzer (Select Stock · Analyzer).</summary>
    [HttpGet("board/zone-analyzer")]
    public IActionResult GetZoneAnalyzer() =>
        Ok(_zoneAnalyzer.Analyze());

    /// <summary>Stock reversal radar — morning fade, failed opens, gap-down continuation.</summary>
    [HttpGet("board/reversal-scan")]
    public IActionResult GetReversalScan() =>
        Ok(StockReversalPatternScanner.Scan(_market.Snapshot()));

    /// <summary>Gap up/down analyzer — verified clues + live trade cards.</summary>
    [HttpGet("board/gap-analyzer")]
    public IActionResult GetGapAnalyzer() => Ok(_gapAnalyzer.Analyze());

    [HttpGet("board/zone-analyzer/detail")]
    public async Task<IActionResult> GetZoneAnalyzerDetail(
        [FromQuery] string name,
        [FromQuery] string patternId,
        [FromQuery] int lookbackDays = 90,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(patternId))
        {
            return BadRequest(new { message = "name and patternId are required." });
        }

        var detail = await _zoneDetail.GetDetailAsync(name.Trim(), patternId.Trim(), lookbackDays, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("board/symbols/search")]
    public IActionResult SearchSymbols([FromQuery] string q, [FromQuery] int limit = 24)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<SymbolSearchHit>());
        }

        var hits = _market.SearchSymbols(q, Math.Clamp(limit, 1, 50));
        return Ok(hits);
    }

    [HttpPost("board/symbols/{name}/ensure")]
    public async Task<IActionResult> EnsureSymbol(string name, CancellationToken ct)
    {
        var snapshot = await _market.EnsureSymbolAsync(name, ct);
        return snapshot is null ? NotFound(new { message = $"Symbol '{name}' was not found on NSE EQ list." }) : Ok(snapshot);
    }

    [HttpGet("board/chart/{name}")]
    [OutputCache(PolicyName = ChartOutputCachePolicy.PolicyName)]
    public async Task<IActionResult> GetChart(
        string name,
        [FromQuery] string interval = "5minute",
        [FromQuery] string depth = "full",
        CancellationToken ct = default)
    {
        var loadDepth = depth.ToLowerInvariant() switch
        {
            "fast" => ChartLoadDepth.Fast,
            "mid"  => ChartLoadDepth.Mid,
            _      => ChartLoadDepth.Full,
        };
        var chart = await _market.ChartAsync(name, interval, ct, loadDepth);
        if (chart is null)
        {
            return NotFound();
        }

        ChartHttpCacheHeaders.Apply(Response, loadDepth, chart);
        return Ok(chart);
    }

    [HttpGet("board/option-chart")]
    public async Task<IActionResult> GetOptionChart(
        [FromQuery] string underlying,
        [FromQuery] DateTime expiry,
        [FromQuery] decimal strike,
        [FromQuery] string side,
        [FromQuery] string interval = "5minute",
        CancellationToken ct = default)
    {
        var chart = await _market.OptionChartAsync(underlying, expiry, strike, side, interval, ct);
        return chart is null
            ? NotFound(new { message = "Live broker data is unavailable for this option contract." })
            : Ok(chart);
    }

    [HttpGet("board/option-chain")]
    public async Task<IActionResult> GetOptionChain(
        [FromQuery] string underlying,
        [FromQuery] DateTime expiry,
        CancellationToken ct = default)
    {
        var chain = await _market.OptionChainAsync(underlying, expiry, ct);
        return Ok(chain);
    }
}
