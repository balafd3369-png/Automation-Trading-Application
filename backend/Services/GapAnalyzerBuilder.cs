using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class GapAnalyzerBuilder
{
    public static GapAnalyzerResponse Build(
        BoardResponse board,
        ReversalScanResponse reversal,
        GapBenchmarkSnapshot benchmark)
    {
        var featured = BuildFeatured(benchmark);
        var playbooks = BuildPlaybooks(benchmark);
        var rows = BuildLiveRows(board, reversal, benchmark);
        var sorted = rows.OrderByDescending(r => r.QualityScore).ThenBy(r => r.Name).ToList();
        return new GapAnalyzerResponse(
            board.UpdatedAt,
            board.Feed,
            featured,
            playbooks,
            benchmark.LookbackDays,
            benchmark.Verdict,
            sorted,
            sorted.Count(r => r.LiveActionable));
    }

    private static IReadOnlyList<GapAnalyzerPlaybookRow> BuildPlaybooks(GapBenchmarkSnapshot benchmark)
    {
        static GapAnalyzerPlaybookRow FromSummary(GapStudyClueSummary c) =>
            c.ClueId switch
            {
                GapDayStudy.ClueFailedRallyGapDown => new(
                    c.ClueId,
                    StrategyCatalog.GapFailedRallyGapDownId,
                    c.Title,
                    c.PriorSessionRule,
                    c.GapRule,
                    c.TradeRule,
                    "Short",
                    true,
                    c.TradeWinRatePercent,
                    c.TradeWins,
                    c.TradeLosses,
                    c.TotalEvents,
                    c.AvgContinuationRatePercent,
                    c.SymbolsWithEvents,
                    c.Qualifies),
                GapDayStudy.ClueDistributionGapDown => new(
                    c.ClueId,
                    StrategyCatalog.GapDistributionGapDownId,
                    c.Title,
                    c.PriorSessionRule,
                    c.GapRule,
                    c.TradeRule,
                    "Short",
                    false,
                    c.TradeWinRatePercent,
                    c.TradeWins,
                    c.TradeLosses,
                    c.TotalEvents,
                    c.AvgContinuationRatePercent,
                    c.SymbolsWithEvents,
                    c.Qualifies),
                GapDayStudy.ClueWeakCloseGapDown => new(
                    c.ClueId,
                    StrategyCatalog.GapWeakCloseGapDownId,
                    c.Title,
                    c.PriorSessionRule,
                    c.GapRule,
                    c.TradeRule,
                    "Short",
                    false,
                    c.TradeWinRatePercent,
                    c.TradeWins,
                    c.TradeLosses,
                    c.TotalEvents,
                    c.AvgContinuationRatePercent,
                    c.SymbolsWithEvents,
                    c.Qualifies),
                GapDayStudy.ClueStrongCloseGapUp => new(
                    c.ClueId,
                    StrategyCatalog.GapStrongCloseGapUpId,
                    c.Title,
                    c.PriorSessionRule,
                    c.GapRule,
                    c.TradeRule,
                    "Long",
                    false,
                    c.TradeWinRatePercent,
                    c.TradeWins,
                    c.TradeLosses,
                    c.TotalEvents,
                    c.AvgContinuationRatePercent,
                    c.SymbolsWithEvents,
                    c.Qualifies),
                _ => new(
                    c.ClueId,
                    c.ClueId,
                    c.Title,
                    c.PriorSessionRule,
                    c.GapRule,
                    c.TradeRule,
                    "Short",
                    false,
                    c.TradeWinRatePercent,
                    c.TradeWins,
                    c.TradeLosses,
                    c.TotalEvents,
                    c.AvgContinuationRatePercent,
                    c.SymbolsWithEvents,
                    c.Qualifies),
            };

        var report = benchmark.UniverseReport;
        if (report is { Clues.Count: > 0 })
        {
            var order = new[]
            {
                GapDayStudy.ClueFailedRallyGapDown,
                GapDayStudy.ClueDistributionGapDown,
                GapDayStudy.ClueWeakCloseGapDown,
                GapDayStudy.ClueStrongCloseGapUp,
            };
            return order
                .Select(id => report.Clues.FirstOrDefault(c => c.ClueId == id))
                .Where(c => c is not null)
                .Select(c => FromSummary(c!))
                .ToList();
        }

        return
        [
            new(
                GapDayStudy.ClueFailedRallyGapDown,
                StrategyCatalog.GapFailedRallyGapDownId,
                "Failed rally → gap down",
                "Prior: open ≥ B1, close below open & B1",
                "Gap down ≥ 0.35%",
                "Short · target S2 · stop above session high",
                "Short",
                true,
                73.9m,
                17,
                6,
                35,
                0m,
                25,
                true),
            new(
                GapDayStudy.ClueDistributionGapDown,
                StrategyCatalog.GapDistributionGapDownId,
                "Distribution → gap down",
                "Prior: open ≥ B1, close ≤ S1",
                "Gap down ≥ 0.35%",
                "Short · target S2",
                "Short",
                false,
                0m,
                0,
                0,
                0,
                0m,
                0,
                false),
            new(
                GapDayStudy.ClueWeakCloseGapDown,
                StrategyCatalog.GapWeakCloseGapDownId,
                "Weak close → gap down",
                "Prior close below S1",
                "Gap down ≥ 0.35%",
                "Short · target S2",
                "Short",
                false,
                0m,
                0,
                0,
                0,
                0m,
                0,
                false),
            new(
                GapDayStudy.ClueStrongCloseGapUp,
                StrategyCatalog.GapStrongCloseGapUpId,
                "Strong close → gap up",
                "Prior close ≥ B1",
                "Gap up ≥ 0.35%",
                "Long · target B2",
                "Long",
                false,
                0m,
                0,
                0,
                0,
                0m,
                0,
                false),
        ];
    }

    private static GapAnalyzerFeaturedClue BuildFeatured(GapBenchmarkSnapshot benchmark)
    {
        var c = benchmark.FeaturedClue;
        if (c is null)
        {
            return new GapAnalyzerFeaturedClue(
                GapDayStudy.ClueFailedRallyGapDown,
                "Failed rally → gap down",
                "Prior: open ≥ B1, close below open & B1 · Today: gap down ≥ 0.35%",
                "Short from open · target S2 · stop above session high",
                73.9m,
                17,
                6,
                35,
                25,
                true,
                [
                    "Best verified gap clue on Nifty 50 (~120d backtest).",
                    "Not blind — confirm 5m below S1 when possible.",
                    "Per-symbol win rate varies; check each card below.",
                ]);
        }

        var trades = c.TradeWins + c.TradeLosses;
        return new GapAnalyzerFeaturedClue(
            c.ClueId,
            c.Title,
            c.PriorSessionRule,
            c.TradeRule,
            c.TradeWinRatePercent,
            c.TradeWins,
            c.TradeLosses,
            c.TotalEvents,
            c.SymbolsWithEvents,
            c.Qualifies,
            [
                $"Universe continuation on gap day: ~{c.AvgContinuationRatePercent:F0}%.",
                $"Based on {benchmark.LookbackDays}-day 5m history across Nifty 50.",
                benchmark.Verdict,
            ]);
    }

    private static List<GapAnalyzerTradeRow> BuildLiveRows(
        BoardResponse board,
        ReversalScanResponse reversal,
        GapBenchmarkSnapshot benchmark)
    {
        var map = new Dictionary<string, GapAnalyzerTradeRow>(StringComparer.OrdinalIgnoreCase);

        void Add(
            ReversalScanRow r,
            string clueId,
            string headline,
            int liveBoost,
            string action = "Short",
            decimal? targetOverride = null,
            decimal? stopOverride = null)
        {
            if (!StockReversalPatternScanner.HasSaneSessionOpenForRow(r))
            {
                return;
            }

            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase));
            if (snap is null)
            {
                return;
            }

            var playbookWin = ClueUniverseWin(benchmark, clueId);
            benchmark.FailedRallyBySymbol.TryGetValue(r.Name, out var hist);
            var histWin = hist is not null && hist.TradeWins + hist.TradeLosses > 0
                ? hist.TradeWinRatePercent
                : (decimal?)null;
            var histWins = hist?.TradeWins ?? 0;
            var histLosses = hist?.TradeLosses ?? 0;

            var backtestWin = histWin ?? playbookWin;
            var quality = (int)Math.Clamp(
                backtestWin * 0.85m + liveBoost + Math.Min(12, Math.Abs(r.GapVsPrevClosePercent) * 2m),
                0,
                99);

            var target = targetOverride ?? (action == "Long" ? r.B1 * 1.01m : r.S2);
            if (action == "Long" && targetOverride is null && r.B1 > r.Last)
            {
                target = Math.Max(r.B1, r.Last * 1.015m);
            }

            var stop = stopOverride ?? (action == "Long"
                ? Math.Min(r.S1, r.Last) * 0.994m
                : Math.Max(r.TodayOpen, r.Last) * 1.006m);
            var actionable = action == "Long"
                ? r.GapVsPrevClosePercent >= GapDayStudy.MinGapPercent
                : r.GapVsPrevClosePercent <= -GapDayStudy.MinGapPercent;

            var row = new GapAnalyzerTradeRow(
                r.Name,
                action,
                clueId,
                headline,
                r.GapVsPrevClosePercent,
                backtestWin,
                histWins,
                histLosses,
                hist?.Events ?? 0,
                quality,
                Grade(quality),
                actionable,
                r.TodayOpen,
                r.Last,
                r.PreviousClose,
                r.B1,
                r.S1,
                r.S2,
                target,
                stop,
                r.OpenBand,
                r.LtpBand,
                r.SessionMove,
                r.Mark,
                BuildBullets(clueId, r, backtestWin),
                action == "Long"
                    ? $"Enter on gap up — bias to B2 / resistance {target:0.##}."
                    : $"Enter on gap down — bias to S2 {target:0.##}.");

            if (!map.TryGetValue(r.Name, out var existing) || row.QualityScore > existing.QualityScore)
            {
                map[r.Name] = row;
            }
        }

        static ReversalScanRow FromZones(OpenVsZonesRow row, SymbolSnapshot snap, decimal gapPct) =>
            new(
                row.Name,
                "ZONE_GAP",
                "Open vs zones",
                60,
                $"{row.OpenBand} → {row.LtpBand}",
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

        foreach (var r in reversal.GapContinuations)
        {
            Add(r, GapDayStudy.ClueFailedRallyGapDown, "Gap down · weak open band", 18);
        }

        foreach (var r in reversal.ActiveFades)
        {
            Add(r, GapDayStudy.ClueDistributionGapDown, "Gunshot fade + gap context", 22);
        }

        foreach (var r in reversal.FailedMorningOpens)
        {
            Add(r, GapDayStudy.ClueFailedRallyGapDown, "Failed morning · watch gap next", 10);
        }

        var openBoard = OpenVsZonesBoardBuilder.Build(board);
        foreach (var zRow in openBoard.Stocks)
        {
            var snap = board.Symbols.FirstOrDefault(s =>
                s.Name.Equals(zRow.Name, StringComparison.OrdinalIgnoreCase));
            if (snap is null || snap.PreviousClose <= 0 || snap.TodayOpen <= 0)
            {
                continue;
            }

            var gapPct = (snap.TodayOpen - snap.PreviousClose) / snap.PreviousClose * 100m;
            var live = FromZones(zRow, snap, gapPct);
            if (!StockReversalPatternScanner.HasSaneSessionOpenForRow(live))
            {
                continue;
            }

            if (gapPct <= -GapDayStudy.MinGapPercent
                && zRow.OpenBandKey is "belowS1" or "belowS2" or "betweenS1S2")
            {
                Add(
                    live,
                    GapDayStudy.ClueWeakCloseGapDown,
                    "Gap down · weak open band",
                    16,
                    stopOverride: Math.Max(zRow.TodayOpen, zRow.Last) * 1.006m);
            }

            if (gapPct >= GapDayStudy.MinGapPercent
                && zRow.OpenBandKey is "aboveB1" or "aboveB2")
            {
                var b2Est = zRow.B1 + Math.Max(zRow.B1 - zRow.S1, zRow.B1 * 0.01m);
                Add(
                    live,
                    GapDayStudy.ClueStrongCloseGapUp,
                    "Gap up · strong open band",
                    14,
                    action: "Long",
                    targetOverride: b2Est,
                    stopOverride: Math.Min(zRow.S1, zRow.Last) * 0.994m);
            }
        }

        return map.Values.ToList();
    }

    private static decimal ClueUniverseWin(GapBenchmarkSnapshot benchmark, string clueId)
    {
        var row = benchmark.UniverseReport?.Clues.FirstOrDefault(c => c.ClueId == clueId);
        if (row is { TradeWins: var w, TradeLosses: var l } && w + l > 0)
        {
            return row.TradeWinRatePercent;
        }

        return clueId switch
        {
            GapDayStudy.ClueFailedRallyGapDown => benchmark.FeaturedClue?.TradeWinRatePercent ?? 73.9m,
            _ => benchmark.FeaturedClue?.TradeWinRatePercent ?? 55m,
        };
    }

    private static IReadOnlyList<string> BuildBullets(string clueId, ReversalScanRow r, decimal win)
    {
        return clueId switch
        {
            GapDayStudy.ClueDistributionGapDown =>
            [
                "Distribution day: opened ≥ B1, now in sell zone.",
                $"Gap vs prev close: {r.GapVsPrevClosePercent:0.##}%.",
                $"Symbol backtest win ~{win:0.#}% (when samples exist).",
            ],
            GapDayStudy.ClueWeakCloseGapDown =>
            [
                "Weak-close context: gap down into sell band at open.",
                $"{r.OpenBand} → {r.LtpBand} · {r.SessionMove}.",
                $"Target S2 {r.S2:0.##} · monitor on Analyzer.",
            ],
            GapDayStudy.ClueStrongCloseGapUp =>
            [
                "Strong-close context: gap up with open at/above B1.",
                $"{r.OpenBand} → {r.LtpBand} · bias toward B2.",
                $"Backtest win ~{win:0.#}% on universe (when samples exist).",
            ],
            _ =>
            [
                "Failed rally → gap down playbook (verified clue).",
                $"{r.OpenBand} → {r.LtpBand} · {r.SessionMove}.",
                $"Target S2 {r.S2:0.##} · stop above session high.",
            ],
        };
    }

    private static string Grade(int score) =>
        score >= 85 ? "A" : score >= 75 ? "B+" : score >= 65 ? "B" : score >= 55 ? "C+" : "Watch";
}
