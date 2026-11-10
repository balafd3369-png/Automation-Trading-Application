using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Live stock scan: morning strength → intraday fade → gap-down continuation (zone playbook).
/// </summary>
public static class StockReversalPatternScanner
{
    private const decimal GapDownThresholdPct = -0.35m;
    private const decimal GapUpThresholdPct = 0.35m;
    private const decimal MaxOpenVsLastDriftPct = 6m;
    private const decimal MaxGapDisplayPct = 12m;

    public static bool HasSaneSessionOpenForRow(ReversalScanRow r)
    {
        if (r.TodayOpen <= 0 || r.Last <= 0 || r.PreviousClose <= 0)
        {
            return false;
        }

        var openVsLast = Math.Abs(r.TodayOpen - r.Last) / r.Last * 100m;
        return openVsLast <= MaxOpenVsLastDriftPct;
    }

    public static ReversalScanResponse Scan(BoardResponse board)
    {
        var openBoard = OpenVsZonesBoardBuilder.Build(board);
        var failedMorning = new List<ReversalScanRow>();
        var activeFades = new List<ReversalScanRow>();
        var gapContinuations = new List<ReversalScanRow>();
        var earlyWarnings = new List<ReversalScanRow>();

        foreach (var row in openBoard.Stocks)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            if (snap is null || !HasSaneSessionOpenCore(snap))
            {
                continue;
            }

            var gapPct = GapVsPreviousClosePercent(snap);
            Classify(row, snap, gapPct, failedMorning, activeFades, gapContinuations, earlyWarnings);
        }

        Sort(activeFades);
        Sort(gapContinuations);
        Sort(earlyWarnings);
        Sort(failedMorning);

        var stats = new ReversalClusterStats(
            openBoard.Stocks.Count,
            failedMorning.Count,
            activeFades.Count,
            gapContinuations.Count,
            earlyWarnings.Count,
            CountGapDown(openBoard.Stocks, board),
            CountGapUp(openBoard.Stocks, board),
            board.Counts.Buying,
            board.Counts.Selling,
            MarketBreadthGate.Describe(board.Counts));

        return new ReversalScanResponse(
            board.UpdatedAt,
            board.Feed,
            Playbook(),
            stats,
            TakeTop(activeFades, 30),
            TakeTop(gapContinuations, 30),
            TakeTop(earlyWarnings, 30),
            TakeTop(failedMorning, 30));
    }

    private static void Classify(
        OpenVsZonesRow row,
        SymbolSnapshot snap,
        decimal gapPct,
        List<ReversalScanRow> failedMorning,
        List<ReversalScanRow> activeFades,
        List<ReversalScanRow> gapContinuations,
        List<ReversalScanRow> earlyWarnings)
    {
        var openBull = row.OpenBandKey is "aboveB1" or "aboveB2";
        var ltpBear = row.LtpBandKey is "belowS1" or "belowS2" or "betweenS1S2";
        var falling = row.SessionMove.StartsWith("Falling", StringComparison.Ordinal);
        var selling = row.Mark.Equals("SELLING", StringComparison.OrdinalIgnoreCase);

        if (openBull && ltpBear && falling)
        {
            activeFades.Add(ToRow(
                row,
                snap,
                gapPct,
                "MORNING_FADE",
                "Gunshot fade (live)",
                88,
                "Opened strong (≥ B1) but LTP collapsed into sell zone — same as B1→S2 rejection playbook."));
            return;
        }

        if (openBull && selling)
        {
            failedMorning.Add(ToRow(
                row,
                snap,
                gapPct,
                "FAILED_MORNING",
                "Bull open → selling mark",
                72,
                "09:15 was above B1; board mark is SELLING now — distribution day on this name."));
        }

        if (gapPct <= GapDownThresholdPct
            && (row.OpenBandKey is "belowS1" or "belowS2" or "betweenS1S2"))
        {
            gapContinuations.Add(ToRow(
                row,
                snap,
                gapPct,
                "GAP_DOWN_CONTINUE",
                "Gap down + weak open",
                80,
                "Open gapped down vs previous close and opened in sell-band — bias for follow-through lower / S2."));
            return;
        }

        if (openBull && !ltpBear && falling && row.Last < row.TodayOpen)
        {
            earlyWarnings.Add(ToRow(
                row,
                snap,
                gapPct,
                "EARLY_WARNING",
                "Fading from bullish open",
                65,
                "Still above S1 on LTP but session falling — watch 5m close below B1 then S1."));
        }
    }

    private static ReversalScanRow ToRow(
        OpenVsZonesRow row,
        SymbolSnapshot snap,
        decimal gapPct,
        string patternId,
        string stage,
        int score,
        string detail) =>
        new(
            row.Name,
            patternId,
            stage,
            score,
            detail,
            gapPct,
            row.OpenBand,
            row.LtpBand,
            row.SessionMove,
            row.Mark,
            row.TodayOpen,
            row.Last,
            snap.PreviousClose,
            row.B1,
            row.S1,
            row.S2);

    private static int CountGapDown(IReadOnlyList<OpenVsZonesRow> stocks, BoardResponse board) =>
        stocks.Count(row =>
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            return snap is not null
                && HasSaneSessionOpenCore(snap)
                && GapVsPreviousClosePercent(snap) <= GapDownThresholdPct;
        });

    private static int CountGapUp(IReadOnlyList<OpenVsZonesRow> stocks, BoardResponse board) =>
        stocks.Count(row =>
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            return snap is not null
                && HasSaneSessionOpenCore(snap)
                && GapVsPreviousClosePercent(snap) >= GapUpThresholdPct;
        });

    public static bool HasSaneSessionOpen(SymbolSnapshot snap) => HasSaneSessionOpenCore(snap);

    private static bool HasSaneSessionOpenCore(SymbolSnapshot snap)
    {
        if (snap.TodayOpen <= 0 || snap.Last <= 0 || snap.PreviousClose <= 0)
        {
            return false;
        }

        var openVsLast = Math.Abs(snap.TodayOpen - snap.Last) / snap.Last * 100m;
        return openVsLast <= MaxOpenVsLastDriftPct;
    }

    private static decimal GapVsPreviousClosePercent(SymbolSnapshot snap)
    {
        var raw = (snap.TodayOpen - snap.PreviousClose) / snap.PreviousClose * 100m;
        return Math.Clamp(raw, -MaxGapDisplayPct, MaxGapDisplayPct);
    }

    private static IReadOnlyList<ReversalScanRow> TakeTop(List<ReversalScanRow> rows, int n) =>
        rows.OrderByDescending(r => r.Score).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(n).ToList();

    private static void Sort(List<ReversalScanRow> rows) =>
        rows.Sort((a, b) =>
        {
            var c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

    private static ReversalPlaybook Playbook() =>
        new(
            "Morning positive → negative → next-day gap",
            [
                "Stage 1 — False strength: gap-up or open above B1 while index/stock looks bullish.",
                "Stage 2 — Gunshot fade: price fails B1 and pushes into S1/S2 by session (live: B1 rejection → S2).",
                "Stage 3 — Mark flip: board changes to SELLING with open still in bull band (failed morning).",
                "Stage 4 — Next session: gap down vs previous close with open below S1/S2 (continuation short bias).",
                "Not blind — confirm on 5m structure; use with Analyzer gunshot + S1 break playbooks.",
            ],
            ZoneRejectionPattern.B1RejectionToS2,
            ZoneBoardPattern.S1BreakShort);
}
