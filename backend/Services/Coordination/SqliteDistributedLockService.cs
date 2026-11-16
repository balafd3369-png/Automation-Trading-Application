using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public sealed class SqliteDistributedLockService : IDistributedLockService
{
    private readonly SystemCoordinationStore _store;
    private readonly SystemReliabilityOptions _options;
    private readonly ILogger<SqliteDistributedLockService> _log;

    public SqliteDistributedLockService(
        SystemCoordinationStore store,
        SystemReliabilityOptions options,
        ILogger<SqliteDistributedLockService> log)
    {
        _store = store;
        _options = options;
        _log = log;
    }

    public Task<DistributedLockAcquireResult> TryAcquireAsync(
        string lockName,
        string ownerInstanceId,
        TimeSpan lease,
        CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DistributedLocksEnabled)
        {
            return Task.FromResult(new DistributedLockAcquireResult(true, Guid.NewGuid().ToString("N"), 1, DateTimeOffset.UtcNow.Add(lease), null));
        }

        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid().ToString("N");
        var expires = now.Add(lease);
        var result = _store.TryAcquireLock(lockName, ownerInstanceId, token, now, expires);
        if (result.Acquired)
        {
            _log.LogInformation(
                "Lock acquired {LockName} for {Owner} fence {Fence} until {ExpiresUtc}.",
                lockName,
                ownerInstanceId,
                result.FencingToken,
                expires);
        }

        return Task.FromResult(result);
    }

    public Task<bool> RenewAsync(string lockName, string ownerInstanceId, string lockToken, TimeSpan lease, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DistributedLocksEnabled)
        {
            return Task.FromResult(true);
        }

        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var ok = _store.RenewLock(lockName, ownerInstanceId, lockToken, now.Add(lease), now);
        if (ok)
        {
            _log.LogDebug("Lock renewed {LockName} for {Owner}.", lockName, ownerInstanceId);
        }

        return Task.FromResult(ok);
    }

    public Task<bool> ReleaseAsync(string lockName, string ownerInstanceId, string lockToken, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DistributedLocksEnabled)
        {
            return Task.FromResult(true);
        }

        var ok = _store.ReleaseLock(lockName, ownerInstanceId, lockToken);
        if (ok)
        {
            _log.LogInformation("Lock released {LockName} for {Owner}.", lockName, ownerInstanceId);
        }

        return Task.FromResult(ok);
    }

    public Task<bool> IsOwnerAsync(
        string lockName,
        string ownerInstanceId,
        string lockToken,
        long fencingToken,
        CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DistributedLocksEnabled)
        {
            return Task.FromResult(true);
        }

        var lockRecord = _store.ListLocks().FirstOrDefault(l => l.LockName == lockName);
        if (lockRecord is null || lockRecord.IsExpired)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(
            lockRecord.OwnerInstanceId == ownerInstanceId
            && lockRecord.LockToken == lockToken
            && lockRecord.FencingToken == fencingToken);
    }

    public IReadOnlyList<DistributedLockRecord> ListActive() => _store.ListLocks();
}