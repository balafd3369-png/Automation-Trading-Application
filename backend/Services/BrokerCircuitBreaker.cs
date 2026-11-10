using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Blocks live broker orders after repeated failures, stale session, or missing heartbeat.</summary>
public sealed class BrokerCircuitBreaker
{
    private const int MaxConsecutiveFailures = 3;
    private static readonly TimeSpan HeartbeatStale = TimeSpan.FromMinutes(3);

    private readonly object _gate = new();
    private readonly BrokerConnectionManager _brokers;
    private readonly OperationsStore _ops;
    private readonly ILogger<BrokerCircuitBreaker> _log;

    private int _consecutiveFailures;
    private bool _tripped;
    private string? _tripReason;
    private DateTimeOffset? _trippedAt;
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastFailureAt;
    private DateTimeOffset? _lastHeartbeatAt;

    public BrokerCircuitBreaker(
        BrokerConnectionManager brokers,
        OperationsStore ops,
        ILogger<BrokerCircuitBreaker> log)
    {
        _brokers = brokers;
        _ops = ops;
        _log = log;
    }

    public BrokerCircuitBreakerSnapshot Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public (bool Allowed, string Message) AssertLiveOrderAllowed()
    {
        lock (_gate)
        {
            if (_tripped)
            {
                return (false, $"LIVE ORDERS BLOCKED: {_tripReason ?? "circuit breaker tripped"}. Reset from Operations.");
            }

            if (!_brokers.HasSession)
            {
                TripUnsafe("Broker session is not connected.");
                return (false, "LIVE ORDERS BLOCKED: connect m.Stock before placing live orders.");
            }

            if (_lastHeartbeatAt is null || DateTimeOffset.UtcNow - _lastHeartbeatAt > HeartbeatStale)
            {
                if (NseCashSession.IsLiveIntradayWindowNow())
                {
                    TripUnsafe("Broker heartbeat stale (no successful broker API recently).");
                    return (false, "LIVE ORDERS BLOCKED: broker heartbeat stale. Check connection or reset breaker.");
                }
            }

            return (true, "");
        }
    }

    public void RecordBrokerSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _lastSuccessAt = DateTimeOffset.UtcNow;
            _lastHeartbeatAt = _lastSuccessAt;
            if (_tripped)
            {
                _log.LogInformation("Broker circuit breaker cleared after successful broker call.");
            }

            _tripped = false;
            _tripReason = null;
            _trippedAt = null;
        }
    }

    public void RecordBrokerFailure(string reason)
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            _lastFailureAt = DateTimeOffset.UtcNow;
            _log.LogWarning(
                "Broker order failure #{Count}: {Reason}",
                _consecutiveFailures,
                reason);
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                TripUnsafe($"{MaxConsecutiveFailures} consecutive broker failures: {reason}");
            }
        }
    }

    public void RecordHeartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeatAt = DateTimeOffset.UtcNow;
        }
    }

    public void ResetManual(string reason)
    {
        lock (_gate)
        {
            _tripped = false;
            _tripReason = null;
            _trippedAt = null;
            _consecutiveFailures = 0;
            _ops.AppendAudit("BROKER", "CircuitBreakerReset", reason);
            _log.LogInformation("Circuit breaker manually reset: {Reason}", reason);
        }
    }

    private void TripUnsafe(string reason)
    {
        if (_tripped)
        {
            return;
        }

        _tripped = true;
        _tripReason = reason;
        _trippedAt = DateTimeOffset.UtcNow;
        _ops.AppendAudit("BROKER", "CircuitBreakerTripped", reason);
        _log.LogError("Circuit breaker TRIPPED: {Reason}", reason);
    }

    private BrokerCircuitBreakerSnapshot BuildSnapshotUnsafe() =>
        new(
            _tripped || (!_brokers.HasSession && NseCashSession.IsLiveIntradayWindowNow()),
            _tripped ? "Open" : "Closed",
            _consecutiveFailures,
            _lastSuccessAt,
            _lastFailureAt,
            _trippedAt,
            _tripReason,
            _lastHeartbeatAt);
}
