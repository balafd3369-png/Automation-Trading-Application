namespace TradeWithBeast.Configuration;

public sealed class CorsOptions
{
    public const string Section = "Cors";

    /// <summary>When empty in Development, any origin is allowed. In Production, empty means no CORS origins configured (policy still allows any until origins are set — see Program).</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
