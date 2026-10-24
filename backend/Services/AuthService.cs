using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class AuthService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    private readonly UserIdentityStore _users;

    public AuthService(UserIdentityStore users) => _users = users;

    public (bool Success, string Message, AuthSessionResponse? Session) Register(
        AuthRegisterRequest request,
        string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return (false, "Email and password are required.", null);
        }

        if (request.Password.Length < 8)
        {
            return (false, "Password must be at least 8 characters.", null);
        }

        if (_users.FindByEmail(request.Email) is not null)
        {
            return (false, "An account with this email already exists.", null);
        }

        var display = string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.Email.Split('@')[0]
            : request.DisplayName.Trim();
        var user = _users.CreateUser(
            request.Email,
            PasswordHasher.Hash(request.Password),
            display,
            UserRoles.Trader);
        return LoginInternal(user, ipAddress);
    }

    public (bool Success, string Message, AuthSessionResponse? Session) Login(
        AuthLoginRequest request,
        string? ipAddress)
    {
        var user = _users.FindByEmail(request.Email);
        if (user is null || !user.IsActive)
        {
            return (false, "Invalid email or password.", null);
        }

        var hash = _users.GetPasswordHash(user.Id);
        if (!PasswordHasher.Verify(request.Password, hash ?? ""))
        {
            return (false, "Invalid email or password.", null);
        }

        return LoginInternal(user, ipAddress);
    }

    public (bool Success, string Message) ChangePassword(string userId, AuthChangePasswordRequest request)
    {
        var hash = _users.GetPasswordHash(userId);
        if (!PasswordHasher.Verify(request.CurrentPassword, hash ?? ""))
        {
            return (false, "Current password is incorrect.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return (false, "New password must be at least 8 characters.");
        }

        _users.UpdatePassword(userId, PasswordHasher.Hash(request.NewPassword));
        return (true, "Password updated.");
    }

    public void Logout(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            _users.RevokeSession(token);
        }
    }

    private (bool Success, string Message, AuthSessionResponse? Session) LoginInternal(
        UserRecord user,
        string? ipAddress)
    {
        _users.RecordLogin(user.Id, user.Email, ipAddress);
        var session = _users.CreateSession(user.Id, SessionLifetime, ipAddress);
        var view = new AuthUserView(user.Id, user.Email, user.DisplayName, user.Role);
        return (true, "OK", new AuthSessionResponse(session.Token, session.ExpiresAt, view));
    }
}
