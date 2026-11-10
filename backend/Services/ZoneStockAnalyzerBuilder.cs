using TradeWithBeast.Formulas;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Live zone playbook scan (B1/B2/S1/S2) — buy / sell / watch lists for Select Stock analyzer.
/// Refreshes with every board poll; aligns with <see cref="ZoneBoardPattern"/> automation rules.
/// </summary>
public static class ZoneStockAnalyzerBuilder
{
    private const decimal NearZonePct = 0.0035m;

    public static ZoneStockAnalyzerResponse Build(BoardResponse board)
    {
        var openBoard = OpenVsZonesBoardBuilder.Build(board);
        var buy = new List<ZoneStockAnalyzerRow>();
        var sell = new List<ZoneStockAnalyzerRow>();
        var watch = new List<ZoneStockAnalyzerRow>();

        void Analyze(OpenVsZonesRow row, SymbolSnapshot? snap)
        {
            if (snap is { TightRange: true })
            {
                watch.Add(ToRow(board.Counts, row, "Watch", ZoneBoardPattern.B1BreakLong, "low",
                    "Tight B1–S1 range",
                    "Skipped for auto zone entries — wait for expansion."));
                return;
            }

            var last = row.Last;
            var mark = row.Mark?.ToUpperInvariant() ?? "";

            if (mark == "BUYING" && last >= row.B1)
            {
                buy.Add(ToRow(board.Counts, row, "Buy", ZoneBoardPattern.B1BreakLong, "high",
                    "Buying board · holds above B1",
                    $"Target B2 {row.B2:0.##} · stop reference S {row.S:0.##}."));
                return;
            }

            if (mark == "SELLING" && last <= row.S1)
            {
                sell.Add(ToRow(board.Counts, row, "Sell", ZoneBoardPattern.S1BreakShort, "high",
                    "Selling board · at/below S1",
                    $"Target S2 {row.S2:0.##} · stop reference S {row.S:0.##}."));
                return;
            }

            if (row.OpenBandKey is "belowS2" or "betweenS1S2" && last > row.S2
                && row.LtpBandKey is not "belowS2" && row.SessionMove.StartsWith("Rising", StringComparison.Ordinal))
            {
                buy.Add(ToRow(board.Counts, row, "Buy", ZoneBoardPattern.S2BounceLong, "medium",
                    "S2 reclaim / bounce",
                    $"Weak open recovered above S2 {row.S2:0.##} — playbook target B1 {row.B1:0.##}."));
                return;
            }

            if (row.OpenBandKey is "aboveB1" or "aboveB2"
                && (row.LtpBandKey is "belowS1" or "belowS2" or "betweenS1S2")
                && row.SessionMove.StartsWith("Falling", StringComparison.Ordinal))
            {
                sell.Add(ToRow(board.Counts, row, "Sell", ZoneRejectionPattern.B1RejectionToS2, "high",
                    "Gunshot fade · open above B1, LTP into sell zone",
                    "Strong open failed — trail toward S2 on 5m structure."));
                return;
            }

            if (NearLevel(last, row.B1) && last < row.B2)
            {
                watch.Add(ToRow(board.Counts, row, "Watch", ZoneBoardPattern.B1BreakLong, "medium",
                    "Testing B1 break",
                    "5m close above B1 → long bias to B2 (confirm on chart)."));
                return;
            }

            if (NearLevel(last, row.S1) && last > row.S2)
            {
                watch.Add(ToRow(board.Counts, row, "Watch", ZoneBoardPattern.S1BreakShort, "medium",
                    "Testing S1 break",
                    "5m close below S1 → short bias to S2."));
                return;
            }

            if (mark == "RANGE BOUND")
            {
                watch.Add(ToRow(board.Counts, row, "Watch", "ZONE_NEUTRAL", "low",
                    "Range-bound",
                    "Between S1 and B1 — no edge until B1 or S1 breaks."));
            }
        }

        foreach (var row in openBoard.Stocks)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            Analyze(row, snap);
        }

