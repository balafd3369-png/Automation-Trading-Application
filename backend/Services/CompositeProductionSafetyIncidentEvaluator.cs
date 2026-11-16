using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class CompositeProductionSafetyIncidentEvaluator : IProductionSafetyIncidentEvaluator
{
    private readonly ProductionSafetyIncidentEvaluator _production;
    private readonly TradingSafetyIncidentEvaluator _trading;

    public CompositeProductionSafetyIncidentEvaluator(
        ProductionSafetyIncidentEvaluator production,
        TradingSafetyIncidentEvaluator trading)
    {
        _production = production;
        _trading = trading;
    }

    public IReadOnlyList<IncidentEvaluationCandidate> Evaluate()
    {
        var list = new List<IncidentEvaluationCandidate>();
        list.AddRange(_production.Evaluate());
        list.AddRange(_trading.Evaluate());
        return list;
    }
}