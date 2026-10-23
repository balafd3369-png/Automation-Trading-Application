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

    /// <summary>
    /// Board symbol → m.Stock quote instrument keys when the NSE ticker no longer matches the index name.
    /// Must be declared before <see cref="QuoteKeys"/> (static init order).
    /// </summary>
    private static readonly Dictionary<string, string[]> EquityQuoteAliasOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Post-demerger: Nifty still uses TATAMOTORS; live NSE symbols are TMCV / TMPV.
            ["TATAMOTORS"] = ["NSE:TMCV-EQ", "NSE:TMPV-EQ", "NSE:TATAMOTORS-EQ"],
            ["ICICIPRULI"] = ["NSE:ICICIPRULI-EQ", "NSE:ICICIPRULI"],
        };

    public static Dictionary<string, string[]> QuoteKeys { get; } = BuildQuoteKeys();

    private static readonly object QuoteKeyGate = new();

    public static bool IsIndex(string name) =>
        IndexNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps m.Stock quote payload keys (e.g. NSE:ICICIPRULI-EQ) to board symbols.</summary>
    public static bool TryResolveBoardFromInstrumentKey(string instrumentKey, out string boardName)
    {
        foreach (var kv in QuoteKeys)
        {
            if (kv.Value.Any(alias => alias.Equals(instrumentKey, StringComparison.OrdinalIgnoreCase)))
            {
                boardName = kv.Key;
                return true;
            }
        }

        var colon = instrumentKey.IndexOf(':');
        if (colon >= 0 && colon < instrumentKey.Length - 1)
        {
            var ticker = instrumentKey[(colon + 1)..].Trim();
            if (ticker.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
            {
                ticker = ticker[..^3];
            }

            if (QuoteKeys.ContainsKey(ticker))
            {
                boardName = ticker;
                return true;
            }
        }

        boardName = "";
        return false;
    }

    /// <summary>Registers NSE/BSE quote aliases for equities not in the default board universe.</summary>
    public static bool TryRegisterEquity(string symbol, out string[] quoteKeys)
    {
        var name = symbol.Trim().ToUpperInvariant();
        if (IsIndex(name))
        {
            quoteKeys = QuoteKeys[name];
            return true;
        }

        lock (QuoteKeyGate)
        {
            if (QuoteKeys.TryGetValue(name, out quoteKeys!))
            {
                return true;
            }

            quoteKeys = [$"NSE:{name}-EQ", $"BSE:{name}"];
            QuoteKeys[name] = quoteKeys;
            return true;
        }
    }

    /// <summary>m.Stock / NSE-BSE instrument tokens mapped to board symbols.</summary>
    public static bool TryResolveBoardByInstrumentToken(int instrumentToken, string? exchange, out string boardName)
    {
        var ex = (exchange ?? "NSE").Trim().ToUpperInvariant();
        if (ex is "NSE" or "NFO" && NseInstrumentTokens.TryGetValue(instrumentToken, out boardName!))
        {
            return true;
        }

        if (ex is "BSE" or "BFO" && BseInstrumentTokens.TryGetValue(instrumentToken, out boardName!))
        {
            return true;
        }

        boardName = "";
        return false;
    }

    private static readonly Dictionary<int, string> NseInstrumentTokens = new()
    {
        [26000] = "NIFTY",
        [26009] = "BANKNIFTY",
        [26037] = "FINNIFTY",
    };

    private static readonly Dictionary<int, string> BseInstrumentTokens = new()
    {
        [1] = "SENSEX",
        [999901] = "SENSEX",
    };

    private static Dictionary<string, string[]> BuildQuoteKeys()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["NIFTY"] = ["NSE:NIFTY 50"],
            ["SENSEX"] = ["BSE:SENSEX", "BSE:SENSEX 50"],
            ["BANKNIFTY"] = ["NSE:NIFTY BANK", "NSE:NIFTY BANK INDEX"],
            ["FINNIFTY"] = ["NSE:NIFTY FIN SERVICE", "NSE:FINNIFTY"],
        };

        foreach (var equity in Equities)
        {
            map[equity] = EquityQuoteAliasOverrides.TryGetValue(equity, out var aliases)
                ? aliases
                : [$"NSE:{equity}-EQ"];
        }

        return map;
    }
}
