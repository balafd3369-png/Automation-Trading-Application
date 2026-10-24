using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/platform")]
public sealed class PlatformController : ControllerBase
{
    private readonly TradingPlatformOptions _platform;
    private readonly SystemIdentityOptions _identity;
    private readonly LiveExecutionOptions _live;
    private readonly DeploymentReadinessService _readiness;

    public PlatformController(
        TradingPlatformOptions platform,
        SystemIdentityOptions identity,
        LiveExecutionOptions live,
        DeploymentReadinessService readiness)
    {
        _platform = platform;
        _identity = identity;
        _live = live;
        _readiness = readiness;
    }

    [HttpGet("features")]
    public IActionResult Features() =>
        Ok(new
        {
            futuresEnabled = _platform.FuturesEnabled,
            liveTradingEnabled = _live.Enabled,
            requireAuthentication = _identity.RequireAuthentication,
            allowAnonymousLocalUser = _identity.AllowAnonymousLocalUser,
            registrationMode = _identity.RegistrationMode,
            defaultInstrumentType = InstrumentTypeNormalizer.DefaultApiValue(),
            productTypes = Enum.GetNames(typeof(ProductType)),
        });

    /// <summary>Deployment status for operators (authenticated users).</summary>
    [HttpGet("deployment-status")]
    [AuthenticatedUser]
    public IActionResult DeploymentStatus()
    {
        var report = _readiness.BuildReport();
        return Ok(new DeploymentStatusSummary(report.Overall, report.Checks, report.GeneratedAt));
    }

    [HttpGet("quantity-rules")]
    public IActionResult QuantityRules(
        [FromQuery] string instrumentType,
        [FromQuery] string underlying,
        [FromQuery] string? symbol) =>
        Ok(TradingQuantityRules.Resolve(
            instrumentType,
            underlying,
            symbol ?? underlying));
}
