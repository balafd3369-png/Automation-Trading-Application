using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class TradingSafetyIncidentEvaluator : IProductionSafetyIncidentEvaluator
{
    private readonly TradingExecutionSafetyOptions _options;
    private readonly LiveTradingExecutionSafetyService _safety;
    private readonly BrokerCircuitBreaker _breaker;
    private readonly TradeHistoryStore _history;
    private readonly SystemIncidentDeduplicationService _dedup;

    public TradingSafetyIncidentEvaluator(
        TradingExecutionSafetyOptions options,
        LiveTradingExecutionSafetyService safety,
        BrokerCircuitBreaker breaker,
        TradeHistoryStore history,
        SystemIncidentDeduplicationService dedup)
    {
        _options = options;
        _safety = safety;
        _breaker = breaker;
        _history = history;
        _dedup = dedup;
    }

    public IReadOnlyList<IncidentEvaluationCandidate> Evaluate()
    {
        var list = new List<IncidentEvaluationCandidate>();
        var gate = _safety.GateSnapshot();
        var breaker = _breaker.Snapshot();
        var now = DateTimeOffset.UtcNow;

        if (gate.IsHalted)
        {
            list.Add(Incident(
                SystemIncidentCategory.EmergencyGateActive,
                "Live emergency gate active",
                gate.Reason,
                SystemIncidentSeverity.High));
        }
        else
        {
            list.Add(Healthy(SystemIncidentCategory.EmergencyGateActive));
        }

        if (breaker.State.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(Incident(
                SystemIncidentCategory.CircuitBreakerOpen,
                "Broker circuit breaker open",
                breaker.TripReason ?? "Circuit open",
                SystemIncidentSeverity.High));
        }
        else
        {
            list.Add(Healthy(SystemIncidentCategory.CircuitBreakerOpen));
        }

        var stuck = _history.ListNonTerminalManualLiveOrders()
            .Concat(_history.ListNonTerminalLiveOrders().Select(MapLiveToManualShape))
            .Where(o => o.Status is "SUBMITTED" or "OPEN" or "PENDING")
            .Where(o => now - o.UpdatedAt > TimeSpan.FromSeconds(_options.StuckSubmittedWarningSeconds))
            .ToList();
        if (stuck.Count > 0)
        {
            list.Add(Incident(
                SystemIncidentCategory.StuckSubmittedOrder,
                "Submitted orders without terminal status",
                $"{stuck.Count} order(s) remain non-terminal beyond threshold.",
                SystemIncidentSeverity.Medium));
        }
        else
        {
            list.Add(Healthy(SystemIncidentCategory.StuckSubmittedOrder));
        }

        var unknown = _history.ListNonTerminalManualLiveOrders()
            .Where(o => o.Status is "UNKNOWN" or "TIMEOUT")
            .ToList();
        unknown.AddRange(_history.ListNonTerminalLiveOrders()
            .Where(o => o.Status is "UNKNOWN" or "TIMEOUT")
            .Select(MapLiveToManualShape));
        if (unknown.Count > 0)
        {
            list.Add(Incident(
                SystemIncidentCategory.OrderStateUnknown,
                "Unknown live order state",
                $"{unknown.Count} order(s) require investigation.",
                SystemIncidentSeverity.Critical));
        }

        return list;
    }

    private static ManualOrderSnapshot MapLiveToManualShape(LiveOrderSnapshot o) =>
        new(
            o.Id,
            "LIVE",
            "STRATEGY",
            o.Status,
            o.Symbol,
            "",
            "",
            o.TransactionType,
            "",
            o.Product,
            o.Exchange,
            o.Quantity,
            null,
            o.FilledQuantity,
            o.AveragePrice,
            o.BrokerOrderId,
            o.TradeId,
            o.BrokerMessage,
            o.UpdatedAt,
            o.UpdatedAt);

    private IncidentEvaluationCandidate Incident(
        SystemIncidentCategory category,
        string title,
        string description,
        SystemIncidentSeverity severity)
    {
        var fingerprint = _dedup.BuildFingerprint(SystemIncidentComponents.OrderSafety, category.ToString(), "trading");
        return new IncidentEvaluationCandidate(
            fingerprint,
            title,
            description,
            SystemIncidentComponents.OrderSafety,
            "trading",
            category,
            severity,
            description,
            1,
            false,
            null);
    }

    private IncidentEvaluationCandidate Healthy(SystemIncidentCategory category)
    {
        var fingerprint = _dedup.BuildFingerprint(SystemIncidentComponents.OrderSafety, category.ToString(), "trading");
        return new IncidentEvaluationCandidate(
            fingerprint,
            $"Healthy: {category}",
            "OK",
            SystemIncidentComponents.OrderSafety,
            "trading",
            category,
            SystemIncidentSeverity.Info,
            null,
            0,
            true,
            null);
    }
}