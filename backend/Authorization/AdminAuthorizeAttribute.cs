using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.RequestServices.GetService<ICurrentUserAccessor>();
        if (user is null || !user.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Authentication required." });
            return;
        }

        if (!string.Equals(user.Role, UserRoles.Admin, StringComparison.Ordinal))
        {
            context.Result = new ObjectResult(new { message = "Admin access required." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthenticatedUserAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.RequestServices.GetService<ICurrentUserAccessor>();
        if (user is null || !user.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Authentication required." });
        }
    }
}
