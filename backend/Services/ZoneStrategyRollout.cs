namespace TradeWithBeast.Services;

/// <summary>
/// Phased rollout: monitor signals in paper first, then auto-execute only where 90d backtest qualifies.
/// Live broker auto remains off until paper P&amp;L is verified over weeks/months (see LiveTradingNote).
/// </summary>
public static class ZoneStrategyRollout
{
    public const string PhaseMonitor = "monitor";
    public const string PhasePaperAuto = "paper_auto";
    public const string PhaseLiveBlocked = "live_blocked";

    public sealed record Row(
        string StrategyCode,
        string StrategyLabel,
        string Index,
        string Phase,
        string Reason);

    public sealed record Snapshot(
        string PolicyVersion,
        int LookbackDays,
        decimal MinWinRatePercent,
        int MinSamples,
        string LiveTradingNote,
        IReadOnlyList<Row> Rows);

    private static readonly (string Pattern, string Label)[] ZonePatterns =
    [
        (ZoneBoardPattern.B1BreakLong, "B1 break → B2"),
        (ZoneBoardPattern.S1BreakShort, "S1 break → S2"),
        (ZoneBoardPattern.S2BounceLong, "S2 bounce → B1"),
    ];

    /// <summary>
    /// Paper auto-execute gates from ~90d index backtest (Oct 2026). Re-run backtest and update when promoting strategies.
    /// </summary>
    private static string PhaseFor(string pattern, string index)
    {
        if (pattern == ZoneBoardPattern.B1BreakLong)
        {
            return index switch
            {
                "BANKNIFTY" or "FINNIFTY" or "SENSEX" => PhasePaperAuto,
                "NIFTY" => PhaseMonitor,
                _ => PhaseMonitor,
            };
        }

        if (pattern == ZoneBoardPattern.S1BreakShort)
        {
            return index switch
            {
                "NIFTY" or "SENSEX" => PhasePaperAuto,
                "BANKNIFTY" or "FINNIFTY" => PhaseMonitor,
                _ => PhaseMonitor,
            };
        }

        if (pattern == ZoneBoardPattern.S2BounceLong)
        {
            return PhaseMonitor;
        }

        return PhaseMonitor;
    }

    private static string ReasonFor(string pattern, string index, string phase)
    {
        if (phase == PhasePaperAuto)
        {
            return "90d backtest: win rate and samples meet threshold; paper auto-admit enabled.";
        }

        if (pattern == ZoneBoardPattern.S2BounceLong)
        {
            return "90d backtest: too few setups; monitor signals only until more data.";
        }

        if (pattern == ZoneBoardPattern.B1BreakLong && index == "NIFTY")
        {
            return "90d backtest: win rate below 55%; monitor and review before auto.";
        }

        if (pattern == ZoneBoardPattern.S1BreakShort && (index == "BANKNIFTY" || index == "FINNIFTY"))
        {
            return "90d backtest: weak or mixed points P&L; monitor before auto.";
        }

        return "Monitor phase: log decisions, no auto entry until promoted.";
    }

    public static string GetPhase(string strategyCode, string index) =>
        PhaseFor(strategyCode, index.ToUpperInvariant());

    public static bool AllowPaperAutoExecute(string strategyCode, string underlying)
    {
        if (!ZoneBoardLiveScope.IsZoneCsvStrategy(strategyCode))
        {
            return true;
        }

        if (!ZoneBoardLiveScope.IsZoneIndex(underlying))
        {
            return false;
        }

        return GetPhase(strategyCode, underlying) == PhasePaperAuto;
    }

    public static Snapshot BuildSnapshot()
    {
        var rows = new List<Row>();
        foreach (var (pattern, label) in ZonePatterns)
        {
            foreach (var index in ZoneBoardLiveScope.Indices)
            {
                var phase = PhaseFor(pattern, index);
                rows.Add(new Row(
                    pattern,
                    label,
                    index,
                    phase,
                    ReasonFor(pattern, index, phase)));
            }
        }

        return new(
            "2026-10-zone-90d-v1",
            90,
            ZoneBoardPattern.MinWinRateToRecommend,
            ZoneBoardPattern.MinBacktestSamples,
            "Live broker auto for zone strategies stays disabled until paper auto shows stable profit over your review period.",
            rows);
    }
}
