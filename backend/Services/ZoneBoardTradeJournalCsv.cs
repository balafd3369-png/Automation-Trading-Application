using System.Globalization;
using System.Text;

namespace TradeWithBeast.Services;

public static class ZoneBoardTradeJournalCsv
{
    private static readonly string[] Headers =
    [
        "Symbol",
        "SessionDate",
        "Pattern",
        "Direction",
        "EntryTimeIst",
        "EntryPrice",
        "StopLoss",
        "Target",
        "Zone_B2",
        "Zone_B1",
        "Zone_S",
        "Zone_S1",
        "Zone_S2",
        "ExitTimeIst",
        "ExitPrice",
        "ExitReason",
        "PointsPnl",
        "Result",
        "EntryRule",
        "SpotSignalInterval",
        "OptionPremiumInterval",
        "OptionBuy",
        "WeeklyExpiry",
        "LotSize",
        "AtmStrike",
        "ItmStrike",
        "ItmEntryPremium",
        "ItmExitPremium",
        "ItmOptionPnlPoints",
        "ItmOptionPnlRupee",
        "ItmOptionResult",
        "ItmPremiumSource",
        "OtmStrike",
        "OtmEntryPremium",
        "OtmExitPremium",
        "OtmOptionPnlPoints",
        "OtmOptionPnlRupee",
        "OtmOptionResult",
        "OtmPremiumSource",
        "OptionNotes",
    ];

    public static async Task<string> ToCsvAsync(
        IReadOnlyList<ZoneBoardPattern.ZoneBoardTradeJournalEntry> trades,
        ZoneBoardBrokerOptionPremiumService premiums,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers));
        foreach (var t in trades)
        {
            var opt = await premiums.EstimateAsync(t, ct);
            sb.AppendLine(FormatRow(t, opt));
        }

        return sb.ToString();
    }

    public static string ToCsv(
        IReadOnlyList<ZoneBoardPattern.ZoneBoardTradeJournalEntry> trades,
        IReadOnlyList<ZoneBoardOptionEstimator.TradeOptionEstimate> estimates)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers));
        for (var i = 0; i < trades.Count; i++)
        {
            var opt = i < estimates.Count ? estimates[i] : ZoneBoardOptionEstimator.Estimate(trades[i]);
            sb.AppendLine(FormatRow(trades[i], opt));
        }

        return sb.ToString();
    }

    private static string FormatRow(
        ZoneBoardPattern.ZoneBoardTradeJournalEntry t,
        ZoneBoardOptionEstimator.TradeOptionEstimate opt)
    {
        return string.Join(
            ",",
            Csv(t.Symbol),
            Csv(t.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Csv(t.Pattern),
            Csv(t.Direction),
            Csv(t.EntryTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(t.Entry),
            Csv(t.StopLoss),
            Csv(t.Target),
            Csv(t.B2),
            Csv(t.B1),
            Csv(t.S),
            Csv(t.S1),
            Csv(t.S2),
            Csv(t.ExitTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Csv(t.ExitPrice),
            Csv(t.ExitReason),
            Csv(t.PointsPnl),
            Csv(t.Result),
            Csv(t.EntryRule),
            Csv("5minute"),
            Csv("15minute"),
            Csv(opt.OptionRight),
            Csv(opt.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Csv(opt.LotSize),
            Csv(opt.AtmStrike),
            Csv(opt.Itm.Strike),
            Csv(opt.Itm.EntryPremiumEst),
            Csv(opt.Itm.ExitPremiumEst),
            Csv(opt.Itm.PremiumPnlPoints),
            Csv(opt.Itm.RupeePnl),
            Csv(opt.Itm.Result),
            Csv(opt.Itm.PremiumSource),
            Csv(opt.Otm.Strike),
            Csv(opt.Otm.EntryPremiumEst),
            Csv(opt.Otm.ExitPremiumEst),
            Csv(opt.Otm.PremiumPnlPoints),
            Csv(opt.Otm.RupeePnl),
            Csv(opt.Otm.Result),
            Csv(opt.Otm.PremiumSource),
            Csv(opt.Notes));
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string Csv(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Csv(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
