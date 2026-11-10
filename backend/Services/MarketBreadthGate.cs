using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Board breadth filter for zone trend entries (see docs/MARKET-BREADTH-AND-ZONE-STRATEGY.md).
/// Uses verified quote marks from <see cref="NiftyZoneFormula.Mark"/>.
/// </summary>
public static class MarketBreadthGate
{
    public const decimal MinBuyingToSellingRatio = 1.25m;
    public const int MinBuyingSymbols = 5;
    public const int MinVerifiedSymbols = 8;

    public static bool IsBullishBreadth(BoardCounts counts)
    {
        if (counts.Buying < MinBuyingSymbols)
        {
            return false;
        }

        var selling = Math.Max(1, counts.Selling);
        if (counts.Buying / (decimal)selling < MinBuyingToSellingRatio)
        {
            return false;
        }

        var total = counts.Buying + counts.Selling + counts.RangeBound;
        return total >= MinVerifiedSymbols;
    }

    public static string Describe(BoardCounts counts)
    {
        var selling = Math.Max(1, counts.Selling);
        var ratio = counts.Buying / (decimal)selling;
        return $"Buying={counts.Buying} Selling={counts.Selling} Range={counts.RangeBound} ratio={ratio:0.##} (need ≥{MinBuyingToSellingRatio} and ≥{MinBuyingSymbols} buying)";
    }
}
