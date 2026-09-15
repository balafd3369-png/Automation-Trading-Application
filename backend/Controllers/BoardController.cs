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
}
