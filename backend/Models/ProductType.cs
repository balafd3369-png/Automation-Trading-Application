namespace TradeWithBeast.Models;

/// <summary>
/// Finer product classification for future broker/instrument master work.
/// Not persisted yet — inferred from legacy instrument type + underlying when needed.
/// </summary>
public enum ProductType
{
    Unknown = 0,
    IndexOption = 1,
    StockOption = 2,
    IndexFuture = 3,
    StockFuture = 4,
    IndexCash = 5,
    StockCash = 6,
}

public static class ProductTypeInference
{
    private static readonly string[] IndexNames = ["NIFTY", "BANKNIFTY", "FINNIFTY", "SENSEX"];

    public static bool IsIndexUnderlying(string? underlying) =>
        !string.IsNullOrWhiteSpace(underlying)
        && IndexNames.Contains(underlying.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps legacy wire values to a product type without changing stored strategy or leg data.
    /// </summary>
    public static ProductType Infer(string? instrumentType, string? underlying)
    {
        var kind = InstrumentTypeNormalizer.Parse(instrumentType);
        var index = IsIndexUnderlying(underlying);
        return kind switch
        {
            InstrumentKind.Future => index ? ProductType.IndexFuture : ProductType.StockFuture,
            InstrumentKind.Option => index ? ProductType.IndexOption : ProductType.StockOption,
            InstrumentKind.Stock => ProductType.StockCash,
            InstrumentKind.Index => ProductType.IndexCash,
            InstrumentKind.MultiLeg => index ? ProductType.IndexOption : ProductType.StockOption,
            _ => ProductType.Unknown,
        };
    }
}
