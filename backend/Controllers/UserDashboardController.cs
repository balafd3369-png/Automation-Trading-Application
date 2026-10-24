using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/user")]
[AuthenticatedUser]
public sealed class UserDashboardController : ControllerBase
{
    private readonly UserDashboardService _dashboard;
    private readonly MarketDataDistributor _market;

    public UserDashboardController(UserDashboardService dashboard, MarketDataDistributor market)
    {
        _dashboard = dashboard;
        _market = market;
    }

    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Ok(_dashboard.GetDashboard());

    [HttpGet("analytics")]
    public IActionResult Analytics() => Ok(_dashboard.GetAnalytics());

    [HttpGet("market-feed")]
    public IActionResult MarketFeed() => Ok(_market.GetStatus());
}
