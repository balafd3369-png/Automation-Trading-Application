using TradeWithBeast.Broker;
using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Image-based zone board on indices only: option legs (CE/PE) on B1/S1/S2 setups.
/// </summary>
public sealed class ZoneBoardIntradayStrategy(ZoneBoardSessionTracker sessions) : IPaperStrategy
{
    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);

        foreach (var item in context.Where(x => IndexUniverse.IsIndex(x.Symbol)))
        {
            if (!TryZones(item.Evidence.Facts, out var b2, out var b1, out var pivot, out var s1, out var s2))
            {
                continue;
            }

            var todayOpen = ZoneRejectionPattern.TryReadZoneFact(item.Evidence.Facts, "todayOpen=", out var open)
                ? open
                : pivot;
            if (NiftyZoneFormula.IsTightRange(item.Symbol, b1, s1, todayOpen))
            {
                continue;
            }

            if (!previous.TryGetValue(item.Symbol, out var prior))
            {
                continue;
            }

            if (sessions.TryTakeCross(item.Symbol, istDay, ZoneBoardPattern.B1BreakLong, prior, item.Last, b1, s1))
            {
                if (TryIndexZoneOptions(
                        item,
                        context,
                        "BULLISH",
                        pivot,
                        b2,
                        ZoneBoardPattern.B1BreakLong,
                        $"5m cross above B1 {b1:0.##} → auto buy CE (ITM+OTM)",
                        $"index target B2 {b2:0.##}",
                        $"index stop S {pivot:0.##}",
                        out var b1Index))
                {
                    yield return b1Index;
                }
            }

            if (sessions.TryTakeCross(item.Symbol, istDay, ZoneBoardPattern.S1BreakShort, prior, item.Last, b1, s1))
            {
                if (TryIndexZoneOptions(
                        item,
                        context,
                        "BEARISH",
                        pivot,
                        s2,
                        ZoneBoardPattern.S1BreakShort,
                        $"5m cross below S1 {s1:0.##} → auto buy PE (ITM+OTM)",
                        $"index target S2 {s2:0.##}",
                        $"index stop S {pivot:0.##}",
                        out var s1Index))
                {
                    yield return s1Index;
                }
            }

            if (sessions.TryTakeS2Bounce(item.Symbol, istDay, prior, item.Last, s2))
            {
                if (TryIndexZoneOptions(
                        item,
                        context,
                        "BULLISH",
                        Math.Min(s2, item.Last) * (1m - ZoneBoardPattern.TouchTolerance),
                        b1,
                        ZoneBoardPattern.S2BounceLong,
                        $"S2 reclaim {s2:0.##} → auto buy CE (ITM+OTM)",
                        $"index target B1 {b1:0.##}",
                        "stop below S2",
                        out var s2Index))
                {
                    yield return s2Index;
                }
            }
        }
    }

    private static bool TryIndexZoneOptions(
        MarketContextSnapshot index,
        IReadOnlyList<MarketContextSnapshot> context,
        string direction,
        decimal stopOnUnderlying,
        decimal targetOnUnderlying,
        string strategy,
        string trigger,
        string targetNote,
        string stopNote,
        out StrategyCandidate candidate)
    {
        candidate = null!;
        if (!ZoneBoardOptionLegSelector.TryBuildIndexOptionLegs(index, direction, context, out var legs))
        {
            return false;
        }

        var entryRef = direction == "BULLISH" ? index.Last : index.Last;
        var risk = direction == "BULLISH"
            ? index.Last - stopOnUnderlying
            : stopOnUnderlying - index.Last;
        var reward = direction == "BULLISH"
            ? targetOnUnderlying - index.Last
            : index.Last - targetOnUnderlying;
        if (risk <= 0 || reward <= 0)
        {
            return false;
        }

        var debit = legs.Sum(leg => leg.Price);
        if (debit <= 0)
        {
            return false;
        }

        var at = index.Quote.DataTimestamp == default ? DateTimeOffset.UtcNow : index.Quote.DataTimestamp;
        var plan = ZoneBoardOptionEstimator.PlanStrikes(index.Symbol, direction, index.Last, at);
        var score = 92m;
        if (index.Evidence.Trend == (direction == "BULLISH" ? "UP" : "DOWN")) score += 5m;

        candidate = new StrategyCandidate(
            strategy,
            index.Symbol,
            direction,
            score,
            [
                trigger,
                targetNote,
                stopNote,
                "auto-admit: no confirmation (zone CSV playbook)",
                $"options ITM {plan.ItmStrike} + OTM {plan.OtmStrike} {plan.OptionRight}",
                $"zone={index.Evidence.Zone}",
            ],
            legs,
            stopOnUnderlying,
            targetOnUnderlying,
            Math.Max(debit * 0.45m, risk),
            Math.Max(debit * 1.2m, reward),
            legs.Max(l => l.Quote is null || l.Quote.DataTimestamp == default
                ? DateTimeOffset.UtcNow
                : l.Quote.DataTimestamp));
        return true;
    }

    private static bool TryZones(
        IReadOnlyList<string> facts,
        out decimal b2,
        out decimal b1,
        out decimal pivot,
        out decimal s1,
        out decimal s2)
    {
        b2 = b1 = pivot = s1 = s2 = 0;
        return ZoneRejectionPattern.TryReadZoneFact(facts, "B2=", out b2)
            && ZoneRejectionPattern.TryReadZoneFact(facts, "B1=", out b1)
            && ZoneRejectionPattern.TryReadZoneFact(facts, "S=", out pivot)
            && ZoneRejectionPattern.TryReadZoneFact(facts, "S1=", out s1)
            && ZoneRejectionPattern.TryReadZoneFact(facts, "S2=", out s2);
    }
}
