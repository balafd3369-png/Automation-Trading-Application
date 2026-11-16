namespace TradeWithBeast.Services;

public sealed record ZoneBoardSessionSnapshot(
    decimal SessionHigh,
    decimal SessionLow,
    bool TouchedB1,
    bool TouchedS1,
    bool TouchedS2);
