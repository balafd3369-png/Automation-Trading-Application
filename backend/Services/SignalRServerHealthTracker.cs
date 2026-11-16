using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Server-side SignalR hub connection observability (not client connectivity).</summary>
public sealed class SignalRServerHealthTracker
{
    private readonly object _gate = new();
    private int _activeConnections;
    private int _totalConnectionsObserved;
    private int _disconnectCount;
    private int _connectionFailureCount;
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private DateTimeOffset? _lastHubActivityAt;
    private string? _lastError;

    public void RecordConnected()
    {
        lock (_gate)
        {
            _activeConnections++;
            _totalConnectionsObserved++;
            _lastConnectedAt = DateTimeOffset.UtcNow;
            _lastHubActivityAt = _lastConnectedAt;
        }
    }

    public void RecordDisconnected(Exception? exception)
    {
        lock (_gate)
        {
            _activeConnections = Math.Max(0, _activeConnections - 1);
            _disconnectCount++;
            _lastDisconnectedAt = DateTimeOffset.UtcNow;
            if (exception is not null)
            {
                _connectionFailureCount++;
                _lastError = exception.Message;
            }
        }
    }

    public void RecordHubBroadcast()
    {
        lock (_gate)
        {
            _lastHubActivityAt = DateTimeOffset.UtcNow;
        }
    }

    public SignalRServerHealthSnapshot BuildSnapshot(DateTimeOffset checkedAt, bool enabled)
    {
        lock (_gate)
        {
            if (!enabled)
            {
                return new SignalRServerHealthSnapshot(
                    ComponentHealthState.Unknown,
                    _activeConnections,
                    _totalConnectionsObserved,
                    _disconnectCount,
                    _connectionFailureCount,
                    _lastConnectedAt,
                    _lastDisconnectedAt,
                    _lastHubActivityAt,
                    null,
                    "SignalR monitoring disabled.",
                    checkedAt,
                    0);
            }

            var state = ComponentHealthState.Healthy;
            if (_connectionFailureCount > 0 && _activeConnections == 0)
            {
                state = ComponentHealthState.Degraded;
            }

            return new SignalRServerHealthSnapshot(
                state,
                _activeConnections,
                _totalConnectionsObserved,
                _disconnectCount,
                _connectionFailureCount,
                _lastConnectedAt,
                _lastDisconnectedAt,
                _lastHubActivityAt,
                _lastError,
                "Server-side hub metrics only; client connectivity is not verified without frontend telemetry.",
                checkedAt,
                0);
        }
    }
}