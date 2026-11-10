using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class ZoneAnalyzerScoring
{
    public sealed record ScoredRow(
        int QualityScore,
        string Grade,
        string RiskClass,
        string EntryTiming,
        decimal TriggerDistancePercent,
        IReadOnlyList<string> WhyBullets,
        int MtfIntraday,
        int MtfDaily,
        int MtfWeekly,
        int MtfAlignmentScore);

    public static string DetectMarketRegime(BoardCounts counts)
    {
        var total = counts.Buying + counts.Selling + counts.RangeBound;
        if (total < 4)
        {
            return "Neutral";
        }

        var selling = Math.Max(1, counts.Selling);
        var ratio = counts.Buying / (decimal)selling;
        var rangeShare = counts.RangeBound / (decimal)Math.Max(1, total);

        if (ratio >= 2.5m && counts.Buying >= 8)
        {
            return "Strong Bullish";
        }

        if (ratio >= 1.4m && counts.Buying > counts.Selling)
        {
            return "Bullish";
        }

        if (ratio <= 0.4m && counts.Selling >= 8)
        {
            return "Strong Bearish";
        }

        if (ratio <= 0.72m && counts.Selling > counts.Buying)
        {
            return "Bearish";
        }

        if (rangeShare >= 0.42m)
        {
            return "Neutral";
        }

        return "Neutral";
    }

    public static ScoredRow Score(
        OpenVsZonesRow row,
        string action,
        string patternId,
        string confidence,
        BoardCounts counts)
    {
        var patternPts = PatternQualityPoints(confidence, patternId);
        var breadthPts = BreadthAlignmentPoints(action, row.Mark, counts);
        var momentumPts = SessionMomentumPoints(action, row.SessionMove);
        var zonePts = ZoneDistancePoints(action, patternId, row.Last, row);
        var historyPts = 5m; // filled on expand via backtest; neutral mid-score live

        var raw = patternPts + breadthPts + momentumPts + zonePts + historyPts;
        var score = (int)Math.Clamp(Math.Round(raw), 0, 100);
        var grade = GradeFromScore(score);
        var (timing, distPct) = EntryTiming(action, patternId, row);
        var risk = ClassifyRisk(row, distPct);
        var why = BuildWhyBullets(row, action, patternId, counts, score);
        var (i, d, w, align) = MultiTimeframe(action, row);

        return new ScoredRow(score, grade, risk, timing, distPct, why, i, d, w, align);
    }

    private static decimal PatternQualityPoints(string confidence, string patternId)
    {
        var basePts = confidence.ToLowerInvariant() switch
        {
            "high" => 36m,
            "medium" => 28m,
            _ => 18m,
        };
        if (patternId is ZoneBoardPattern.B1BreakLong or ZoneBoardPattern.S1BreakShort)
        {
            basePts += 2m;
        }

        return Math.Min(40m, basePts);
    }

    private static decimal BreadthAlignmentPoints(string action, string? mark, BoardCounts counts)
    {
        var bullish = MarketBreadthGate.IsBullishBreadth(counts);
        var sellingHeavy = counts.Selling > counts.Buying * 1.5m;
        var buyingHeavy = counts.Buying > counts.Selling * 1.5m;

        return action switch
        {
            "Buy" when bullish => 20m,
            "Buy" when buyingHeavy => 16m,
            "Buy" when mark?.Equals("BUYING", StringComparison.OrdinalIgnoreCase) == true => 14m,
            "Buy" => 8m,
            "Sell" when sellingHeavy => 20m,
            "Sell" when mark?.Equals("SELLING", StringComparison.OrdinalIgnoreCase) == true => 16m,
            "Sell" => 10m,
            _ => 10m,
        };
    }

    private static decimal SessionMomentumPoints(string action, string sessionMove)
    {
        var rising = sessionMove.StartsWith("Rising", StringComparison.OrdinalIgnoreCase);
        var falling = sessionMove.StartsWith("Falling", StringComparison.OrdinalIgnoreCase);
        return action switch
        {
            "Buy" when rising => 15m,
            "Buy" when !falling => 10m,
            "Sell" when falling => 15m,
            "Sell" when !rising => 10m,
            _ => 7m,
        };
    }

    private static decimal ZoneDistancePoints(string action, string patternId, decimal last, OpenVsZonesRow row)
    {
        var trigger = TriggerLevel(patternId, row);
        if (trigger <= 0 || last <= 0)
        {
            return 8m;
        }

        var dist = Math.Abs(last - trigger) / trigger;
        if (action is "Buy" or "Sell" && dist <= 0.0025m)
        {
            return 15m;
        }

        if (dist <= 0.006m)
        {
            return 12m;
        }

        if (dist <= 0.012m)
        {
            return 9m;
        }

        return dist > 0.025m ? 4m : 7m;
    }

    private static (string Timing, decimal DistPct) EntryTiming(string action, string patternId, OpenVsZonesRow row)
    {
        var trigger = TriggerLevel(patternId, row);
        if (trigger <= 0 || row.Last <= 0)
        {
            return ("Preparing", 0m);
        }

        var signed = (row.Last - trigger) / trigger;
        var distPct = Math.Abs(signed) * 100m;

        if (action == "Watch")
        {
            return distPct <= 0.35m ? ("Preparing", distPct) : ("Late entry", distPct);
        }

        var alignedLong = signed >= 0;
        var alignedShort = signed <= 0;
        var ready = action switch
        {
            "Buy" => alignedLong && distPct <= 0.55m,
            "Sell" => alignedShort && distPct <= 0.55m,
            _ => distPct <= 0.4m,
        };
        if (ready)
        {
            return ("Ready now", distPct);
        }

        if (distPct > 1.2m)
        {
            return ("Late entry", distPct);
        }

        return ("Preparing", distPct);
    }

    private static string ClassifyRisk(OpenVsZonesRow row, decimal triggerDistPct)
    {
        if (row.Last <= 0)
        {
            return "Normal";
        }

        var zoneWidthPct = row.B2 > row.S2
            ? (row.B2 - row.S2) / row.Last * 100m
            : 2m;
        var sessionPct = row.TodayOpen > 0
            ? Math.Abs(row.Last - row.TodayOpen) / row.TodayOpen * 100m
            : 0m;

        if (zoneWidthPct <= 1.8m && triggerDistPct <= 0.45m && sessionPct <= 2.5m)
        {
            return "Safe";
        }

        if (zoneWidthPct >= 4m || triggerDistPct >= 1.5m || sessionPct >= 5m)
        {
            return "Aggressive";
        }

        return "Normal";
    }

    private static decimal TriggerLevel(string patternId, OpenVsZonesRow row) =>
        patternId switch
        {
            ZoneBoardPattern.B1BreakLong => row.B1,
            ZoneBoardPattern.S1BreakShort => row.S1,
            ZoneBoardPattern.S2BounceLong => row.S2,
            ZoneRejectionPattern.B1RejectionToS2 => row.B1,
            _ => row.B1,
        };

    private static IReadOnlyList<string> BuildWhyBullets(
        OpenVsZonesRow row,
        string action,
        string patternId,
        BoardCounts counts,
        int score)
    {
        var list = new List<string>
        {
            $"Open band: {row.OpenBand}",
            $"LTP band: {row.LtpBand}",
            $"Board mark: {row.Mark}",
            $"Session: {row.SessionMove}",
            $"Board breadth: {counts.Buying} buying / {counts.Selling} selling",
            $"Setup score: {score}/100",
        };

        if (row.TightRange)
        {
            list.Add("Tight B1–S1 range — lower conviction until expansion");
        }

        if (action == "Buy" && row.Last >= row.B1)
        {
            list.Add("Price holds at/above B1 (long bias)");
        }

        if (action == "Sell" && row.Last <= row.S1)
        {
            list.Add("Price at/below S1 (short bias)");
        }

        list.Add($"Pattern: {patternId}");
        return list;
    }

    private static (int Intraday, int Daily, int Weekly, int Align) MultiTimeframe(string action, OpenVsZonesRow row)
    {
        int biasFromMark = row.Mark?.ToUpperInvariant() switch
        {
            "BUYING" => 1,
            "SELLING" => -1,
            _ => 0,
        };

        int daily = row.SessionMove.StartsWith("Rising", StringComparison.OrdinalIgnoreCase) ? 1
            : row.SessionMove.StartsWith("Falling", StringComparison.OrdinalIgnoreCase) ? -1
            : 0;

        if (row.LtpBandKey is "aboveB2" or "aboveB1")
        {
            daily = Math.Max(daily, 1);
        }
        else if (row.LtpBandKey is "belowS2" or "belowS1")
        {
            daily = Math.Min(daily, -1);
        }

        int want = action switch
        {
            "Buy" => 1,
            "Sell" => -1,
            _ => 0,
        };

        int intraday = want != 0 && biasFromMark == want ? 1
            : want != 0 && biasFromMark == -want ? -1
            : biasFromMark;

        int weekly = biasFromMark;

        int ScoreTf(int tf) => want == 0 ? (tf == 0 ? 1 : 0) : (tf == want ? 1 : 0);

        var align = ScoreTf(intraday) + ScoreTf(daily) + ScoreTf(weekly);
        return (intraday, daily, weekly, align);
    }

    private static string GradeFromScore(int score) => score switch
    {
        >= 92 => "A+",
        >= 80 => "A",
        >= 70 => "B",
        >= 60 => "C",
        _ => "D",
    };

    public static string MtfLabel(int value) => value switch
    {
        > 0 => "BUY",
        < 0 => "SELL",
        _ => "NEUTRAL",
    };
}
