using Microsoft.Extensions.Logging;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Canonical paper-trade qty, capital, and P&amp;L (no UI-side recomputation).
/// </summary>
public static class PaperTradeEconomics
{
    public const decimal AssertionTolerance = 1m;

    public static int QuantityFromCapital(decimal capitalPerTrade, decimal entryPrice)
    {
        if (capitalPerTrade <= 0 || entryPrice <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(capitalPerTrade / entryPrice);
    }

    public static decimal CapitalDeployed(int quantity, decimal entryPrice) =>
        quantity > 0 && entryPrice > 0 ? quantity * entryPrice : 0;

    public static bool IsLongDirection(string direction) =>
        direction.Equals("LONG", StringComparison.OrdinalIgnoreCase)
        || direction.Equals("BULLISH", StringComparison.OrdinalIgnoreCase);

    public static decimal RealizedPnl(string direction, decimal entryPrice, decimal exitPrice, int quantity)
    {
        if (quantity <= 0 || entryPrice <= 0 || exitPrice <= 0)
        {
            return 0;
        }

        var move = IsLongDirection(direction)
            ? exitPrice - entryPrice
            : entryPrice - exitPrice;
        return move * quantity;
    }

    public static decimal LegEntryPrice(PaperTradeHistoryLegSnapshot leg) =>
        leg.EntryPrice is > 0 ? leg.EntryPrice.Value : leg.ProposedPrice;

    public static decimal? LegExitPrice(PaperTradeHistoryLegSnapshot leg) =>
        leg.ExitPrice is > 0 ? leg.ExitPrice : leg.CurrentPrice is > 0 ? leg.CurrentPrice : null;

    public static decimal LegPnl(PaperTradeHistoryLegSnapshot leg)
    {
        var entry = LegEntryPrice(leg);
        var exit = LegExitPrice(leg);
        if (entry <= 0 || exit is null or <= 0)
        {
            return leg.Pnl ?? 0;
        }

        var move = leg.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase)
            ? exit.Value - entry
            : entry - exit.Value;
        return move * leg.Quantity;
    }

    public static decimal TradeEntryPrice(IReadOnlyList<PaperTradeHistoryLegSnapshot> legs)
    {
        if (legs.Count == 0)
        {
            return 0;
        }

        decimal sum = 0;
        var qty = 0;
        foreach (var leg in legs)
        {
            var entry = LegEntryPrice(leg);
            if (entry <= 0)
            {
                continue;
            }

            sum += entry * leg.Quantity;
            qty += leg.Quantity;
        }

        return qty > 0 ? sum / qty : 0;
    }

    public static decimal? TradeExitPrice(IReadOnlyList<PaperTradeHistoryLegSnapshot> legs)
    {
        if (legs.Count == 0)
        {
            return null;
        }

        decimal sum = 0;
        var qty = 0;
        foreach (var leg in legs)
        {
            var exit = LegExitPrice(leg);
            if (exit is null or <= 0)
            {
                continue;
            }

            sum += exit.Value * leg.Quantity;
            qty += leg.Quantity;
        }

        return qty > 0 ? sum / qty : null;
    }

    public static int TradeQuantity(IReadOnlyList<PaperTradeHistoryLegSnapshot> legs, decimal capitalPerTrade)
    {
        if (legs.Count == 0)
        {
            return 0;
        }

        if (legs.Count == 1)
        {
            var entry = LegEntryPrice(legs[0]);
            if (entry > 0 && capitalPerTrade > 0)
            {
                var sized = QuantityFromCapital(capitalPerTrade, entry);
                return sized > 0 ? sized : legs[0].Quantity;
            }

            return legs[0].Quantity;
        }

        return legs.Sum(leg => leg.Quantity);
    }

    public static decimal TradeCapital(IReadOnlyList<PaperTradeHistoryLegSnapshot> legs, int quantity, decimal entryPrice)
    {
        if (legs.Count == 1 && entryPrice > 0 && quantity > 0)
        {
            return CapitalDeployed(quantity, entryPrice);
        }

        return legs.Sum(leg =>
        {
            var entry = LegEntryPrice(leg);
            return entry > 0 ? entry * leg.Quantity : 0;
        });
    }

