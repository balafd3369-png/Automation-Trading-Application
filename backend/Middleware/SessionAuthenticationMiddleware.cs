using TradeWithBeast.Services;

namespace TradeWithBeast.Middleware;

public sealed class SessionAuthenticationMiddleware
{
    private const string HeaderName = "Authorization";
    private readonly RequestDelegate _next;

    public SessionAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor currentUser,
        UserIdentityStore users)
    {
        var token = ExtractToken(context.Request);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var resolved = users.ResolveSession(token);
            if (resolved is not null)
            {
                currentUser.SetUser(resolved.Value.User.Id, resolved.Value.User.Role);
                context.Items["SessionToken"] = token;
                context.Items["UserId"] = resolved.Value.User.Id;
                context.Items["UserRole"] = resolved.Value.User.Role;
                users.TouchActivity(resolved.Value.User.Id);
            }
        }

        try
        {
            await _next(context);
        }
        finally
        {
            currentUser.Clear();
        }
    }

    private static string? ExtractToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header))
        {
            var value = header.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return value["Bearer ".Length..].Trim();
            }
        }

        if (request.Headers.TryGetValue("X-Session-Token", out var direct))
        {
            return direct.ToString().Trim();
        }

        return null;
    }
}
