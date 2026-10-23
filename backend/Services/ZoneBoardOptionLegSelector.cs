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

        var options = OptionChain(index.Symbol, true, plan.OptionRight, context);

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

    /// <summary>One long option (CE/PE) for catalog intraday setups on indices or Nifty 50 stocks.</summary>
    public static bool TryBuildSingleLongOptionLeg(
        MarketContextSnapshot underlying,
        string direction,
        IReadOnlyList<MarketContextSnapshot> context,
        out StrategyLeg? leg)
    {
        leg = null;
        if (underlying.Last <= 0)
        {
            return false;
        }

        var isIndex = IndexUniverse.IsIndex(underlying.Symbol);
        if (!isIndex
            && !IndexUniverse.Nifty50.Contains(underlying.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var at = underlying.Quote.DataTimestamp == default ? DateTimeOffset.UtcNow : underlying.Quote.DataTimestamp;
        var plan = ZoneBoardOptionEstimator.PlanStrikes(underlying.Symbol, direction, underlying.Last, at);
        var options = OptionChain(underlying.Symbol, isIndex, plan.OptionRight, context);
        if (options.Count == 0)
        {
            return false;
        }

        foreach (var strike in new[] { plan.ItmStrike, plan.AtmStrike, plan.OtmStrike })
        {
            if (TryLeg(options, strike, underlying.Symbol, plan.OptionRight, plan.ExpiryDate, out var built))
            {
                leg = built;
                return true;
            }
        }

        return false;
    }

    private static List<MarketContextSnapshot> OptionChain(
        string underlying,
        bool isIndex,
        string right,
        IReadOnlyList<MarketContextSnapshot> context) =>
        context
            .Where(x => x.InstrumentType == "OPTION"
                && x.Last > 0
                && x.Symbol.EndsWith(right, StringComparison.OrdinalIgnoreCase)
                && (isIndex
                    ? x.Symbol.StartsWith(underlying, StringComparison.OrdinalIgnoreCase)
                    : x.Symbol.StartsWith(underlying + " ", StringComparison.OrdinalIgnoreCase)))
            .ToList();

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
