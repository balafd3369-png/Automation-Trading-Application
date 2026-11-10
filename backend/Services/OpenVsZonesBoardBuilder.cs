using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class OpenVsZonesBoardBuilder
{
    public static OpenVsZonesBoardResponse Build(BoardResponse board)
    {
        var indices = new List<OpenVsZonesRow>();
        var stocks = new List<OpenVsZonesRow>();
        foreach (var symbol in board.Symbols)
        {
            if (!HasZones(symbol))
            {
                continue;
            }

            var row = ToRow(symbol);
            if (IndexUniverse.IsIndex(symbol.Name))
            {
                indices.Add(row);
            }
            else
            {
                stocks.Add(row);
            }
        }

        indices.Sort(CompareRows);
        stocks.Sort(CompareRows);
        return new OpenVsZonesBoardResponse(board.UpdatedAt, board.Feed, indices, stocks);
    }

    private static bool HasZones(SymbolSnapshot s) =>
        s.TodayOpen > 0 && s.B1 > 0 && s.B2 > 0 && s.S1 > 0 && s.S2 > 0;

    private static OpenVsZonesRow ToRow(SymbolSnapshot s)
    {
        var open = s.TodayOpen;
        var aboveB2 = open >= s.B2;
        var aboveB1 = open >= s.B1;
        var belowS1 = open <= s.S1;
        var belowS2 = open <= s.S2;
        var openBand = ClassifyOpenBand(open, s.B2, s.B1, s.S1, s.S2);
        var openKey = ClassifyOpenBandKey(open, s.B2, s.B1, s.S1, s.S2);
        var ltpBand = ClassifyOpenBand(s.Last, s.B2, s.B1, s.S1, s.S2);
        var ltpKey = ClassifyOpenBandKey(s.Last, s.B2, s.B1, s.S1, s.S2);
        return new OpenVsZonesRow(
            s.Name,
            IndexUniverse.IsIndex(s.Name),
            open,
            s.Last,
            s.B2,
            s.B1,
            s.S,
            s.S1,
            s.S2,
            s.Mark,
            s.TightRange,
            aboveB2,
            aboveB1,
            belowS1,
            belowS2,
            openBand,
            openKey,
            ltpBand,
            ltpKey,
            DescribeSessionMove(open, s.Last, openBand, ltpBand));
    }

    internal static string ClassifyOpenBand(decimal open, decimal b2, decimal b1, decimal s1, decimal s2)
    {
        return ClassifyOpenBandKey(open, b2, b1, s1, s2) switch
        {
            "aboveB2" => "Above B2",
            "aboveB1" => "Above B1",
            "betweenS1B1" => "Between S1–B1",
            "betweenS1S2" => "Between S1–S2",
            "belowS1" => "Below S1",
            "belowS2" => "Below S2",
            _ => "Unknown",
        };
    }

    /// <summary>Stable key for UI columns (open at 09:15 vs zone lines).</summary>
    internal static string ClassifyOpenBandKey(decimal open, decimal b2, decimal b1, decimal s1, decimal s2)
    {
        if (open <= 0 || b1 <= 0)
        {
            return "unknown";
        }

        if (b2 > 0 && open >= b2)
        {
            return "aboveB2";
        }

        if (open >= b1)
        {
            return "aboveB1";
        }

        if (s2 > 0 && open <= s2)
        {
            return "belowS2";
        }

        if (s1 > 0 && s2 > 0 && open > s2 && open <= s1)
        {
            return "betweenS1S2";
        }

        if (s1 > 0 && open <= s1)
        {
            return "belowS1";
        }

        return "betweenS1B1";
    }

    internal static string DescribeSessionMove(decimal open, decimal last, string openBand, string ltpBand)
    {
        if (open <= 0 || last <= 0)
        {
            return "—";
        }

        if (string.Equals(openBand, ltpBand, StringComparison.Ordinal))
        {
            return "Holding open band";
        }

        if (last > open)
        {
            return $"Rising · LTP in {ltpBand}";
        }

        if (last < open)
        {
            return $"Falling · LTP in {ltpBand}";
        }

        return $"Shifted · LTP in {ltpBand}";
    }

    private static int CompareRows(OpenVsZonesRow a, OpenVsZonesRow b) =>
        string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
