using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TradeWithBeast.Models;

namespace TradeWithBeast.Authorization;

/// <summary>Blocks live-agent mutations when LiveTrading:Enabled is false.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LiveTradingEnabledAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetService<LiveExecutionOptions>();
        if (options is null || options.Enabled)
        {
            return;
        }

        context.Result = new ObjectResult(new { message = "Live trading is disabled in server configuration." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
