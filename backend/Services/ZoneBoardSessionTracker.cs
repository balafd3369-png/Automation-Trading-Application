namespace TradeWithBeast.Services;

/// <summary>
/// One signal per setup per symbol per IST day (re-entry up to <see cref="MaxAttemptsPerSetup"/>).
/// </summary>
public sealed class ZoneBoardSessionTracker
{
    public const int MaxAttemptsPerSetup = 2;

    private readonly object _gate = new();
    private readonly Dictionary<string, DayState> _states = new(StringComparer.OrdinalIgnoreCase);

    public void Update(
        string symbol,
        decimal last,
        decimal b1,
        decimal s1,
        decimal s2,
        DateOnly istDay)
    {
        if (string.IsNullOrWhiteSpace(symbol) || b1 <= 0 || s1 <= 0 || s2 <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state) || state.Day != istDay)
            {
                state = new DayState { Day = istDay, SessionHigh = last, SessionLow = last };
                _states[symbol] = state;
            }

            state.SessionHigh = Math.Max(state.SessionHigh, last);
            state.SessionLow = Math.Min(state.SessionLow, last);
            if (state.SessionHigh >= b1 * (1m - ZoneBoardPattern.TouchTolerance))
            {
                state.TouchedB1 = true;
            }

            if (state.SessionLow <= s2 * (1m + ZoneBoardPattern.TouchTolerance))
            {
                state.TouchedS2 = true;
            }
        }
    }

    public bool TryTakeCross(
        string symbol,
        DateOnly istDay,
        string setupId,
        decimal previous,
        decimal current,
        decimal b1,
        decimal s1)
    {
        var signal = PaperTradeRules.DetectZoneCross(previous, current, b1, s1);
        if (signal == PaperEntrySignal.None)
        {
            return false;
        }

        var key = setupId switch
        {
            ZoneBoardPattern.B1BreakLong when signal == PaperEntrySignal.Long => ZoneBoardPattern.B1BreakLong,
            ZoneBoardPattern.S1BreakShort when signal == PaperEntrySignal.Short => ZoneBoardPattern.S1BreakShort,
            _ => null,
        };
        if (key is null)
        {
            return false;
        }

        return TryConsume(symbol, istDay, key);
    }

    public bool TryTakeS2Bounce(
        string symbol,
        DateOnly istDay,
        decimal previous,
        decimal current,
        decimal s2)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state)
                || state.Day != istDay
                || !state.TouchedS2
                || previous > s2
                || current <= s2)
            {
                return false;
            }
        }

        return TryConsume(symbol, istDay, ZoneBoardPattern.S2BounceLong);
    }

    public bool TouchedB1(string symbol, DateOnly istDay)
    {
        lock (_gate)
        {
            return _states.TryGetValue(symbol, out var state)
                && state.Day == istDay
                && state.TouchedB1;
        }
    }

    private bool TryConsume(string symbol, DateOnly istDay, string setupId)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(symbol, out var state) || state.Day != istDay)
            {
                state = new DayState { Day = istDay };
                _states[symbol] = state;
            }

            var attempts = state.Attempts.GetValueOrDefault(setupId);
            if (attempts >= MaxAttemptsPerSetup)
            {
                return false;
            }

            state.Attempts[setupId] = attempts + 1;
            return true;
        }
    }

    private sealed class DayState
    {
        public DateOnly Day { get; set; }
        public decimal SessionHigh { get; set; }
        public decimal SessionLow { get; set; }
        public bool TouchedB1 { get; set; }
        public bool TouchedS2 { get; set; }
        public Dictionary<string, int> Attempts { get; } = new(StringComparer.Ordinal);
    }
}
