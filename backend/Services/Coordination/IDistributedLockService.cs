using TradeWithBeast.Models;

namespace TradeWithBeast.Services.Coordination;

public interface IDistributedLockService
{
    Task<DistributedLockAcquireResult> TryAcquireAsync(
        string lockName,
        string ownerInstanceId,
        TimeSpan lease,
        CancellationToken ct);

    Task<bool> RenewAsync(string lockName, string ownerInstanceId, string lockToken, TimeSpan lease, CancellationToken ct);

    Task<bool> ReleaseAsync(string lockName, string ownerInstanceId, string lockToken, CancellationToken ct);

    Task<bool> IsOwnerAsync(string lockName, string ownerInstanceId, string lockToken, long fencingToken, CancellationToken ct);

    IReadOnlyList<DistributedLockRecord> ListActive();
}