using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

internal sealed class NoOpProductionSafetyIncidentEvaluator : IProductionSafetyIncidentEvaluator
{
    public IReadOnlyList<IncidentEvaluationCandidate> Evaluate() => [];
}