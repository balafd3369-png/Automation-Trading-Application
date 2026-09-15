using Microsoft.AspNetCore.Mvc;
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
            ? NotFound(new { message = "Live m.Stock data is unavailable for this option contract." })
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
