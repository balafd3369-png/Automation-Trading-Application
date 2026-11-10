namespace TradeWithBeast.Services;

/// <summary>Immediate halt for live confirmations and auto-live (in addition to disarm / circuit breaker).</summary>
public sealed class LiveTradingEmergencyGate
{
    private readonly object _gate = new();
    private bool _halted;
    private string _reason = "";

    public bool IsHalted
    {
        get
        {
            lock (_gate) return _halted;
        }
    }

    public string Reason
    {
        get
        {
            lock (_gate) return _reason;
        }
    }

    public void Halt(string reason)
    {
        lock (_gate)
        {
            _halted = true;
            _reason = string.IsNullOrWhiteSpace(reason) ? "Emergency halt active." : reason.Trim();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _halted = false;
            _reason = "";
        }
    }
}
