namespace TradeWithBeast.Models;

public sealed record BoardResponse(
    BoardCounts Counts,
    IReadOnlyList<SymbolSnapshot> Symbols,
    DateTimeOffset UpdatedAt,
    string Feed);

public sealed record BoardCounts(int Buying, int Selling, int RangeBound);

public sealed record SymbolSnapshot(
    string Name,
    decimal Last,
    decimal TodayOpen,
    decimal YesterdayHigh,
    decimal YesterdayLow,
    decimal HighTwoDaysAgo,
    decimal LowTwoDaysAgo,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2,
    string Mark,
    bool TightRange,
    decimal PreviousClose);

public sealed record ChartBar(long Time, decimal Open, decimal High, decimal Low, decimal Close);

public sealed record ChartResponse(
    string Name,
    IReadOnlyList<ChartBar> Candles,
    decimal Last,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2);

public sealed record OptionChartResponse(
    string Name,
    string TradingSymbol,
    IReadOnlyList<ChartBar> Candles,
    decimal Last,
    decimal TodayOpen,
    decimal PreviousClose,
    decimal B2,
    decimal B1,
    decimal S,
    decimal S1,
    decimal S2);

public sealed record OptionChainItem(
    decimal Strike,
    string Side,
    string TradingSymbol,
    decimal Last,
    decimal Open,
    decimal PreviousClose,
    decimal ChangePercent);
