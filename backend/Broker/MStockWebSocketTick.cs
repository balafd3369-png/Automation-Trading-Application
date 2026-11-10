namespace TradeWithBeast.Broker;

public sealed record MStockWebSocketTick(
    int InstrumentToken,
    decimal Last,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    string Exchange,
    DateTimeOffset? ExchangeTimestamp);
