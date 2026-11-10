using System.Text.Json;

namespace TradeWithBeast.Models;

public sealed record StructuredRiskDecision(
    string CorrelationId,
    string Strategy,
    string DailyLoss,
    string PositionCount,
    string CapitalCheck,
    string RiskDecision,
    string Reason)
{
    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
