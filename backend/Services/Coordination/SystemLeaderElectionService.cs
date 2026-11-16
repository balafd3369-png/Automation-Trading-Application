using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemLeaderElectionService
{
    private readonly SystemCoordinationStore _store;
    private readonly SystemReliabilityOptions _options;
    private readonly SystemInstanceRegistryService _instances;
    private readonly ILogger<SystemLeaderElectionService> _log;

    public SystemLeaderElectionService(
        SystemCoordinationStore store,
        SystemReliabilityOptions options,
        SystemInstanceRegistryService instances,
        ILogger<SystemLeaderElectionService> log)
    {
        _store = store;
        _options = options;
        _instances = instances;
        _log = log;
    }

    public bool IsLeader(string role)
    {
        if (!_options.Enabled || !_options.LeaderElectionEnabled)
        {
            return true;
        }

        var leader = _store.GetLeader(role);
        return leader is not null
               && leader.LeaderInstanceId == _instances.InstanceId
               && leader.LeaseExpiresUtc > DateTimeOffset.UtcNow;
    }

    public bool TryBecomeLeader(string role)
    {
        if (!_options.Enabled || !_options.LeaderElectionEnabled)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var lease = now.AddSeconds(_options.LeaderLeaseSeconds);
        var fence = DateTimeOffset.UtcNow.Ticks;
        var elected = _store.TryElectLeader(role, _instances.InstanceId, now, lease, fence);
        if (elected)
        {
            _log.LogInformation("Leader elected for {Role} instance {InstanceId}.", role, _instances.InstanceId);
        }

        return elected;
    }

    public LeaderElectionRecord? Current(string role) => _store.GetLeader(role);
}