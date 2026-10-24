using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ICurrentUserAccessor _current;
    private readonly UserIdentityStore _users;

    private readonly SystemIdentityOptions _identity;

    public AuthController(
        AuthService auth,
        ICurrentUserAccessor current,
        UserIdentityStore users,
        SystemIdentityOptions identity)
    {
        _auth = auth;
        _current = current;
        _users = users;
        _identity = identity;
    }

    [HttpGet("config")]
    public IActionResult Config() =>
        Ok(new
        {
            requireAuthentication = _identity.RequireAuthentication,
            allowAnonymousLocalUser = _identity.AllowAnonymousLocalUser,
            registrationMode = _identity.RegistrationMode,
        });

    [HttpPost("register")]
    public IActionResult Register([FromBody] AuthRegisterRequest request)
    {
        var mode = _identity.RegistrationMode?.Trim() ?? RegistrationModes.Open;
        if (string.Equals(mode, RegistrationModes.Disabled, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Registration is disabled on this server." });
        }

        if (string.Equals(mode, RegistrationModes.InviteOnly, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Registration is invite-only. Contact the platform admin." });
        }

        var (success, message, session) = _auth.Register(request, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(session);
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] AuthLoginRequest request)
    {
        var (success, message, session) = _auth.Login(request, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(session);
    }

    [HttpPost("logout")]
    [AuthenticatedUser]
    public IActionResult Logout()
    {
        var token = HttpContext.Items["SessionToken"] as string;
        _auth.Logout(token);
        return Ok(new { status = "logged_out" });
    }

    [HttpPost("change-password")]
    [AuthenticatedUser]
    public IActionResult ChangePassword([FromBody] AuthChangePasswordRequest request)
    {
        var (success, message) = _auth.ChangePassword(_current.UserId!, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { message });
    }

    [HttpGet("me")]
    [AuthenticatedUser]
    public IActionResult Me()
    {
        var user = _users.FindById(_current.UserId!);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new AuthUserView(user.Id, user.Email, user.DisplayName, user.Role));
    }
}
