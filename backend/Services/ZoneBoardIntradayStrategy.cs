using TradeWithBeast.Broker;
using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Image-based zone board: buy on B1/S2 setups, sell on S1/B1 rejection; exit at target or pivot stop (S).
/// </summary>
public sealed class ZoneBoardIntradayStrategy(ZoneBoardSessionTracker sessions) : IPaperStrategy
{
    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist).DateTime);

        foreach (var item in context.Where(x => x.InstrumentType is "STOCK" or "INDEX"))
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
                if (IndexUniverse.IsIndex(item.Symbol)
                    && TryIndexZoneOptions(
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
                else if (TryLong(item, b1, pivot, b2, ZoneBoardPattern.B1BreakLong,
                        $"5m-style cross above B1 {b1:0.##} (buy zone)",
                        $"target B2 {b2:0.##}",
                        $"stop pivot S {pivot:0.##}",
                        "exit: target B2 or stop at S / 15:20 square-off",
                        out var b1Long))
                {
                    yield return b1Long;
                }
            }

            if (sessions.TryTakeCross(item.Symbol, istDay, ZoneBoardPattern.S1BreakShort, prior, item.Last, b1, s1))
            {
                if (IndexUniverse.IsIndex(item.Symbol)
                    && TryIndexZoneOptions(
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
                else if (TryShort(item, s1, pivot, s2, ZoneBoardPattern.S1BreakShort,
                        $"cross below S1 {s1:0.##} (sell zone)",
                        $"target S2 {s2:0.##}",
                        $"stop pivot S {pivot:0.##}",
                        "exit: target S2 or stop at S / 15:20 square-off",
                        out var s1Short))
                {
                    yield return s1Short;
                }
            }

            if (sessions.TryTakeS2Bounce(item.Symbol, istDay, prior, item.Last, s2))
            {
                if (IndexUniverse.IsIndex(item.Symbol)
                    && TryIndexZoneOptions(
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
                else if (TryLong(item, s2, Math.Min(s2, item.Last) * (1m - ZoneBoardPattern.TouchTolerance), b1,
                        ZoneBoardPattern.S2BounceLong,
                        $"S2 support touch then reclaim {s2:0.##}",
                        $"target B1 {b1:0.##}",
                        $"stop below S2",
                        "exit: target B1 or stop under S2 / 15:20 square-off",
                        out var s2Long))
                {
                    yield return s2Long;
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

    private static bool TryLong(
        MarketContextSnapshot item,
        decimal entryRef,
        decimal stop,
        decimal target,
        string strategy,
        string trigger,
        string targetNote,
        string stopNote,
        string exitNote,
        out StrategyCandidate candidate)
    {
        candidate = null!;
        var entry = item.Last;
        var risk = entry - stop;
        var reward = target - entry;
        if (risk <= 0 || reward <= 0 || entry <= entryRef)
        {
            return false;
        }

        var score = 62m;
        if (item.Evidence.Trend == "UP") score += 8m;
        if (item.Evidence.MomentumPercent > 0) score += 5m;

        candidate = new StrategyCandidate(
            strategy,
            item.Symbol,
            "BULLISH",
            score,
            [trigger, targetNote, stopNote, exitNote, $"zone={item.Evidence.Zone}"],
            [new(item.Symbol, item.Symbol, item.InstrumentType, "BUY", 1, entry, Quote: item.Quote)],
            stop,
            target,
            risk,
            reward,
            item.Quote.DataTimestamp);
        return true;
    }

    private static bool TryShort(
        MarketContextSnapshot item,
        decimal entryRef,
        decimal stop,
        decimal target,
        string strategy,
        string trigger,
        string targetNote,
        string stopNote,
        string exitNote,
        out StrategyCandidate candidate)
    {
        candidate = null!;
        var entry = item.Last;
        var risk = stop - entry;
        var reward = entry - target;
        if (risk <= 0 || reward <= 0 || entry >= entryRef)
        {
            return false;
        }

        var score = 62m;
        if (item.Evidence.Trend == "DOWN") score += 8m;
        if (item.Evidence.MomentumPercent < 0) score += 5m;

        candidate = new StrategyCandidate(
            strategy,
            item.Symbol,
            "BEARISH",
            score,
            [trigger, targetNote, stopNote, exitNote, $"zone={item.Evidence.Zone}"],
            [new(item.Symbol, item.Symbol, item.InstrumentType, "SELL", 1, entry, Quote: item.Quote)],
            stop,
            target,
            risk,
            reward,
            item.Quote.DataTimestamp);
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
