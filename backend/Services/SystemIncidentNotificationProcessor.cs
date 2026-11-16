using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentNotificationProcessor
{
    private readonly SystemIncidentNotificationStore _store;
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly SystemReliabilityOptions _reliability;
    private readonly SystemInstanceRegistryService _instances;
    private readonly IEnumerable<IIncidentNotificationProvider> _providers;
    private readonly ILogger<SystemIncidentNotificationProcessor> _log;

    public SystemIncidentNotificationProcessor(
        SystemIncidentNotificationStore store,
        SystemIncidentNotificationSettingsService settings,
        SystemReliabilityOptions reliability,
        SystemInstanceRegistryService instances,
        IEnumerable<IIncidentNotificationProvider> providers,
        ILogger<SystemIncidentNotificationProcessor> log)
    {
        _store = store;
        _settings = settings;
        _reliability = reliability;
        _instances = instances;
        _providers = providers;
        _log = log;
    }

    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var options = _settings.EffectiveOptions;
        var batch = _store.ClaimDueNotifications(
            options.MaxConcurrentDeliveries,
            now,
            _instances.InstanceId,
            _reliability.NotificationProcessingLeaseSeconds);
        var sent = 0;
        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            var provider = _providers.FirstOrDefault(p => p.Channel == item.Channel);
            if (provider is null)
            {
                _store.UpdateNotification(ClearClaim(item with
                {
                    DeliveryStatus = IncidentNotificationDeliveryStatus.Failed,
                    AttemptCount = item.AttemptCount + 1,
                    FailureReason = "No provider registered for channel.",
                    LastAttemptUtc = now,
                }));
                continue;
            }

            _log.LogInformation("Notification processing started {NotificationId}.", item.Id);
            NotificationProviderResult result;
            try
            {
                result = await provider.SendAsync(item, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Notification provider threw for {NotificationId}.", item.Id);
                result = new NotificationProviderResult(false, true, ex.Message, null);
            }

            if (result.Success)
            {
                _store.UpdateNotification(ClearClaim(item with
                {
                    DeliveryStatus = IncidentNotificationDeliveryStatus.Sent,
                    AttemptCount = item.AttemptCount + 1,
                    SentUtc = now,
                    LastAttemptUtc = now,
                    NextRetryUtc = null,
                    FailureReason = null,
                    ProviderMetadataJson = result.ProviderMetadataJson,
                }));
                _log.LogInformation("Notification successfully delivered {NotificationId}.", item.Id);
                sent++;
                continue;
            }

            var attempts = item.AttemptCount + 1;
            if (!result.TransientFailure || attempts >= options.MaximumDeliveryAttempts)
            {
                _store.UpdateNotification(ClearClaim(item with
                {
                    DeliveryStatus = IncidentNotificationDeliveryStatus.Failed,
                    AttemptCount = attempts,
                    LastAttemptUtc = now,
                    NextRetryUtc = null,
                    FailureReason = result.Error,
                    ProviderMetadataJson = result.ProviderMetadataJson,
                }));
                _log.LogWarning("Notification delivery failed permanently {NotificationId}.", item.Id);
                continue;
            }

            var delay = ComputeBackoff(options, attempts);
            _store.UpdateNotification(ClearClaim(item with
            {
                DeliveryStatus = IncidentNotificationDeliveryStatus.RetryScheduled,
                AttemptCount = attempts,
                LastAttemptUtc = now,
                NextRetryUtc = now.AddSeconds(delay),
                FailureReason = result.Error,
                ProviderMetadataJson = result.ProviderMetadataJson,
            }));
            _log.LogInformation(
                "Retry scheduled for notification {NotificationId} in {DelaySeconds}s.",
                item.Id,
                delay);
        }

        return sent;
    }

    public IncidentNotificationRecord RetryNow(string notificationId, DateTimeOffset now)
    {
        var current = _store.GetNotification(notificationId) ?? throw new InvalidOperationException("Notification not found.");
        if (current.DeliveryStatus is not (IncidentNotificationDeliveryStatus.Failed or IncidentNotificationDeliveryStatus.RetryScheduled))
        {
            throw new InvalidOperationException("Only failed or retry-scheduled notifications can be retried.");
        }

        return _store.UpdateNotification(current with
        {
            DeliveryStatus = IncidentNotificationDeliveryStatus.Pending,
            NextRetryUtc = now,
            LastAttemptUtc = now,
        });
    }

    private static int ComputeBackoff(IncidentNotificationOptions options, int attempts)
    {
        var delay = options.RetryBaseDelaySeconds * (int)Math.Pow(2, Math.Max(0, attempts - 1));
        return Math.Clamp(delay, options.RetryBaseDelaySeconds, options.RetryMaxDelaySeconds);
    }

    private static IncidentNotificationRecord ClearClaim(IncidentNotificationRecord record) =>
        record with
        {
            ProcessingOwner = null,
            ProcessingLeaseExpiresUtc = null,
            ClaimToken = null,
            ClaimFencingToken = null,
        };
}