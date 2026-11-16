using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;
using TradeWithBeast.Services.Coordination;

namespace TradeWithBeast.Tests.Services;

public sealed class SystemReliabilityPhase6Tests
{
    [Fact]
    public void Distributed_lock_prevents_concurrent_owners()
    {
        var harness = CreateHarness();
        var now = DateTimeOffset.UtcNow;
        var a = harness.Store.TryAcquireLock("test-lock", "instance-a", Guid.NewGuid().ToString("N"), now, now.AddSeconds(30));
        var b = harness.Store.TryAcquireLock("test-lock", "instance-b", Guid.NewGuid().ToString("N"), now, now.AddSeconds(30));
        Assert.True(a.Acquired);
        Assert.False(b.Acquired);
        Assert.Equal("instance-a", b.ConflictOwner);
    }

    [Fact]
    public void Expired_lock_can_be_acquired_by_new_owner()
    {
        var harness = CreateHarness();
        var now = DateTimeOffset.UtcNow;
        _ = harness.Store.TryAcquireLock("expiring", "instance-a", "token-a", now.AddMinutes(-2), now.AddMinutes(-1));
        var next = harness.Store.TryAcquireLock("expiring", "instance-b", "token-b", now, now.AddSeconds(30));
        Assert.True(next.Acquired);
        Assert.Equal("instance-b", harness.Store.ListLocks().First().OwnerInstanceId);
    }

    [Fact]
    public void Trading_worker_automatic_recovery_is_denied()
    {
        var harness = CreateHarness();
        var worker = new SystemWorkerRegistryRecord(
            $"{harness.Instances.InstanceId}:{MonitoredWorkerNames.PaperTradingAgent}",
            MonitoredWorkerNames.PaperTradingAgent,
            harness.Instances.InstanceId,
            SystemReliabilityWorkerTypes.Trading,
            SystemWorkerRegistryStatus.Unresponsive,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            0,
            null,
            "stale",
            true,
            false);
        harness.Store.UpsertWorker(worker);
        var history = harness.Recovery.TryAutomaticRecovery(MonitoredWorkerNames.PaperTradingAgent, "corr-1");
        Assert.NotNull(history);
        Assert.Equal(SystemRecoveryResult.Denied, history!.Result);
    }

    [Fact]
    public void Startup_recovery_releases_stuck_processing_notifications()
    {
        var harness = CreateHarness();
        var incident = harness.Incidents.CreateOrUpdateOccurrence(
            new IncidentEvaluationCandidate(
                "r|DatabaseUnavailable|db",
                "db",
                "db",
                "database",
                "primary",
                SystemIncidentCategory.DatabaseUnavailable,
                SystemIncidentSeverity.High,
                null,
                1,
                false,
                "{}"),
            DateTimeOffset.UtcNow);
        harness.NotificationStore.TryInsertNotification(new IncidentNotificationRecord(
            Guid.NewGuid().ToString("N"),
            incident.Id,
            "k1",
            IncidentNotificationChannel.Email,
            "ops@example.com",
            IncidentNotificationType.IncidentDetected,
            "s",
            "h",
            "t",
            IncidentNotificationDeliveryStatus.Processing,
            1,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            "stale-owner",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "token",
            1));
        var released = harness.Recovery.RunStartupRecovery();
        Assert.Equal(1, released);
    }

  [Fact]
    public async Task Concurrent_notification_claims_do_not_duplicate()
    {
        var harness = CreateHarness();
        var incident = harness.Incidents.CreateOrUpdateOccurrence(
            new IncidentEvaluationCandidate(
                "r|BrokerDisconnected|m",
                "t",
                "d",
                "broker",
                "m",
                SystemIncidentCategory.BrokerDisconnected,
                SystemIncidentSeverity.High,
                null,
                1,
                false,
                "{}"),
            DateTimeOffset.UtcNow);
        harness.NotificationStore.TryInsertNotification(new IncidentNotificationRecord(
            Guid.NewGuid().ToString("N"),
            incident.Id,
            "idem",
            IncidentNotificationChannel.Email,
            "ops@example.com",
            IncidentNotificationType.IncidentDetected,
            "s",
            "h",
            "t",
            IncidentNotificationDeliveryStatus.Pending,
            0,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null));
        var now = DateTimeOffset.UtcNow;
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => harness.NotificationStore.ClaimDueNotifications(1, now, $"inst-{_}", 60)))
            .ToArray();
        await Task.WhenAll(tasks);
        var totalClaimed = tasks.SelectMany(t => t.Result).Select(n => n.Id).Distinct().Count();
        Assert.Equal(1, totalClaimed);
    }

    private static Harness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-rel-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var monitoring = new SystemMonitoringOptions();
        var reliability = new SystemReliabilityOptions { DistributedLocksEnabled = true };
        reliability.Clamp();
        var heartbeats = new WorkerHeartbeatRegistry(monitoring);
        var store = new SystemCoordinationStore(configuration, database, reliability, NullLogger<SystemCoordinationStore>.Instance);
        var instances = new SystemInstanceRegistryService(store, reliability, heartbeats, NullLogger<SystemInstanceRegistryService>.Instance);
        var policies = new SystemRecoveryPolicyService(store, reliability);
        var recovery = new SystemRecoveryService(store, reliability, policies, instances, NullLogger<SystemRecoveryService>.Instance);
        var incidentStore = new SystemIncidentStore(configuration, database, NullLogger<SystemIncidentStore>.Instance);
        var notificationStore = new SystemIncidentNotificationStore(configuration, database, NullLogger<SystemIncidentNotificationStore>.Instance);
        store.EnsureNotificationLeaseSchemaIfPresent();
        return new Harness(store, instances, recovery, incidentStore, notificationStore);
    }

    private sealed record Harness(
        SystemCoordinationStore Store,
        SystemInstanceRegistryService Instances,
        SystemRecoveryService Recovery,
        SystemIncidentStore Incidents,
        SystemIncidentNotificationStore NotificationStore);
}