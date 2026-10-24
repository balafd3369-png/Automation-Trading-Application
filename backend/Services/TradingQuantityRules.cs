using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record TradingQuantityRulesResponse(
    string InstrumentType,
    string Underlying,
    string Symbol,
    int LotSize,
    int StepSize,
    int DefaultQuantity,
    int DefaultLots,
    bool UsesLots,
    string QuantityUnitLabel);

public sealed record TradingQuantityValidation(bool Valid, string Message);

public static class TradingQuantityRules
{
    public static TradingQuantityRulesResponse Resolve(
        string instrumentType,
        string underlying,
        string symbol)
    {
        var kind = InstrumentTypeNormalizer.Parse(instrumentType);
        var apiType = InstrumentTypeNormalizer.ToApiValue(kind);
        var under = string.IsNullOrWhiteSpace(underlying) ? symbol : underlying;
        under = under.Trim();
        var sym = string.IsNullOrWhiteSpace(symbol) ? under : symbol.Trim();

        if (kind is InstrumentKind.Stock)
        {
            return new TradingQuantityRulesResponse(
                apiType,
                under,
                sym,
                LotSize: 1,
                StepSize: 1,
                DefaultQuantity: 1,
                DefaultLots: 0,
                UsesLots: false,
                QuantityUnitLabel: "Shares");
        }

        var lot = InstrumentLotMetadata.LotSize(under);
        return new TradingQuantityRulesResponse(
            apiType,
            under,
            sym,
            LotSize: lot,
            StepSize: lot,
            DefaultQuantity: lot,
            DefaultLots: 1,
            UsesLots: true,
            QuantityUnitLabel: "Contracts");
    }

    public static TradingQuantityValidation Validate(
        string instrumentType,
        string underlying,
        string symbol,
        int quantity)
    {
        if (quantity <= 0)
        {
            return new TradingQuantityValidation(false, "Quantity must be at least 1.");
        }

        var rules = Resolve(instrumentType, underlying, symbol);
        if (!rules.UsesLots)
        {
            return new TradingQuantityValidation(true, "");
        }

        if (quantity < rules.LotSize)
        {
            return new TradingQuantityValidation(
                false,
                $"Minimum quantity is {rules.LotSize} (1 lot).");
        }

        if (quantity % rules.LotSize != 0)
        {
            return new TradingQuantityValidation(
                false,
                $"Quantity must be a multiple of lot size ({rules.LotSize}).");
        }

        return new TradingQuantityValidation(true, "");
    }

    public static int LotsFromQuantity(int quantity, int lotSize) =>
        lotSize <= 0 ? quantity : Math.Max(1, quantity / lotSize);
}
