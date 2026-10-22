using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api")]
public sealed class BoardController : ControllerBase
{
    private readonly MockMarketService _market;

    public BoardController(MockMarketService market)
    {
        _market = market;
    }

    [HttpGet("board")]
    public IActionResult GetBoard() => Ok(_market.Snapshot());

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
    public async Task<IActionResult> GetChart(string name, [FromQuery] string interval = "5minute", CancellationToken ct = default)
    {
        var chart = await _market.ChartAsync(name, interval, ct);
        return chart is null ? NotFound() : Ok(chart);
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
