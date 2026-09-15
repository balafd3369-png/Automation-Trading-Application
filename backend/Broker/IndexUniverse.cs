namespace TradeWithBeast.Broker;

public static class IndexUniverse
{
    public static readonly string[] IndexNames = ["NIFTY", "SENSEX", "BANKNIFTY", "FINNIFTY"];

    public static readonly string[] Nifty50 =
    [
        "ADANIENT", "ADANIPORTS", "APOLLOHOSP", "ASIANPAINT", "AXISBANK",
        "BAJAJ-AUTO", "BAJFINANCE", "BAJAJFINSV", "BEL", "BHARTIARTL",
        "BPCL", "CIPLA", "COALINDIA", "DRREDDY", "EICHERMOT",
        "GRASIM", "HCLTECH", "HDFCBANK", "HDFCLIFE", "HEROMOTOCO",
        "HINDALCO", "HINDUNILVR", "ICICIBANK", "INDUSINDBK", "INFY",
        "ITC", "JIOFIN", "JSWSTEEL", "KOTAKBANK", "LT",
        "M&M", "MARUTI", "NESTLEIND", "NTPC", "ONGC",
        "POWERGRID", "RELIANCE", "SBILIFE", "SBIN", "SHRIRAMFIN",
        "SUNPHARMA", "TATACONSUM", "TATAMOTORS", "TATASTEEL", "TCS",
        "TECHM", "TITAN", "TRENT", "ULTRACEMCO", "WIPRO",
    ];

    public static readonly string[] Sensex =
    [
        "ASIANPAINT", "AXISBANK", "BAJAJ-AUTO", "BAJFINANCE", "BHARTIARTL",
        "HCLTECH", "HDFCBANK", "HINDUNILVR", "ICICIBANK", "INDUSINDBK",
        "INFY", "ITC", "JSWSTEEL", "KOTAKBANK", "LT",
        "M&M", "MARUTI", "NESTLEIND", "NTPC", "POWERGRID",
        "RELIANCE", "SBIN", "SUNPHARMA", "TATAMOTORS", "TATASTEEL",
        "TCS", "TECHM", "TITAN", "ULTRACEMCO", "WIPRO",
    ];

    public static readonly string[] BankNifty =
    [
        "AUBANK", "AXISBANK", "BANKBARODA", "CANBK", "FEDERALBNK",
        "HDFCBANK", "ICICIBANK", "IDFCFIRSTB", "INDUSINDBK", "KOTAKBANK",
        "PNB", "SBIN",
    ];

    public static readonly string[] FinNifty =
    [
        "AXISBANK", "BAJFINANCE", "BAJAJFINSV", "BANKBARODA", "CANBK",
        "CHOLAFIN", "FEDERALBNK", "HDFCAMC", "HDFCBANK", "HDFCLIFE",
        "ICICIBANK", "ICICIGI", "ICICIPRULI", "IDFCFIRSTB", "INDUSINDBK",
        "KOTAKBANK", "LICHSGFIN", "MUTHOOTFIN", "PFC", "PNB",
        "RECLTD", "SBICARD", "SBILIFE", "SBIN", "SHRIRAMFIN",
    ];

    public static IReadOnlyList<string> Equities { get; } =
        Nifty50.Concat(Sensex).Concat(BankNifty).Concat(FinNifty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> AllNames { get; } =
        IndexNames.Concat(Equities).ToArray();

    public static Dictionary<string, string[]> QuoteKeys { get; } = BuildQuoteKeys();

    public static bool IsIndex(string name) =>
        IndexNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string[]> BuildQuoteKeys()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["NIFTY"] = ["NSE:NIFTY 50"],
            ["SENSEX"] = ["BSE:SENSEX"],
            ["BANKNIFTY"] = ["NSE:NIFTY BANK"],
            ["FINNIFTY"] = ["NSE:NIFTY FIN SERVICE"],
        };

        foreach (var equity in Equities)
        {
            map[equity] = [$"NSE:{equity}-EQ"];
        }

        return map;
    }
}
