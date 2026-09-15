namespace TradeWithBeast.Formulas;

public static class BlackScholes
{
    public static decimal Premium(decimal spot, decimal strike, bool isCall, DateTimeOffset expiryIst, string underlying)
    {
        var years = YearsToExpiry(expiryIst);
        var iv = ImpliedVol(underlying);
        var value = Price((double)spot, (double)strike, years, iv, 0.065, isCall);
        var tick = 0.05m;
        var rounded = Math.Round((decimal)value / tick, MidpointRounding.AwayFromZero) * tick;
        return Math.Max(tick, rounded);
    }

    public static double ImpliedVol(string underlying) =>
        underlying.ToUpperInvariant() switch
        {
            "BANKNIFTY" => 0.18,
            "FINNIFTY" => 0.16,
            _ => 0.13,
        };

    private static double YearsToExpiry(DateTimeOffset expiryIst)
    {
        var years = (expiryIst - DateTimeOffset.UtcNow).TotalDays / 365.0;
        return Math.Max(1.0 / 365.0 / 8.0, years);
    }

    private static double Price(double spot, double strike, double years, double iv, double rate, bool isCall)
    {
        if (spot <= 0 || strike <= 0)
        {
            return 0;
        }

        if (years < 1.0 / 365.0 / 24.0)
        {
            return Math.Max(0, isCall ? spot - strike : strike - spot);
        }

        var sqrtT = Math.Sqrt(years);
        var d1 = (Math.Log(spot / strike) + (rate + iv * iv / 2.0) * years) / (iv * sqrtT);
        var d2 = d1 - iv * sqrtT;
        if (isCall)
        {
            return spot * Cdf(d1) - strike * Math.Exp(-rate * years) * Cdf(d2);
        }

        return strike * Math.Exp(-rate * years) * Cdf(-d2) - spot * Cdf(-d1);
    }

    private static double Cdf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;
        var sign = x < 0 ? -1.0 : 1.0;
        var t = 1.0 / (1.0 + p * Math.Abs(x));
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x / 2.0);
        return 0.5 * (1.0 + sign * y);
    }
}
