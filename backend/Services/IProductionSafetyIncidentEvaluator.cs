using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface IProductionSafetyIncidentEvaluator
{
    IReadOnlyList<IncidentEvaluationCandidate> Evaluate();
}