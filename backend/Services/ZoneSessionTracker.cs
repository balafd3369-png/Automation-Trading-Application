namespace TradeWithBeast.Services;

public sealed class ZoneSessionTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.OrdinalIgnoreCase);

    public void Update(string symbol, decimal last, decimal b1, DateOnly istDay)
    {
        if (string.IsNullOrWhiteSpace(symbol) || b1 <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state) || state.Day != istDay)
            {
                state = new SessionState(istDay, last, last, false, false, false);
            }

            var previous = state.PreviousLast;
            var sessionHigh = Math.Max(state.SessionHigh, last);
            var touchedB1 = state.TouchedB1 || sessionHigh >= b1 * (1m - 0.0015m);
            var pending = state.PendingRejection;
            if (ZoneRejectionPattern.TryDetectSessionRejection(sessionHigh, previous, last, b1, out var fresh) && fresh)
            {
                pending = true;
            }

            _states[symbol] = new SessionState(istDay, last, sessionHigh, touchedB1, pending, state.RejectionUsed);
        }
    }

    public bool TryTakePendingRejection(string symbol, DateOnly istDay)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state)
                || state.Day != istDay
                || !state.PendingRejection
                || state.RejectionUsed)
            {
                return false;
            }

            _states[symbol] = state with { PendingRejection = false, RejectionUsed = true };
            return true;
        }
    }

    public bool HasPendingRejection(string symbol, DateOnly istDay)
    {
        lock (_gate)
        {
            return _states.TryGetValue(symbol, out var state)
                && state.Day == istDay
                && state.PendingRejection
                && !state.RejectionUsed;
        }
    }

    private sealed record SessionState(
        DateOnly Day,
        decimal PreviousLast,
        decimal SessionHigh,
        bool TouchedB1,
        bool PendingRejection,
        bool RejectionUsed);
}
