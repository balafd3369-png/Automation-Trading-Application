using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemInstanceRegistryService
{
    private readonly SystemCoordinationStore _store;
    private readonly SystemReliabilityOptions _options;
    private readonly WorkerHeartbeatRegistry _heartbeats;
    private readonly ILogger<SystemInstanceRegistryService> _log;
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private bool _draining;

    public SystemInstanceRegistryService(
        SystemCoordinationStore store,
        SystemReliabilityOptions options,
        WorkerHeartbeatRegistry heartbeats,
        ILogger<SystemInstanceRegistryService> log)
    {
        _store = store;
        _options = options;
        _heartbeats = heartbeats;
        _log = log;
    }

    public string InstanceId => _heartbeats.InstanceId;

    public bool IsDraining => _draining;

    public void RegisterStartup()
    {
        if (!_options.Enabled || !_options.InstanceRegistryEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _store.UpsertInstance(new SystemInstanceRecord(
            InstanceId,
            Environment.MachineName,
            _options.ApplicationVersion,
            false,
            false,
            null,
            _startedUtc,
            now,
            now.AddSeconds(_options.InstanceStaleSeconds)));
        _log.LogInformation("Instance registered {InstanceId}.", InstanceId);
    }

    public void Pulse(bool isLeader, string? leaderRole)
    {
        if (!_options.Enabled || !_options.InstanceRegistryEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _store.UpsertInstance(new SystemInstanceRecord(
            InstanceId,
            Environment.MachineName,
            _options.ApplicationVersion,
            _draining,
            isLeader,
            leaderRole,
            _startedUtc,
            now,
            now.AddSeconds(_options.InstanceStaleSeconds)));
    }

    public void Drain()
    {
        _draining = true;
        _store.SetInstanceDraining(InstanceId, true);
        _log.LogWarning("Instance {InstanceId} entered drain mode.", InstanceId);
    }

    public IReadOnlyList<SystemInstanceRecord> List() => _store.ListInstances();

    public SystemInstanceRecord? Get(string instanceId) => _store.GetInstance(instanceId);
}