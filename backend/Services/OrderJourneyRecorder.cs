using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class OrderJourneyRecorder
{
    private readonly OrderJourneyStore _store;
    private readonly OperationsStore _ops;

    public OrderJourneyRecorder(OrderJourneyStore store, OperationsStore ops)
    {
        _store = store;
        _ops = ops;
    }

    public string Begin(
        string source,
        string executionMode,
        string? symbol,
        string? strategyId = null)
    {
        var correlationId = OrderCorrelation.NewId();
        _store.UpsertJourney(
            correlationId,
            source,
            executionMode,
            strategyId,
            symbol,
            orderId: null,
            currentStage: OrderJourneyStages.Submitted,
            lastReason: null);
        _store.AppendEvent(correlationId, OrderJourneyStages.Submitted, symbol, null);
        _ops.AppendAudit("ORDER", "Submitted", $"{correlationId} {executionMode} {symbol}");
        return correlationId;
    }

    public void Stage(
        string correlationId,
        string stage,
        string? detail = null,
        string? reason = null,
        string? orderId = null)
    {
        _store.UpdateStage(correlationId, stage, reason, orderId, symbol: detail);
        _store.AppendEvent(correlationId, stage, detail, reason);
        _ops.AppendAudit("ORDER", stage, $"{correlationId}: {reason ?? detail ?? stage}");
    }

    public void StageOnly(string correlationId, string stage, string? detail = null, string? reason = null) =>
        _store.AppendEvent(correlationId, stage, detail, reason);

    public void RecordStructuredRiskDecision(string correlationId, StructuredRiskDecision decision)
    {
        var json = decision.ToJson();
        _store.AppendEvent(correlationId, OrderJourneyStages.StructuredRiskDecision, decision.Strategy, json);
        _ops.AppendAudit("RISK", decision.RiskDecision, json);
    }

    public OrderJourneyLookup? Lookup(string correlationId)
    {
        var summary = _store.GetSummary(correlationId);
        if (summary is null)
        {
            return null;
        }

        return new OrderJourneyLookup(
            summary.CorrelationId,
            summary.CurrentStage,
            summary.LastReason,
            summary.UpdatedAt,
            summary.UserId,
            summary.Symbol,
            summary.Source,
            summary.ExecutionMode,
            summary.Timeline);
    }
}
