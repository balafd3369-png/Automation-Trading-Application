namespace TradeWithBeast.Services;

public enum PaperEntrySignal
{
    None,
    Long,
    Short,
}

public static class PaperTradeRules
{
    public static PaperEntrySignal DetectZoneCross(
        decimal previous,
        decimal current,
        decimal b1,
        decimal s1)
    {
        if (previous <= b1 && current > b1)
        {
            return PaperEntrySignal.Long;
        }
        if (previous >= s1 && current < s1)
        {
            return PaperEntrySignal.Short;
        }
        return PaperEntrySignal.None;
    }

    public static int CalculateQuantity(decimal entryPrice, decimal capitalPerTrade) =>
        PaperTradeEconomics.QuantityFromCapital(capitalPerTrade, entryPrice);

    public static bool HitStop(
        string direction,
        decimal current,
        decimal stop) =>
        direction == "LONG" ? current <= stop : current >= stop;

    public static bool HitTarget(
        string direction,
        decimal current,
        decimal target) =>
        direction == "LONG" ? current >= target : current <= target;

    public static bool ApprovalStillValid(
        string direction,
        decimal indicativePrice,
        decimal current,
        decimal target,
        decimal maxDriftFraction = 0.005m)
    {
        if (indicativePrice <= 0 || current <= 0)
        {
            return false;
        }
        var drift = Math.Abs(current - indicativePrice) / indicativePrice;
        var reward = direction == "LONG" ? target - current : current - target;
        return drift <= maxDriftFraction && reward > 0;
    }
}