    public static decimal? TradePnl(PaperTradeHistorySnapshot trade, int quantity, decimal entryPrice, decimal? exitPrice)
    {
        if (trade.Legs.Count == 1 && entryPrice > 0 && exitPrice is > 0 && quantity > 0)
        {
            return RealizedPnl(trade.Direction, entryPrice, exitPrice.Value, quantity);
        }

        if (trade.Legs.Count == 0)
        {
            return trade.Pnl;
        }

        return trade.Legs.Sum(LegPnl);
    }

    public static PaperTradeHistorySnapshot Normalize(
        PaperTradeHistorySnapshot trade,
        decimal capitalPerTrade,
        ILogger? log = null)
    {
        var entryPrice = TradeEntryPrice(trade.Legs);
        var exitPrice = TradeExitPrice(trade.Legs);
        var quantity = TradeQuantity(trade.Legs, capitalPerTrade);
        var capital = TradeCapital(trade.Legs, quantity, entryPrice);
        var pnl = trade.Status == "CLOSED"
            ? TradePnl(trade, quantity, entryPrice, exitPrice)
            : trade.Pnl;

        if (trade.Legs.Count == 1 && entryPrice > 0 && quantity > 0)
        {
            var expectedCapital = CapitalDeployed(quantity, entryPrice);
            if (Math.Abs(capital - expectedCapital) >= AssertionTolerance)
            {
                log?.LogWarning(
                    "Paper trade capital mismatch {TradeId} {Symbol}: capital={Capital} expected={Expected}",
                    trade.Id,
                    trade.Underlying,
                    capital,
                    expectedCapital);
            }

            capital = expectedCapital;
        }

        if (quantity > 0 && entryPrice > 0)
        {
            var expectedCapital = CapitalDeployed(quantity, entryPrice);
            if (Math.Abs(capital - expectedCapital) >= AssertionTolerance)
            {
                log?.LogError(
                    "Paper trade capital assertion failed {TradeId} {Symbol}: |capital-qty*entry|={Delta}",
                    trade.Id,
                    trade.Underlying,
                    Math.Abs(capital - expectedCapital));
            }
        }

        if (trade.Status == "CLOSED" && pnl is not null && trade.Pnl is not null)
        {
            if (Math.Abs(trade.Pnl.Value - pnl.Value) >= AssertionTolerance)
            {
                log?.LogError(
                    "Paper trade PnL assertion failed {TradeId} {Symbol}: |stored-calculated|={Delta}",
                    trade.Id,
                    trade.Underlying,
                    Math.Abs(trade.Pnl.Value - pnl.Value));
            }
        }

        log?.LogInformation(
            "Paper trade economics symbol={Symbol} entryPrice={EntryPrice} exitPrice={ExitPrice} qty={Qty} capital={Capital} pnl={Pnl}",
            trade.Underlying,
            entryPrice,
            exitPrice,
            quantity,
            capital,
            pnl);

        var legs = trade.Legs.Count == 1 && quantity > 0
            ? trade.Legs.Select(leg => leg with { Quantity = quantity, Pnl = pnl }).ToList()
            : trade.Legs.Select(leg => leg with { Pnl = LegPnl(leg) }).ToList();

        return trade with
        {
            Quantity = quantity,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            CapitalRequired = capital,
            Pnl = pnl,
            Legs = legs,
        };
    }

    public static int SizeLegQuantity(decimal capitalPerTrade, decimal entryPrice, int lotSize)
    {
        var qty = QuantityFromCapital(capitalPerTrade, entryPrice);
        if (lotSize <= 1)
        {
            return qty;
        }

        if (qty < lotSize)
        {
            return capitalPerTrade >= entryPrice * lotSize ? lotSize : 0;
        }

        return qty / lotSize * lotSize;
    }
}
