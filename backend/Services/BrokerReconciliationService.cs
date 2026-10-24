using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class BrokerReconciliationService : IHostedService, IDisposable
{
    public const int DefaultIntervalSeconds = 20;

    private readonly object _gate = new();
    private readonly BrokerConnectionManager _brokers;
    private readonly TradeHistoryStore _history;
    private readonly OperationsStore _ops;
    private readonly ILogger<BrokerReconciliationService> _log;
    private Timer? _timer;
    private int _busy;
    private BrokerReconciliationSnapshot _snapshot = CreateSkipped("Not run yet");
    private int _lastAlertIssueCount;

    public BrokerReconciliationService(
        BrokerConnectionManager brokers,
        TradeHistoryStore history,
        OperationsStore ops,
        ILogger<BrokerReconciliationService> log)
    {
        _brokers = brokers;
        _history = history;
        _ops = ops;
        _log = log;
    }

    public BrokerReconciliationSnapshot GetSnapshot()
    {
        lock (_gate) return _snapshot;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(
            _ => _ = RunSafeAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(DefaultIntervalSeconds));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    internal async Task<BrokerReconciliationSnapshot> RunOnceAsync(CancellationToken ct) =>
        await ReconcileAsync(ct);

    private async Task RunSafeAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            await ReconcileAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Broker reconciliation cycle failed.");
            SetSnapshot(CreateBrokerError(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task<BrokerReconciliationSnapshot> ReconcileAsync(CancellationToken ct)
    {
        if (!_brokers.HasSession)
        {
            var skipped = CreateSkipped("Broker not connected");
            SetSnapshot(skipped);
            return skipped;
        }

        var positionsResult = await _brokers.Orders.GetPositionsAsync(ct);
        var ordersResult = await _brokers.Orders.GetOrdersAsync(ct);
        if (!positionsResult.Success || !ordersResult.Success)
        {
            var message = !positionsResult.Success
                ? positionsResult.Message
                : ordersResult.Message;
            var error = CreateBrokerError(message);
            SetSnapshot(error);
            return error;
        }

        var trades = _history.ReadLiveTrades(activeOnly: false);
        var compare = BrokerReconciliationEngine.Compare(new(
            trades,
            positionsResult.Data ?? [],
            ordersResult.Data ?? []));
        var issueCount = compare.Mismatches.Count;
        var health = issueCount == 0 ? BrokerReconciliationHealth.Healthy : BrokerReconciliationHealth.IssuesFound;
        var snapshot = new BrokerReconciliationSnapshot(
            health,
            HealthLabel(health),
            true,
            compare.PositionMatches,
            compare.PositionMismatches,
            compare.OrderMatches,
            compare.OrderMismatches,
            issueCount,
            DateTimeOffset.UtcNow,
            null,
            compare.Mismatches,
            compare.SuggestedActions);

        SetSnapshot(snapshot);
        _ops.AppendReconciliationHistory(snapshot);
        MaybeAlert(snapshot);
        return snapshot;
    }

    private void MaybeAlert(BrokerReconciliationSnapshot snapshot)
    {
        if (snapshot.Health != BrokerReconciliationHealth.IssuesFound)
        {
            _lastAlertIssueCount = 0;
            return;
        }

        if (snapshot.IssueCount <= _lastAlertIssueCount)
        {
            return;
        }

        _lastAlertIssueCount = snapshot.IssueCount;
        var summary = $"Broker reconciliation: {snapshot.IssueCount} issue(s) detected.";
        _ops.AppendNotification("warning", "BrokerReconciliation", summary);
        _ops.AppendAudit("RECONCILIATION", "Mismatch", summary);

        foreach (var mismatch in snapshot.Mismatches.Take(5))
        {
            var detail = $"{mismatch.Kind}: {mismatch.Symbol} — {mismatch.Reason}";
            _ops.AppendNotification("warning", MismatchEventType(mismatch), detail);
            _ops.AppendAudit("RECONCILIATION", MismatchEventType(mismatch), detail);
        }
    }

    private static string MismatchEventType(BrokerReconciliationMismatch mismatch) =>
        mismatch.Kind switch
        {
            "ORDER" => "OrderStatusMismatch",
            "POSITION" => "PositionMismatch",
            _ => "BrokerReconciliationMismatch",
        };

    private void SetSnapshot(BrokerReconciliationSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
    }

    private static BrokerReconciliationSnapshot CreateSkipped(string reason) =>
        new(
            BrokerReconciliationHealth.Skipped,
            "Skipped",
            false,
            0,
            0,
            0,
            0,
            0,
            DateTimeOffset.UtcNow,
            reason,
            [],
            []);

    private static BrokerReconciliationSnapshot CreateBrokerError(string message) =>
        new(
            BrokerReconciliationHealth.BrokerError,
            "Broker error",
            true,
            0,
            0,
            0,
            0,
            0,
            DateTimeOffset.UtcNow,
            message,
            [],
            ["Verify broker connection"]);

    private static string HealthLabel(BrokerReconciliationHealth health) => health switch
    {
        BrokerReconciliationHealth.Healthy => "Healthy",
        BrokerReconciliationHealth.IssuesFound => "Issues Found",
        BrokerReconciliationHealth.BrokerError => "Broker Error",
        _ => "Skipped",
    };
}
