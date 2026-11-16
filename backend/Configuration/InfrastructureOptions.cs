namespace TradeWithBeast.Configuration;

public sealed class InfrastructureOptions
{
    public const string Section = "Infrastructure";

    public string PublicApiBaseUrl { get; set; } = "";
    public int BrokerHttpTimeoutSeconds { get; set; } = 35;
    public bool SignalREnableDetailedErrors { get; set; }

    /// <summary>Production data root (block volume mount). Database and backups must live under this path.</summary>
    public string PersistentDataRoot { get; set; } = "";

    /// <summary>When true in Production, API refuses to start unless the data root is an active mount (Linux).</summary>
    public bool RequirePersistentDataMount { get; set; }
}
