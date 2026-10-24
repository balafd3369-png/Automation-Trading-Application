using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface ICurrentUserAccessor
{
    string? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
    bool IsAdmin => string.Equals(Role, UserRoles.Admin, StringComparison.Ordinal);
    void SetUser(string userId, string role);
    void Clear();
}

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private static readonly AsyncLocal<string?> UserIdLocal = new();
    private static readonly AsyncLocal<string?> RoleLocal = new();

    public string? UserId => UserIdLocal.Value;
    public string? Role => RoleLocal.Value;

    public void SetUser(string userId, string role)
    {
        UserIdLocal.Value = userId;
        RoleLocal.Value = role;
    }

    public void Clear()
    {
        UserIdLocal.Value = null;
        RoleLocal.Value = null;
    }
}
