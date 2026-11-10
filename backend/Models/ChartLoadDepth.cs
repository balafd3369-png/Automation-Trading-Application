namespace TradeWithBeast.Models;

public enum ChartLoadDepth
{
    /// <summary>Today only (1-2 sessions). Used for first-paint speed.</summary>
    Fast,
    /// <summary>~5 sessions. Progressive load stage 2 — fills in prior sessions after first paint.</summary>
    Mid,
    /// <summary>Full history (up to 12 sessions). Background deep history.</summary>
    Full,
}
