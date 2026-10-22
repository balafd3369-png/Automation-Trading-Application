namespace TradeWithBeast.Broker;

public static class BrokerIds
{
    public const string MStock = "mstock";

    public static bool IsKnown(string? id) =>
        string.Equals(id, MStock, StringComparison.OrdinalIgnoreCase);
}

public enum BrokerAuthKind
{
    Otp,
}

public sealed record BrokerProviderInfo(
    string Id,
    string DisplayName,
    BrokerAuthKind AuthKind,
    IReadOnlyList<string> SupportedExchanges);
