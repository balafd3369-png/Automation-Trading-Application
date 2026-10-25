namespace TradeWithBeast.Configuration;

/// <summary>
/// Broker credentials and endpoints (m.Stock). Environment variables: Broker__ApiKey, Broker__ApiSecret, etc.
/// Values merge into <see cref="Broker.MStockOptions"/> at startup (MStock section still supported).
/// </summary>
public sealed class BrokerOptions
{
    public const string Section = "Broker";

    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string ClientCode { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    public string RedirectUrl { get; set; } = "";
    public string RestBaseUrl { get; set; } = "";
    public string WsUrl { get; set; } = "";
    public string AccessToken { get; set; } = "";
}
