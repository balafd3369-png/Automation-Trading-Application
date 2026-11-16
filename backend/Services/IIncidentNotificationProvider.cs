using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface IIncidentNotificationProvider
{
    IncidentNotificationChannel Channel { get; }

    Task<NotificationProviderResult> SendAsync(IncidentNotificationRecord notification, CancellationToken ct);
}

public sealed record NotificationProviderResult(
    bool Success,
    bool TransientFailure,
    string? Error,
    string? ProviderMetadataJson);