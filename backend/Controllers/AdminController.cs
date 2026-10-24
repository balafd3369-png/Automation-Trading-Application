using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin")]
[AdminAuthorize]
public sealed class AdminController : ControllerBase
{
    private readonly AdminPlatformService _admin;
    private readonly DeploymentReadinessService _readiness;

    public AdminController(AdminPlatformService admin, DeploymentReadinessService readiness)
    {
        _admin = admin;
        _readiness = readiness;
    }

    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Ok(_admin.GetDashboard());

    [HttpGet("users/activity")]
    public IActionResult UserActivity() => Ok(_admin.GetUserActivity());

    [HttpGet("users/logins")]
    public IActionResult LoginHistory([FromQuery] int limit = 200) =>
        Ok(_admin.GetLoginHistory(limit));

    [HttpGet("deployment-readiness")]
    public IActionResult DeploymentReadiness() => Ok(_readiness.BuildReport());
}
