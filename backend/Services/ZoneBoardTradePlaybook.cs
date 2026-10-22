namespace TradeWithBeast.Services;

internal static class ZoneBoardTradePlaybook
{
    public static object Summary => new
    {
        timeframe = "5 minute candles",
        session = "9:15–15:30 IST (square-off ~15:20 in live agent)",
        b1BreakLong = new
        {
            direction = "LONG",
            entry = "5m close crosses above B1 (prior close at or below B1)",
            target = "B2 (upper green buy zone)",
            stopLoss = "S (pivot: avg of yesterday H/L and today open)",
            exit = "Whichever hits first: B2 target, S stop, or session end on last 5m bar",
        },
        s1BreakShort = new
        {
            direction = "SHORT",
            entry = "5m close crosses below S1 (prior close at or above S1)",
            target = "S2 (lower red sell zone)",
            stopLoss = "S (pivot)",
            exit = "S2 target, S stop, or session end",
        },
        skipWhen = "B1–S1 tight range (board tightRange flag)",
    };
}
