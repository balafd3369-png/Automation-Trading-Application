using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/strategies")]
public sealed class StrategiesController : ControllerBase
{
    private readonly StrategyRecommendationEngine _feed;

    public StrategiesController(StrategyRecommendationEngine feed)
    {
        _feed = feed;
    }

    [HttpGet]
    public IActionResult List() => Ok(StrategyCatalog.All());

    [HttpGet("feed")]
    public IActionResult Feed(CancellationToken ct) => Ok(_feed.BuildFeed(ct));

    /// <summary>Monitor vs paper-auto rollout for zone board strategies (from 90d backtest policy).</summary>
    [HttpGet("rollout")]
    public IActionResult Rollout() => Ok(ZoneStrategyRollout.BuildSnapshot());

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var entry = StrategyCatalog.Find(id);
        return entry is null ? NotFound() : Ok(entry);
    }
}
