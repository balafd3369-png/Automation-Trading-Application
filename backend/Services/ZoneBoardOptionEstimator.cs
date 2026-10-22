namespace TradeWithBeast.Services;

/// <summary>
/// Models long-only index option P&amp;L for zone backtests (historical option quotes are not available per bar).
/// LONG spot setup → buy CE; SHORT → buy PE. Reports one ITM and one OTM strike per trade.
/// </summary>
public static class ZoneBoardOptionEstimator
{
    public const string SourceBroker15M = "BROKER_15M";
    public const string SourceModeled = "MODELED";

    public sealed record OptionLegEstimate(
        string Moneyness,
        decimal Strike,
        decimal EntryPremiumEst,
        decimal ExitPremiumEst,
        decimal PremiumPnlPoints,
        decimal RupeePnl,
        string Result,
        string PremiumSource);

    public sealed record StrikePlan(
        string Symbol,
        string OptionRight,
        DateOnly ExpiryDate,
        int LotSize,
        decimal AtmStrike,
        decimal ItmStrike,
        decimal OtmStrike,
        decimal Step,
        bool IsCall);

    public sealed record TradeOptionEstimate(
        string OptionRight,
        DateOnly ExpiryDate,
        int LotSize,
        decimal AtmStrike,
        OptionLegEstimate Itm,
        OptionLegEstimate Otm,
        string Notes);

    public static StrikePlan PlanStrikes(ZoneBoardPattern.ZoneBoardTradeJournalEntry trade) =>
        PlanStrikes(trade.Symbol, trade.Direction, trade.Entry, trade.EntryTime);

    public static StrikePlan PlanStrikes(string symbol, string direction, decimal spotEntry, DateTimeOffset entryTime)
    {
        var step = StrikeStep(symbol);
        var atm = Math.Round(spotEntry / step, 0, MidpointRounding.AwayFromZero) * step;
        var isLong = direction.Equals("LONG", StringComparison.OrdinalIgnoreCase)
            || direction.Equals("BULLISH", StringComparison.OrdinalIgnoreCase);
        var right = isLong ? "CE" : "PE";
        var lot = PortfolioRiskManager.LotSize(symbol);
        var expiry = DateOnly.FromDateTime(
            MarketContextService.NextWeeklyExpiry(symbol, entryTime).Date);

        decimal itmStrike;
        decimal otmStrike;
        if (isLong)
        {
            itmStrike = atm - step;
            otmStrike = atm + step;
        }
        else
        {
            itmStrike = atm + step;
            otmStrike = atm - step;
        }

        itmStrike = Math.Max(step, itmStrike);
        otmStrike = Math.Max(step, otmStrike);

        return new StrikePlan(symbol, right, expiry, lot, atm, itmStrike, otmStrike, step, isLong);
    }

    public static TradeOptionEstimate Estimate(ZoneBoardPattern.ZoneBoardTradeJournalEntry trade) =>
        EstimateModeled(trade, PlanStrikes(trade));

    public static TradeOptionEstimate EstimateModeled(
        ZoneBoardPattern.ZoneBoardTradeJournalEntry trade,
        StrikePlan plan)
    {
        var spotEntry = trade.Entry;
        var spotExit = trade.ExitPrice;
        var holdMinutes = Math.Max(1d, (trade.ExitTime - trade.EntryTime).TotalMinutes);
        var decay = TimeDecay(holdMinutes);

        var itm = BuildModeledLeg("ITM", plan.ItmStrike, plan.IsCall, spotEntry, spotExit, plan.Step, decay, plan.LotSize, isItm: true);
        var otm = BuildModeledLeg("OTM", plan.OtmStrike, plan.IsCall, spotEntry, spotExit, plan.Step, decay, plan.LotSize, isItm: false);

        return new TradeOptionEstimate(
            plan.OptionRight,
            plan.ExpiryDate,
            plan.LotSize,
            plan.AtmStrike,
            itm,
            otm,
            "Spot signal 5m; option premium MODELED (broker 15m unavailable).");
    }

    public static OptionLegEstimate BuildBrokerLeg(
        string moneyness,
        decimal strike,
        decimal entryPremium,
        decimal exitPremium,
        int lot,
        string source)
    {
        var premiumPnl = decimal.Round(exitPremium - entryPremium, 2);
        var rupee = decimal.Round(premiumPnl * lot, 2);
        var result = premiumPnl >= 0 ? "PROFIT" : "LOSS";
        return new OptionLegEstimate(
            moneyness,
            strike,
            decimal.Round(entryPremium, 2),
            decimal.Round(exitPremium, 2),
            premiumPnl,
            rupee,
            result,
            source);
    }

    internal static decimal StrikeStep(string symbol) =>
        symbol.ToUpperInvariant() switch
        {
            "BANKNIFTY" or "SENSEX" => 100m,
            _ => 50m,
        };

    private static OptionLegEstimate BuildModeledLeg(
        string moneyness,
        decimal strike,
        bool isCall,
        decimal spotEntry,
        decimal spotExit,
        decimal step,
        decimal decay,
        int lot,
        bool isItm)
    {
        var entryPremium = Mark(spotEntry, strike, isCall, step, isItm, 1m);
        var exitPremium = Mark(spotExit, strike, isCall, step, isItm, decay);
        return BuildBrokerLeg(moneyness, strike, entryPremium, exitPremium, lot, SourceModeled);
    }

    private static decimal Mark(decimal spot, decimal strike, bool isCall, decimal step, bool isItm, decimal decay)
    {
        var intrinsic = isCall ? Math.Max(0m, spot - strike) : Math.Max(0m, strike - spot);
        return intrinsic + ExtrinsicBase(isItm, step) * decay;
    }

    private static decimal ExtrinsicBase(bool isItm, decimal step) =>
        Math.Max(12m, step * (isItm ? 0.42m : 0.52m));

    private static decimal TimeDecay(double holdMinutes)
    {
        const double sessionMinutes = 375d;
        return (decimal)Math.Clamp(1d - holdMinutes / sessionMinutes * 0.82d, 0.12d, 1d);
    }

}
