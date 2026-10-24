using System.Text.Json;
using TradeWithBeast.Services;

namespace TradeWithBeast.Middleware;

public sealed class RequireAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SystemIdentityOptions _options;

    public RequireAuthenticationMiddleware(RequestDelegate next, SystemIdentityOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserAccessor currentUser)
    {
        if (!_options.RequireAuthentication || _options.AllowAnonymousLocalUser)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        if (!IsProtectedApi(path) || currentUser.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { message = "Authentication required." },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static bool IsProtectedApi(string path) =>
        path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/api/platform/features", StringComparison.OrdinalIgnoreCase);
}
