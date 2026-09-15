using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/paper-agent")]
public sealed class PaperAgentController : ControllerBase
{
    private readonly PaperTradingAgent _agent;
    private readonly TradeHistoryStore _history;

    public PaperAgentController(PaperTradingAgent agent, TradeHistoryStore history)
    {
        _agent = agent;
        _history = history;
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
    public IActionResult Trades([FromQuery] string range = "1m") =>
        Ok(_history.QueryPaperTrades(range, DateTimeOffset.UtcNow));

    [HttpGet("performance")]
    public IActionResult Performance() =>
        Ok(_agent.Snapshot().Performance);
}
