namespace TradeWithBeast.Models;

/// <summary>
/// Canonical instrument kinds for UI and policy. API wire values remain legacy strings (OPTION, FUTURE, …).
/// </summary>
public enum InstrumentKind
{
    Option = 0,
    Future = 1,
    Stock = 2,
    Index = 3,
    MultiLeg = 4,
    Unknown = 5,
}

public static class InstrumentTypeNormalizer
{
    public const string ApiOption = "OPTION";
    public const string ApiFuture = "FUTURE";
    public const string ApiStock = "STOCK";
    public const string ApiIndex = "INDEX";
    public const string ApiMultiLeg = "MULTI_LEG";

    /// <summary>
    /// Maps legacy instrument type strings. Defaults to <see cref="InstrumentKind.Option"/> when absent or unrecognized
    /// (catalog strategies are predominantly options).
    /// </summary>
    public static InstrumentKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InstrumentKind.Option;
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            ApiOption or "CE" or "PE" => InstrumentKind.Option,
            ApiFuture => InstrumentKind.Future,
            ApiStock => InstrumentKind.Stock,
            ApiIndex => InstrumentKind.Index,
            ApiMultiLeg => InstrumentKind.MultiLeg,
            _ => InstrumentKind.Option,
        };
    }

    public static string ToApiValue(InstrumentKind kind) =>
        kind switch
        {
            InstrumentKind.Future => ApiFuture,
            InstrumentKind.Stock => ApiStock,
            InstrumentKind.Index => ApiIndex,
            InstrumentKind.MultiLeg => ApiMultiLeg,
            _ => ApiOption,
        };

    /// <summary>Display / persistence default for legacy rows missing instrument type.</summary>
    public static string DefaultApiValue() => ApiOption;
}
