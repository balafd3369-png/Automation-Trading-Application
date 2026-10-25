namespace TradeWithBeast.Configuration;

/// <summary>
/// Security-related settings (session/JWT/encryption). Reserved for future hardening; not all keys are used yet.
/// </summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    public string JwtSigningKey { get; set; } = "";
    public string DataEncryptionKey { get; set; } = "";
    public int SessionLifetimeHours { get; set; } = 168;
    public bool CookieSecure { get; set; } = true;
    public bool CookieHttpOnly { get; set; } = true;
    public string CookieSameSite { get; set; } = "Lax";
}
