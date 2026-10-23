using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record PortfolioExposure(
    string Underlying,
    string Direction,
    decimal MaxRisk,
    string Strategy);

public sealed record RiskAssessment(
    bool Accepted,
    int QuantityMultiplier,
    decimal MaxRisk,
    decimal MaxReward,
    decimal CapitalRequired,
    IReadOnlyList<string> Rejections);

public sealed class PortfolioRiskManager
{
    private static readonly IReadOnlyDictionary<string, int> Lots =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["NIFTY"] = 65,
            ["BANKNIFTY"] = 30,
            ["FINNIFTY"] = 60,
            ["SENSEX"] = 20,
        };

    public static int LotSize(string underlying) => Lots.TryGetValue(underlying, out var lot) ? lot : 1;

    public RiskAssessment Assess(
        StrategyCandidate candidate,
        PaperAgentConfig config,
        decimal realizedPnl,
        decimal aggregateOpenRisk,
        int openPositions,
        IReadOnlyList<PortfolioExposure> exposures,
        DateTimeOffset now,
        bool paperTrading = false,
        decimal? brokerAvailableFunds = null)
    {
        var rejections = new List<string>();
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(now, ist);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            || local.TimeOfDay < new TimeSpan(9, 20, 0)
            || local.TimeOfDay >= new TimeSpan(15, 10, 0))
        {
            rejections.Add("outside entry session 09:20-15:10 IST");
        }
        if (openPositions >= config.MaxOpenPositions) rejections.Add("maximum open positions reached");
        if (!paperTrading && realizedPnl <= -config.MaxDailyLoss) rejections.Add("daily loss limit reached");
        if (candidate.Legs.Any(x => x.Quote is null || !x.Quote.IsFresh)) rejections.Add("stale or unavailable quote");
        if (candidate.Legs.Any(x => x.InstrumentType == "OPTION"
            && (x.Quote?.Bid is null || x.Quote.Ask is null)))
        {
            rejections.Add("option bid/ask missing");
        }
        if (candidate.Legs.Any(x => x.InstrumentType == "OPTION"
            && x.Quote?.Bid is decimal bid && x.Quote.Ask is decimal ask
            && ask > 0 && (ask - bid) / ask > 0.08m))
        {
            rejections.Add("option spread exceeds 8%");
        }
        if (candidate.Legs.Any(x => x.InstrumentType == "OPTION" && x.Quote?.Volume is null or <= 0))
        {
            rejections.Add("option volume missing or zero");
        }
        if (candidate.Legs.Any(x => x.InstrumentType == "OPTION" && x.Quote?.OpenInterest is null or <= 0))
        {
            rejections.Add("option open interest missing or zero");
        }
        if (candidate.MaxRiskPerUnit <= 0 || candidate.MaxRewardPerUnit <= 0)
        {
            rejections.Add("invalid payoff");
        }
        var rr = candidate.MaxRewardPerUnit / Math.Max(0.01m, candidate.MaxRiskPerUnit);
        if (rr < 1.0m) rejections.Add("reward/risk below 1.0");
        if (candidate.Strategy.Contains("CREDIT", StringComparison.OrdinalIgnoreCase)
            && !candidate.Strategy.Contains("NAKED", StringComparison.OrdinalIgnoreCase)
            && !candidate.Legs.Any(x => x.Side == "BUY"))
        {
            rejections.Add("naked short options prohibited");
        }
        if (candidate.Strategy.Contains("NAKED", StringComparison.OrdinalIgnoreCase))
        {
            if (candidate.Legs.Count != 1)
            {
                rejections.Add("naked strategies must contain exactly one leg");
            }
            else
            {
                var leg = candidate.Legs[0];
                var allowed = candidate.Strategy switch
                {
                    "NAKED_INDEX_OPTION_SHORT" => IsIndex(candidate.Underlying)
                        && leg.InstrumentType == "OPTION"
                        && leg.Side == "SELL",
                    "NAKED_STOCK_OPTION_SHORT" => !IsIndex(candidate.Underlying)
                        && leg.InstrumentType == "OPTION"
                        && leg.Side == "SELL",
                    "NAKED_INDEX_FUTURE" => IsIndex(candidate.Underlying)
                        && leg.InstrumentType == "FUTURE",
                    "NAKED_STOCK_FUTURE" => !IsIndex(candidate.Underlying)
                        && leg.InstrumentType == "FUTURE",
                    _ => false,
                };
                if (!allowed)
                {
                    rejections.Add("unsupported naked strategy shape");
                }
            }
        }
        if (candidate.Strategy == ZoneRejectionPattern.B1RejectionToS2)
        {
            if (!IsIndex(candidate.Underlying))
            {
                rejections.Add("B1 rejection runs on indices only (NIFTY, BANKNIFTY, FINNIFTY, SENSEX)");
            }
            else if (!candidate.Legs.All(x => x.InstrumentType == "OPTION" && x.Side == "BUY"))
            {
                rejections.Add("B1 rejection on index requires option buy legs (PE spread)");
            }
        }
        if (candidate.Strategy is TenAmIntradayPattern.HighFadeStrategy or TenAmIntradayPattern.LowBreakStrategy)
        {
            if (candidate.Legs.Count != 1)
            {
                rejections.Add("10 AM intraday strategies require a single leg");
            }
            else
            {
                var leg = candidate.Legs[0];
                var ok = leg.InstrumentType switch
                {
                    "FUTURE" => IsIndex(candidate.Underlying),
                    "STOCK" => !IsIndex(candidate.Underlying),
                    "OPTION" => leg.Side == "BUY",
                    _ => false,
                };
                if (!ok)
                {
                    rejections.Add("10 AM strategy leg type does not match underlying");
                }
            }
        }
        var isHedgedSpread = candidate.Strategy.Contains("SPREAD", StringComparison.OrdinalIgnoreCase);
        if (isHedgedSpread)
        {
            if (!IsIndex(candidate.Underlying)
                || candidate.Legs.Any(leg => leg.InstrumentType != "OPTION")
                || !candidate.Legs.Any(leg => leg.Side == "BUY")
                || !candidate.Legs.Any(leg => leg.Side == "SELL"))
            {
                rejections.Add("option hedging is restricted to protected index spreads");
            }
            if (config.AllocatedCapital < 100_000m)
            {
                rejections.Add("index option hedging requires at least ₹1,00,000 allocated capital");
            }
            if (exposures.Any(exposure =>
                    exposure.Strategy.Contains("SPREAD", StringComparison.OrdinalIgnoreCase)))
            {
                rejections.Add("only one index option hedge may be open at a time");
            }
        }
        if (candidate.Legs.Any(x => x.InstrumentType == "OPTION"
            && exposures.Any(e => e.Direction == candidate.Direction
                && e.Underlying.Equals(candidate.Underlying, StringComparison.OrdinalIgnoreCase))))
        {
            rejections.Add("same underlying and direction already open");
        }

        if (candidate.Strategy is ZoneBoardPattern.B1BreakLong
            or ZoneBoardPattern.S1BreakShort
            or ZoneBoardPattern.S2BounceLong)
        {
            if (!candidate.Legs.All(x => x.InstrumentType == "OPTION" && x.Side == "BUY")
                && !candidate.Legs.All(x => x.InstrumentType is "STOCK" or "INDEX"))
            {
                rejections.Add("zone board index setup must be all option buys or single cash leg");
            }
        }

        var lot = candidate.Legs.Any(x => x.InstrumentType is "OPTION" or "FUTURE") && IsIndex(candidate.Underlying)
            ? LotSize(candidate.Underlying)
            : 1;
        decimal riskBudget;
        decimal capitalBudget;
        if (paperTrading)
        {
            // Paper auto-trading: size each setup independently; do not deplete a shared ₹1L pool across positions.
            riskBudget = config.MaxDailyLoss * 0.35m;
            var brokerPool = brokerAvailableFunds is > 0 ? brokerAvailableFunds.Value : 0m;
            capitalBudget = Math.Max(config.AllocatedCapital, brokerPool);
        }
        else
        {
            riskBudget = Math.Min(config.MaxDailyLoss * 0.35m, config.MaxDailyLoss + realizedPnl - aggregateOpenRisk);
            capitalBudget = config.AllocatedCapital - aggregateOpenRisk;
        }
        var riskPerLot = candidate.MaxRiskPerUnit * lot;
        var debitPerUnit = Math.Max(0, candidate.Legs.Sum(x => (x.Side == "BUY" ? 1m : -1m) * x.Price * x.Ratio));
        var capitalPerLot = Math.Max(riskPerLot, debitPerUnit * lot);
        var byRisk = riskPerLot > 0 ? (int)Math.Floor(riskBudget / riskPerLot) : 0;
        var byCapital = capitalPerLot > 0 ? (int)Math.Floor(capitalBudget / capitalPerLot) : 0;
        var multiplier = Math.Min(byRisk, byCapital);
        if (multiplier <= 0) rejections.Add("insufficient risk or capital budget");
        return new(
            rejections.Count == 0,
            Math.Max(0, multiplier) * lot,
            Math.Max(0, multiplier) * riskPerLot,
            Math.Max(0, multiplier) * candidate.MaxRewardPerUnit * lot,
            Math.Max(0, multiplier) * capitalPerLot,
            rejections);
    }

    public static (decimal MaxRisk, decimal MaxReward) VerticalPayoff(
        decimal width,
        decimal netPremium,
        bool debit)
    {
        if (width <= 0 || netPremium <= 0 || netPremium >= width) return (0, 0);
        return debit ? (netPremium, width - netPremium) : (width - netPremium, netPremium);
    }

    private static bool IsIndex(string value) =>
        value is "NIFTY" or "BANKNIFTY" or "FINNIFTY" or "SENSEX";
}
