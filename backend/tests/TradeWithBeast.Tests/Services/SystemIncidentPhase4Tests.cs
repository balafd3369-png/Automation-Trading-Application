using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Tests.Services;

public sealed class SystemIncidentPhase4Tests
{
    [Fact]
    public void Critical_incident_routes_email_and_telegram()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.Critical);
        var count = harness.Notifications.QueueLifecycleNotifications(
            incident,
            IncidentNotificationType.IncidentDetected,
            DateTimeOffset.UtcNow,
            false,
            null);
        Assert.Equal(2, count);
        var rows = harness.NotificationStore.ListNotifications(new IncidentNotificationListQuery(incident.Id));
        Assert.Contains(rows, r => r.Channel == IncidentNotificationChannel.Email);
        Assert.Contains(rows, r => r.Channel == IncidentNotificationChannel.Telegram);
    }

    [Fact]
    public void Duplicate_lifecycle_notification_is_suppressed_by_idempotency()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.High);
        var now = DateTimeOffset.UtcNow;
        var first = harness.Notifications.QueueLifecycleNotifications(incident, IncidentNotificationType.IncidentDetected, now, false, null);
        var second = harness.Notifications.QueueLifecycleNotifications(incident, IncidentNotificationType.IncidentDetected, now, false, null);
        Assert.Equal(2, first);
        Assert.Equal(0, second);
        var rows = harness.NotificationStore.ListNotifications(new IncidentNotificationListQuery(incident.Id));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Acknowledgement_cancels_unacknowledged_escalation_jobs()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.Critical);
        harness.Escalation.ScheduleForIncident(incident, DateTimeOffset.UtcNow);
        var pending = harness.NotificationStore.ListDueEscalationJobs(DateTimeOffset.UtcNow.AddHours(1));
        Assert.NotEmpty(pending);
        harness.Incidents.Acknowledge(incident.Id, "admin");
        var after = harness.NotificationStore.ListDueEscalationJobs(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Empty(after);
    }

    [Fact]
    public void Resolve_cancels_all_escalation_jobs()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.High);
        harness.Escalation.ScheduleForIncident(incident, DateTimeOffset.UtcNow);
        harness.Incidents.Resolve(incident.Id, "admin");
        var after = harness.NotificationStore.ListDueEscalationJobs(DateTimeOffset.UtcNow.AddHours(1));
        Assert.Empty(after);
    }

    [Fact]
    public async Task Processor_delivers_with_mock_provider_and_retries_transient_failure()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.High);
        _ = harness.Notifications.QueueLifecycleNotifications(
            incident,
            IncidentNotificationType.IncidentDetected,
            DateTimeOffset.UtcNow,
            false,
            null);
        var batch = harness.NotificationStore.ListNotifications(new IncidentNotificationListQuery(incident.Id));
        var email = batch.First(r => r.Channel == IncidentNotificationChannel.Email);
        harness.EmailProvider.FailTransientOnce = true;
        var now = DateTimeOffset.UtcNow;
        _ = await harness.Processor.ProcessDueAsync(now, CancellationToken.None);
        var retried = harness.NotificationStore.GetNotification(email.Id)!;
        Assert.Equal(IncidentNotificationDeliveryStatus.RetryScheduled, retried.DeliveryStatus);
        Assert.NotNull(retried.NextRetryUtc);
        _ = await harness.Processor.ProcessDueAsync(retried.NextRetryUtc!.Value, CancellationToken.None);
        var sent = harness.NotificationStore.GetNotification(email.Id)!;
        Assert.Equal(IncidentNotificationDeliveryStatus.Sent, sent.DeliveryStatus);
        Assert.Single(harness.EmailProvider.Sent);
    }

    [Fact]
    public void Severity_increase_uses_distinct_idempotency_key()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var incident = harness.CreateIncident(SystemIncidentSeverity.Medium);
        var now = DateTimeOffset.UtcNow;
        var first = harness.Notifications.QueueLifecycleNotifications(incident, IncidentNotificationType.IncidentDetected, now, false, null);
        var increased = incident with { Severity = SystemIncidentSeverity.Critical };
        var second = harness.Notifications.QueueLifecycleNotifications(increased, IncidentNotificationType.SeverityIncreased, now, false, "severity up");
        Assert.True(first > 0);
        Assert.True(second > 0);
    }

    [Fact]
    public void Reopened_incident_after_resolution_queues_reopened_type()
    {
        var harness = CreateHarness();
        harness.ConfigureRecipients();
        var candidate = SampleCandidate("workers", "PaperTradingAgent", SystemIncidentCategory.WorkerStalled, SystemIncidentSeverity.High);
        var incident = harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow);
        harness.Incidents.Resolve(incident.Id, "admin");
        var reopened = harness.Store.CreateOrUpdateOccurrence(candidate, DateTimeOffset.UtcNow);
        harness.Notifications.HandleIncidentUpsert(reopened, null, null);
        var rows = harness.NotificationStore.ListNotifications(new IncidentNotificationListQuery(reopened.Id));
        Assert.Contains(rows, r => r.NotificationType == IncidentNotificationType.Reopened);
    }

    private static Harness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-notify-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var monitoring = new SystemMonitoringOptions();
        monitoring.ClampIntervals();
        var store = new SystemIncidentStore(configuration, database, NullLogger<SystemIncidentStore>.Instance);
        var lifecycle = new SystemIncidentLifecycleService();
        var dedup = new SystemIncidentDeduplicationService();
        var evaluator = new SystemIncidentEvaluator(monitoring, dedup);
        var dispatcher = new DatabaseIncidentAlertDispatcher(store, NullLogger<DatabaseIncidentAlertDispatcher>.Instance);
        var alertService = new SystemIncidentAlertService(monitoring, store, dispatcher);
        var notificationOptions = new IncidentNotificationOptions
        {
            NotificationsEnabled = true,
            EmailEnabled = true,
            TelegramEnabled = true,
            TelegramOnMediumSeverity = true,
            NotificationThrottleSeconds = 0,
        };
        notificationOptions.Clamp();
        var notificationStore = new SystemIncidentNotificationStore(configuration, database, NullLogger<SystemIncidentNotificationStore>.Instance);
        var notificationSettings = new SystemIncidentNotificationSettingsService(notificationOptions, notificationStore);
        var router = new SystemIncidentNotificationRouter();
        var content = new IncidentNotificationContentBuilder();
        var escalation = new SystemIncidentEscalationService(notificationStore, notificationSettings, lifecycle, NullLogger<SystemIncidentEscalationService>.Instance);
        var notifications = new SystemIncidentNotificationService(
            notificationStore,
            store,
            notificationSettings,
            router,
            content,
            escalation,
            NullLogger<SystemIncidentNotificationService>.Instance);
        var reliability = new SystemReliabilityOptions();
        reliability.Clamp();
        var heartbeats = new WorkerHeartbeatRegistry(monitoring);
        var coordination = new SystemCoordinationStore(configuration, database, reliability, NullLogger<SystemCoordinationStore>.Instance);
        var instances = new SystemInstanceRegistryService(coordination, reliability, heartbeats, NullLogger<SystemInstanceRegistryService>.Instance);
        var emailProvider = new RecordingNotificationProvider(IncidentNotificationChannel.Email);
        var telegramProvider = new RecordingNotificationProvider(IncidentNotificationChannel.Telegram);
        var processor = new SystemIncidentNotificationProcessor(
            notificationStore,
            notificationSettings,
            reliability,
            instances,
            [emailProvider, telegramProvider],
            NullLogger<SystemIncidentNotificationProcessor>.Instance);
        var service = new SystemIncidentService(
            monitoring,
            store,
            lifecycle,
            evaluator,
            alertService,
            notifications,
            new NoOpProductionSafetyIncidentEvaluator(),
            NullLogger<SystemIncidentService>.Instance);
        return new Harness(store, service, notificationStore, notifications, escalation, processor, emailProvider, notificationOptions, notificationSettings);
    }

    private static IncidentEvaluationCandidate SampleCandidate(
        string component,
        string identifier,
        SystemIncidentCategory category,
        SystemIncidentSeverity severity = SystemIncidentSeverity.High)
    {
        var dedup = new SystemIncidentDeduplicationService();
        var fingerprint = dedup.BuildFingerprint(component, category.ToString(), identifier);
        return new IncidentEvaluationCandidate(
            fingerprint,
            $"{category} on {identifier}",
            "Test incident",
            component,
            identifier,
            category,
            severity,
            "test error",
            1,
            false,
            "{}");
    }

    private sealed class Harness
    {
        public Harness(
            SystemIncidentStore store,
            SystemIncidentService incidents,
            SystemIncidentNotificationStore notificationStore,
            SystemIncidentNotificationService notifications,
            SystemIncidentEscalationService escalation,
            SystemIncidentNotificationProcessor processor,
            RecordingNotificationProvider emailProvider,
            IncidentNotificationOptions notificationOptions,
            SystemIncidentNotificationSettingsService notificationSettings)
        {
            Store = store;
            Incidents = incidents;
            NotificationStore = notificationStore;
            Notifications = notifications;
            Escalation = escalation;
            Processor = processor;
            EmailProvider = emailProvider;
            NotificationOptions = notificationOptions;
            NotificationSettings = notificationSettings;
        }

        public SystemIncidentStore Store { get; }
        public SystemIncidentService Incidents { get; }
        public SystemIncidentNotificationStore NotificationStore { get; }
        public SystemIncidentNotificationService Notifications { get; }
        public SystemIncidentEscalationService Escalation { get; }
        public SystemIncidentNotificationProcessor Processor { get; }
        public RecordingNotificationProvider EmailProvider { get; }
        public IncidentNotificationOptions NotificationOptions { get; }
        public SystemIncidentNotificationSettingsService NotificationSettings { get; }

        public void ConfigureRecipients()
        {
            NotificationOptions.PrimaryEmailRecipients = ["ops@example.com"];
            NotificationOptions.PrimaryTelegramChatIds = [1001];
            NotificationSettings.ApplySettingsUpdate(new NotificationSettingsUpdateRequest
            {
                PrimaryEmailRecipients = ["ops@example.com"],
                PrimaryTelegramChatIds = [1001],
            });
        }

        public SystemIncidentRecord CreateIncident(SystemIncidentSeverity severity) =>
            Store.CreateOrUpdateOccurrence(
                SampleCandidate("database", "primary", SystemIncidentCategory.DatabaseUnavailable, severity),
                DateTimeOffset.UtcNow);
    }

    private sealed class RecordingNotificationProvider : IIncidentNotificationProvider
    {
        public RecordingNotificationProvider(IncidentNotificationChannel channel) => Channel = channel;

        public IncidentNotificationChannel Channel { get; }
        public List<IncidentNotificationRecord> Sent { get; } = [];
        public bool FailTransientOnce { get; set; }

        public Task<NotificationProviderResult> SendAsync(IncidentNotificationRecord notification, CancellationToken ct)
        {
            if (FailTransientOnce)
            {
                FailTransientOnce = false;
                return Task.FromResult(new NotificationProviderResult(false, true, "transient", null));
            }

            Sent.Add(notification);
            return Task.FromResult(new NotificationProviderResult(true, false, null, "{}"));
        }
    }
}