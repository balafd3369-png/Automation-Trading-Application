using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SystemRecoveryPolicyService
{
    private readonly SystemCoordinationStore _store;
    private readonly SystemReliabilityOptions _options;

    public SystemRecoveryPolicyService(SystemCoordinationStore store, SystemReliabilityOptions options)
    {
        _store = store;
        _options = options;
    }

    public bool IsAutomaticRecoveryAllowed(string workerName)
    {
        if (TradingCriticalWorkers.DenyAutomaticRecovery.Contains(workerName))
        {
            return false;
        }

        var policy = _store.ListRecoveryPolicies().FirstOrDefault(p => p.WorkerName == workerName);
        return policy?.AutomaticRecoveryAllowed ?? false;
    }

    public IReadOnlyList<SystemRecoveryPolicyRecord> List() => _store.ListRecoveryPolicies();

    public void Replace(IReadOnlyList<SystemRecoveryPolicyRecord> policies)
    {
        foreach (var policy in policies)
        {
            var sanitized = policy with
            {
                MaxAttempts = Math.Clamp(policy.MaxAttempts, 0, _options.RecoveryMaxAttempts),
                CooldownSeconds = Math.Clamp(policy.CooldownSeconds, 0, 86400),
            };
            var finalPolicy = TradingCriticalWorkers.DenyAutomaticRecovery.Contains(sanitized.WorkerName)
                ? sanitized with { AutomaticRecoveryAllowed = false, MaxAttempts = 0 }
                : sanitized;
            _store.UpsertRecoveryPolicy(finalPolicy);
        }
    }
}