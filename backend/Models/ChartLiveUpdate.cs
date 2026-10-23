namespace TradeWithBeast.Models;

public sealed record ChartLiveUpdate(
    string Name,
    string Interval,
    ChartBar Bar,
    decimal Last,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2,
    DateTimeOffset ServerTime);
