namespace TradeWithBeast.Broker;

public sealed class MStockOptions
{
    public const string Section = "MStock";

    public string ApiKey { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RestBaseUrl { get; set; } = "https://api.mstock.trade";
    public string WsUrl { get; set; } = "wss://ws.mstock.trade";
}