        foreach (var row in openBoard.Indices)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            Analyze(row, snap);
        }

        SortRows(buy);
        SortRows(sell);
        SortRows(watch);

        var breadthNote = MarketBreadthGate.Describe(board.Counts);
        var regime = ZoneAnalyzerScoring.DetectMarketRegime(board.Counts);
        var top = buy.Concat(sell)
            .OrderByDescending(r => r.QualityScore)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(r => new ZoneAnalyzerTopOpportunity(
                r.Name, r.Action, r.PatternId, r.QualityScore, r.Grade, r.RiskClass))
            .ToList();

        return new ZoneStockAnalyzerResponse(
            board.UpdatedAt,
            board.Feed,
            board.Counts,
            breadthNote,
            regime,
            top,
            PlaybookCards(),
            buy,
            sell,
            watch);
    }

    private static ZoneStockAnalyzerRow ToRow(
        BoardCounts counts,
        OpenVsZonesRow row,
        string action,
        string patternId,
        string confidence,
        string headline,
        string detail)
    {
        var scored = ZoneAnalyzerScoring.Score(row, action, patternId, confidence, counts);
        var (targetPrice, stopPrice) = TargetsForPattern(patternId, row);
        return new(
            row.Name,
            row.IsIndex,
            action,
            patternId,
            headline,
            detail,
            confidence,
            scored.QualityScore,
            scored.Grade,
            scored.RiskClass,
            scored.EntryTiming,
            scored.TriggerDistancePercent,
            scored.WhyBullets,
            ZoneAnalyzerScoring.MtfLabel(scored.MtfIntraday),
            ZoneAnalyzerScoring.MtfLabel(scored.MtfDaily),
            ZoneAnalyzerScoring.MtfLabel(scored.MtfWeekly),
            scored.MtfAlignmentScore,
            row.TodayOpen,
            row.Last,
            row.OpenBand,
            row.LtpBand,
            row.SessionMove,
            row.Mark,
            row.TightRange,
            row.B1,
            row.B2,
            row.S1,
            row.S2,
            row.S,
            targetPrice,
            stopPrice);
    }

    private static (decimal Target, decimal Stop) TargetsForPattern(string patternId, OpenVsZonesRow row) =>
        patternId switch
        {
            ZoneBoardPattern.B1BreakLong => (row.B2, row.S),
            ZoneBoardPattern.S1BreakShort => (row.S2, row.S),
            ZoneBoardPattern.S2BounceLong => (row.B1, row.S2),
            ZoneRejectionPattern.B1RejectionToS2 => (row.S2, row.B1),
            _ => (row.B2, row.S),
        };

    private static bool NearLevel(decimal price, decimal level) =>
        level > 0 && price > 0
        && Math.Abs(price - level) / level <= NearZonePct;

    private static void SortRows(List<ZoneStockAnalyzerRow> rows) =>
        rows.Sort((a, b) =>
        {
            var s = b.QualityScore.CompareTo(a.QualityScore);
            if (s != 0)
            {
                return s;
            }

            var c = ConfidenceRank(b.Confidence).CompareTo(ConfidenceRank(a.Confidence));
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

    private static int ConfidenceRank(string confidence) =>
        confidence switch
        {
            "high" => 3,
            "medium" => 2,
            _ => 1,
        };

    private static IReadOnlyList<ZoneStrategyPlaybookCard> PlaybookCards() =>
    [
        new(
            ZoneBoardPattern.B1BreakLong,
            "B1 break → B2",
            "Buy / long",
            "5m close crosses above B1",
            $"B2 · min {ZoneBoardPattern.MinWinRateToRecommend}% WR over {ZoneBoardPattern.MinBacktestSamples}+ sessions (backtest)",
            $"Stop reference S pivot"),
        new(
            ZoneBoardPattern.S1BreakShort,
            "S1 break → S2",
            "Sell / short",
            "5m close crosses below S1",
            $"S2 · min {ZoneBoardPattern.MinWinRateToRecommend}% WR",
            "Stop reference S pivot"),
        new(
            ZoneBoardPattern.S2BounceLong,
            "S2 bounce → B1",
            "Buy / long",
            "Reclaim S2 after touch",
            $"B1 · min {ZoneBoardPattern.MinWinRateToRecommend}% WR",
            "Below S2"),
        new(
            ZoneRejectionPattern.B1RejectionToS2,
            "B1 rejection → S2",
            "Sell / short",
            "Failed breakout above B1",
            "S2 on spot rejection setups",
            "Above B1"),
    ];
}
