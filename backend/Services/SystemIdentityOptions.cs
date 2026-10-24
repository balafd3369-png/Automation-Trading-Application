namespace TradeWithBeast.Services;

public static class RegistrationModes
{
    public const string Open = "Open";
    public const string InviteOnly = "InviteOnly";
    public const string Disabled = "Disabled";
}

public sealed class SystemIdentityOptions
{
    public const string Section = "SystemIdentity";
    public const string DefaultAdminPassword = "ChangeMe-Admin-123!";
    public string AdminEmail { get; set; } = "admin@tradewithbeast.local";
    public string AdminPassword { get; set; } = DefaultAdminPassword;
    public string AdminDisplayName { get; set; } = "Platform Admin";
    public decimal DefaultPaperCapital { get; set; } = 100_000m;
    public string StrategyEngineUserId { get; set; } = TradeHistoryStore.LocalUserId;
    /// <summary>When true, API calls require a valid session (hosted / friend testing).</summary>
    public bool RequireAuthentication { get; set; } = true;
    /// <summary>Development-only: allow legacy local-user when no session (must be false in hosted mode).</summary>
    public bool AllowAnonymousLocalUser { get; set; } = false;
    /// <summary>Open (default), InviteOnly, or Disabled — controls self-service registration.</summary>
    public string RegistrationMode { get; set; } = RegistrationModes.Open;
}
