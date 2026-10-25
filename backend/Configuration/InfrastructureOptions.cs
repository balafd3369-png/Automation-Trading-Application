namespace TradeWithBeast.Configuration;

public sealed class InfrastructureOptions
{
    public const string Section = "Infrastructure";

    public string PublicApiBaseUrl { get; set; } = "";
    public int BrokerHttpTimeoutSeconds { get; set; } = 35;
    public bool SignalREnableDetailedErrors { get; set; }
}
