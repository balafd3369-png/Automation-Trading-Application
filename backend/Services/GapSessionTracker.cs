namespace TradeWithBeast.Services;

public sealed class GapSessionTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateOnly Day, GapFailedRallySignal Signal)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);

    public void SetPending(GapFailedRallySignal signal, DateOnly istDay)
    {
        var key = $"{signal.Symbol}:{istDay}";
        lock (_gate)
        {
            _pending[key] = (istDay, signal);
        }
    }

    public bool TryPeekPending(string symbol, DateOnly istDay, out GapFailedRallySignal signal)
    {
        signal = null!;
        var key = $"{symbol}:{istDay}";
        lock (_gate)
        {
            if (_consumed.Contains(key))
            {
                return false;
            }

            if (!_pending.TryGetValue(key, out var entry) || entry.Day != istDay)
            {
                return false;
            }

            signal = entry.Signal;
            return true;
        }
    }

    public bool TryTakePending(string symbol, DateOnly istDay, out GapFailedRallySignal signal)
    {
        if (!TryPeekPending(symbol, istDay, out signal))
        {
            return false;
        }

        var key = $"{symbol}:{istDay}";
        lock (_gate)
        {
            _consumed.Add(key);
            return true;
        }
    }

    public void MarkConsumed(string symbol, DateOnly istDay)
    {
        var key = $"{symbol}:{istDay}";
        lock (_gate)
        {
            _consumed.Add(key);
        }
    }

    public bool HasPending(string symbol, DateOnly istDay)
    {
        var key = $"{symbol}:{istDay}";
        lock (_gate)
        {
            return _pending.TryGetValue(key, out var e) && e.Day == istDay && !_consumed.Contains(key);
        }
    }
}
