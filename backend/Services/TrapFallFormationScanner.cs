using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Live LUPIN-style trap: week rollover → B1 test → S1 break (early stages for trading, not EOD stats).
/// </summary>
public static class TrapFallFormationScanner
{
    private const decimal NearZonePct = 0.0035m;

    public static TrapFallFormationResponse Scan(
        BoardResponse board,
        ZoneBoardSessionTracker sessions,
        DateOnly istDay)
    {
        var openBoard = OpenVsZonesBoardBuilder.Build(board);
        var rows = new List<TrapFallFormationRow>();

        void ScanRow(OpenVsZonesRow row, SymbolSnapshot snap)
        {
            if (!snap.QuoteVerified || snap.TightRange)
            {
                return;
            }

            sessions.Update(snap.Name, snap.Last, snap.B1, snap.S1, snap.S2, istDay);
            var session = sessions.TryGetSession(snap.Name, istDay);
            var week = DescribeWeekBias(snap);
            var (stage, headline, detail, urgency) = ClassifyStage(snap, row, session, week);
            if (stage == TrapFallFormationStage.None)
            {
                return;
            }

            var distB1Pct = snap.B1 > 0
                ? decimal.Round((snap.B1 - snap.Last) / snap.B1 * 100m, 2)
                : 0m;
            var distS1Pct = snap.S1 > 0
                ? decimal.Round((snap.Last - snap.S1) / snap.S1 * 100m, 2)
                : 0m;

            rows.Add(new TrapFallFormationRow(
                snap.Name,
                IndexUniverse.IsIndex(snap.Name),
                stage,
                urgency,
                headline,
                detail,
                week,
                snap.Last,
                snap.B1,
                snap.S1,
                snap.S2,
                snap.S,
                row.SessionMove,
                session?.TouchedB1 ?? false,
                session?.TouchedS1 ?? false,
                session?.SessionHigh,
                distB1Pct,
                distS1Pct,
                ZoneBoardPattern.S1BreakShort));
        }

        foreach (var row in openBoard.Stocks)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            if (snap is not null)
            {
                ScanRow(row, snap);
            }
        }

        foreach (var row in openBoard.Indices)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            if (snap is not null)
            {
                ScanRow(row, snap);
            }
        }

        rows.Sort((a, b) =>
        {
            var stageCmp = StagePriority(b.Stage).CompareTo(StagePriority(a.Stage));
            return stageCmp != 0
                ? stageCmp
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new TrapFallFormationResponse(
            DateTimeOffset.UtcNow,
            board.Feed,
            TrapFallFormationPlaybook.Summary,
            rows.Count(r => r.Stage >= TrapFallFormationStage.B1Tested),
            rows.Count(r => r.Stage == TrapFallFormationStage.EnterShort),
            rows);
    }

    private static int StagePriority(TrapFallFormationStage stage) => stage switch
    {
        TrapFallFormationStage.EnterShort => 5,
        TrapFallFormationStage.B1Tested => 4,
        TrapFallFormationStage.ApproachB1 => 3,
        TrapFallFormationStage.WeekWatch => 2,
        TrapFallFormationStage.Completed => 1,
        _ => 0,
    };

    private static string DescribeWeekBias(SymbolSnapshot snap)
    {
        var lowerHigh = snap.HighTwoDaysAgo > 0
            && snap.YesterdayHigh < snap.HighTwoDaysAgo * (1m - ZoneBoardPattern.TouchTolerance);
        var weakClose = snap.YesterdayHigh > 0
            && snap.PreviousClose < snap.YesterdayHigh * (1m - ZoneBoardPattern.TouchTolerance);
        if (lowerHigh && weakClose)
        {
            return "LowerHighs_WeakClose";
        }

        if (lowerHigh)
        {
            return "LowerHighs";
        }

        if (weakClose)
        {
            return "WeakClose";
        }

        return "Neutral";
    }

    private static (TrapFallFormationStage Stage, string Headline, string Detail, string Urgency) ClassifyStage(
        SymbolSnapshot snap,
        OpenVsZonesRow row,
        ZoneBoardSessionSnapshot? session,
        string weekBias)
    {
        var touchedB1 = session?.TouchedB1 == true
            || snap.Last >= snap.B1 * (1m - ZoneBoardPattern.TouchTolerance)
            || snap.TodayOpen >= snap.B1 * (1m - ZoneBoardPattern.TouchTolerance);
        var touchedS1 = session?.TouchedS1 == true
            || snap.Last <= snap.S1 * (1m + ZoneBoardPattern.TouchTolerance);
        var weekOk = weekBias is "LowerHighs" or "LowerHighs_WeakClose" or "WeakClose";
        var openMid = row.OpenBandKey is "betweenS1B1" or "aboveB1" or "aboveB2";
        var falling = row.SessionMove.StartsWith("Falling", StringComparison.Ordinal);

        if (touchedB1 && touchedS1 && snap.Last <= snap.S2 * (1m + ZoneBoardPattern.TouchTolerance))
        {
            return (
                TrapFallFormationStage.Completed,
                "Target S2 reached",
                "Trap fade played out — book or trail.",
                "low");
        }

        if (touchedB1 && touchedS1 && (falling || snap.Last <= snap.S1))
        {
            return (
                TrapFallFormationStage.EnterShort,
                "Enter · S1 break after B1 test",
                $"5m close below S1 {snap.S1:0.##} → short toward S2 {snap.S2:0.##}, stop ref S {snap.S:0.##}.",
                "high");
        }

        if (touchedB1 && snap.Last > snap.S1)
        {
            return (
                TrapFallFormationStage.B1Tested,
                "Armed · B1 tested, S1 intact",
                "Pattern forming — wait for 5m close below S1 (do not front-run).",
                "high");
        }

        if (weekOk && openMid && NearLevel(snap.Last, snap.B1) && snap.Last < snap.B1)
        {
            return (
                TrapFallFormationStage.ApproachB1,
                "Watch · approaching B1",
                $"Week bias {weekBias.Replace('_', ' ')} — lift toward B1 {snap.B1:0.##} may trap longs.",
                "medium");
        }

        if (weekOk && openMid)
        {
            return (
                TrapFallFormationStage.WeekWatch,
                "Watch · week rollover",
                "Structure weakening — alert if session tests B1 then loses S1.",
                "low");
        }

        return (TrapFallFormationStage.None, "", "", "");
    }

    private static bool NearLevel(decimal last, decimal level)
    {
        if (level <= 0)
        {
            return false;
        }

        return Math.Abs(last - level) / level <= NearZonePct;
    }
}

public static class TrapFallFormationPlaybook
{
    public const string Summary =
        "Live trap-fall radar: week bias → B1 test → 5m S1 break. Enter on S1 break after B1 touch, target S2.";
}
