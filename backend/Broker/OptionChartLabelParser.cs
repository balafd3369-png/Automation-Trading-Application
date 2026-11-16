using System.Globalization;
using System.Text.RegularExpressions;
using TradeWithBeast.Services;

namespace TradeWithBeast.Broker;

/// <summary>Parses option chart watch names such as <c>NIFTY 17 NOV 25150 PE</c>.</summary>
public static partial class OptionChartLabelParser
{
    public sealed record Spec(string Underlying, DateTime Expiry, decimal Strike, string Side);

    public static bool TryParse(string chartName, out Spec spec)
    {
        spec = null!;
        if (string.IsNullOrWhiteSpace(chartName))
        {
            return false;
        }

        var m = ChartNameRegex().Match(chartName.Trim());
        if (!m.Success)
        {
            return false;
        }

        var underlying = m.Groups["u"].Value.ToUpperInvariant();
        if (!int.TryParse(m.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        var monText = m.Groups["mon"].Value;
        if (!DateTime.TryParseExact(monText, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthProbe))
        {
            return false;
        }

        var month = monthProbe.Month;
        if (!decimal.TryParse(m.Groups["k"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var strike))
        {
            return false;
        }

        var side = m.Groups["side"].Value.ToUpperInvariant();
        if (side is not ("CE" or "PE"))
        {
            return false;
        }

        var year = NseCashSession.IstToday().Year;
        var expiry = new DateTime(year, month, day);
        var today = NseCashSession.IstToday().ToDateTime(TimeOnly.MinValue);
        if (expiry < today.AddDays(-4))
        {
            expiry = expiry.AddYears(1);
        }

        spec = new Spec(underlying, expiry, strike, side);
        return true;
    }

    public static bool LooksLikeOptionChartName(string name) =>
        !string.IsNullOrWhiteSpace(name) && ChartNameRegex().IsMatch(name.Trim());

    [GeneratedRegex(
        @"^(?<u>[A-Z0-9&-]+)\s+(?<d>\d{1,2})\s+(?<mon>[A-Z]{3})\s+(?<k>[\d.]+)\s+(?<side>CE|PE)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChartNameRegex();
}