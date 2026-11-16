using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface IIncidentAlertDispatcher
{
    Task<IncidentAlertDispatchResult> DispatchAsync(SystemAlertRecord alert, CancellationToken ct);
}

public sealed record IncidentAlertDispatchResult(
    bool Recorded,
    SystemAlertStatus Status,
    string? Error);