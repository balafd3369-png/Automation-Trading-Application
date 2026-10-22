using System.Globalization;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class ZoneBoardOptionLegSelector
{
    public static bool TryBuildIndexOptionLegs(
        MarketContextSnapshot index,
        string direction,
        IReadOnlyList<MarketContextSnapshot> context,
        out IReadOnlyList<StrategyLeg> legs)
    {
        legs = [];
        if (!IndexUniverse.IsIndex(index.Symbol) || index.Last <= 0)
        {
            return false;
        }

        var plan = ZoneBoardOptionEstimator.PlanStrikes(
            index.Symbol,
            direction,
            index.Last,
            index.Quote.DataTimestamp == default ? DateTimeOffset.UtcNow : index.Quote.DataTimestamp);

        var options = context
            .Where(x => x.InstrumentType == "OPTION"
                && x.Symbol.StartsWith(index.Symbol, StringComparison.OrdinalIgnoreCase)
                && x.Symbol.EndsWith(plan.OptionRight, StringComparison.OrdinalIgnoreCase)
                && x.Last > 0)
            .ToList();

        if (options.Count == 0)
        {
            return false;
        }

        var built = new List<StrategyLeg>();
        if (TryLeg(options, plan.ItmStrike, index.Symbol, plan.OptionRight, plan.ExpiryDate, out var itm))
        {
            built.Add(itm);
        }

        if (TryLeg(options, plan.OtmStrike, index.Symbol, plan.OptionRight, plan.ExpiryDate, out var otm))
        {
            built.Add(otm);
        }

        if (built.Count == 0)
        {
            return false;
        }

        legs = built;
        return true;
    }

    private static bool TryLeg(
        IReadOnlyList<MarketContextSnapshot> options,
        decimal strike,
        string underlying,
        string right,
        DateOnly expiry,
        out StrategyLeg leg)
    {
        leg = null!;
        var match = options
            .Select(o => (Option: o, Strike: ReadStrike(o)))
            .Where(x => x.Strike > 0 && Math.Abs(x.Strike - strike) < 0.01m)
            .Select(x => x.Option)
            .FirstOrDefault();
        if (match is null)
        {
            return false;
        }

        var ask = match.Quote.Ask ?? match.Last;
        if (ask <= 0)
        {
            return false;
        }

        leg = new StrategyLeg(
            match.Symbol,
            underlying,
            "OPTION",
            "BUY",
            1,
            ask,
            strike,
            right,
            new DateTimeOffset(expiry.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)),
            match.Quote);
        return true;
    }

    private static decimal ReadStrike(MarketContextSnapshot option)
    {
        if (TryFact(option.Evidence.Facts, "strike=", out var fromFact))
        {
            return fromFact;
        }

        var parts = option.Symbol.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var strike))
            {
                return strike;
            }
        }

        return 0;
    }

    private static bool TryFact(IReadOnlyList<string> facts, string prefix, out decimal value)
    {
        value = 0;
        var fact = facts.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return fact is not null
            && decimal.TryParse(fact[(fact.IndexOf('=') + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
