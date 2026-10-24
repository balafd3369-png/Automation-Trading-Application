using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Central guard: broker LTP/OHLC must pass session + deviation rules before mutating board/chart state.
/// </summary>
public static class BrokerQuoteGate
{
    private const decimal MaxIndexDeviation = 0.035m;
    private const decimal MaxStockDeviation = 0.12m;
    private const decimal MaxIntradayBarJump = 0.09m;

    public enum RejectReason
    {
        None,
        InvalidPrice,
        OffSession,
        NonTradingDay,
        DeviationFromAnchor,
    }

    public readonly record struct LastPriceDecision(bool Accept, RejectReason Reason, decimal Anchor);

    public static LastPriceDecision EvaluateLastPrice(
        string symbolName,
        decimal incomingLast,
        decimal storedLast,
        decimal todayOpen,
        decimal previousClose)
    {
        if (incomingLast <= 0)
        {
            return new(false, RejectReason.InvalidPrice, 0);
        }

        if (!NseCashSession.IsTradingDay(NseCashSession.IstToday()))
        {
            return new(false, RejectReason.NonTradingDay, ResolveAnchor(storedLast, todayOpen, previousClose));
        }

        if (!NseCashSession.IsLiveIntradayWindowNow())
        {
            return new(false, RejectReason.OffSession, ResolveAnchor(storedLast, todayOpen, previousClose));
        }

        var anchor = ResolveAnchor(storedLast, todayOpen, previousClose, incomingLast);
        if (anchor > 0)
        {
            var deviation = Math.Abs(incomingLast - anchor) / anchor;
            var max = IndexUniverse.IsIndex(symbolName) ? MaxIndexDeviation : MaxStockDeviation;
            if (deviation > max)
            {
                return new(false, RejectReason.DeviationFromAnchor, anchor);
            }
        }

        return new(true, RejectReason.None, anchor);
    }

    public static bool ShouldApplyTodayOpen(
        string symbolName,
        decimal incomingOpen,
        decimal storedLast,
        decimal todayOpen,
        decimal previousClose)
    {
        if (incomingOpen <= 0)
        {
            return false;
        }

        if (!NseCashSession.IsLiveIntradayWindowNow() || !NseCashSession.IsTradingDay(NseCashSession.IstToday()))
        {
            return false;
        }

        var anchor = ResolveAnchor(storedLast, todayOpen, previousClose, incomingOpen);
        if (anchor <= 0)
        {
            return true;
        }

        var deviation = Math.Abs(incomingOpen - anchor) / anchor;
        var max = IndexUniverse.IsIndex(symbolName) ? MaxIndexDeviation : MaxStockDeviation;
        return deviation <= max;
    }

    /// <summary>Drop corrupt historical bars (bad broker chunks) before they reach the chart API.</summary>
    public static List<ChartBar> FilterBrokerIntradayBars(IReadOnlyList<ChartBar> bars, string interval)
    {
        if (bars.Count == 0)
        {
            return [];
        }

        var ordered = bars.OrderBy(b => b.Time).ToList();
        var clean = new List<ChartBar>(ordered.Count);
        decimal? priorClose = null;
        foreach (var bar in ordered)
        {
            var clamped = ChartBarSanitizer.ClampWicks(bar, interval);
            if (!IsBarPlausible(clamped, priorClose))
            {
                continue;
            }

            clean.Add(clamped);
            priorClose = clamped.Close;
        }

        return clean;
    }

    private static bool IsBarPlausible(ChartBar bar, decimal? priorClose)
    {
        if (bar.Open <= 0 || bar.Close <= 0 || bar.High < bar.Low)
        {
            return false;
        }

        if (priorClose is null or <= 0)
        {
            return true;
        }

        var jump = Math.Abs(bar.Open - priorClose.Value) / priorClose.Value;
        var closeJump = Math.Abs(bar.Close - priorClose.Value) / priorClose.Value;
        return jump <= MaxIntradayBarJump && closeJump <= MaxIntradayBarJump;
    }

    private static decimal ResolveAnchor(
        decimal storedLast,
        decimal todayOpen,
        decimal previousClose,
        decimal incomingFallback = 0)
    {
        if (storedLast > 0)
        {
            return storedLast;
        }

        if (todayOpen > 0)
        {
            return todayOpen;
        }

        if (previousClose > 0)
        {
            return previousClose;
        }

        return incomingFallback;
    }
}
