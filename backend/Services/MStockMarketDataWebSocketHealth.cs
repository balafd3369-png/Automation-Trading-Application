namespace TradeWithBeast.Services;

/// <summary>Tracks recent m.Stock Type A WebSocket market ticks (for feed fallback decisions).</summary>
public sealed class MStockMarketDataWebSocketHealth
{
    private long _lastTickUnixMs;

    public void RecordTick()
    {
        Interlocked.Exchange(ref _lastTickUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _lastTickUnixMs, 0);
    }

    public bool ReceivedWithin(TimeSpan window)
    {
        var last = Interlocked.Read(ref _lastTickUnixMs);
        if (last <= 0)
        {
            return false;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last <= (long)window.TotalMilliseconds;
    }
}