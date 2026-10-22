namespace TradeWithBeast.Services;

public sealed class TenAmSessionTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DayState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool TryTakeSignal(string symbol, DateOnly istDay, string strategy, long signalBarTime, int maxAttempts = 2)
    {
        var key = $"{strategy}:{signalBarTime}";
        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state) || state.Day != istDay)
            {
                state = new DayState { Day = istDay };
                _states[symbol] = state;
            }

            if (state.ConsumedSignals.Contains(key))
            {
                return false;
            }

            var attempts = strategy == TenAmIntradayPattern.HighFadeStrategy
                ? state.FadeAttempts
                : state.LowBreakAttempts;
            if (attempts >= maxAttempts)
            {
                return false;
            }

            state.ConsumedSignals.Add(key);
            if (strategy == TenAmIntradayPattern.HighFadeStrategy)
            {
                state.FadeAttempts++;
            }
            else
            {
                state.LowBreakAttempts++;
            }

            return true;
        }
    }

    private sealed class DayState
    {
        public DateOnly Day { get; set; }
        public int FadeAttempts { get; set; }
        public int LowBreakAttempts { get; set; }
        public HashSet<string> ConsumedSignals { get; } = new(StringComparer.Ordinal);
    }
}
