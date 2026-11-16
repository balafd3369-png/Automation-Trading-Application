namespace TradeWithBeast.Services;

/// <summary>Lightweight counter for unobserved task exceptions (Phase 1 runtime signal only).</summary>
public sealed class SystemHealthExceptionTracker
{
    private int _unobservedTaskExceptions;

    public void RecordUnobservedTaskException() => Interlocked.Increment(ref _unobservedTaskExceptions);

    public int UnobservedTaskExceptionCount => Volatile.Read(ref _unobservedTaskExceptions);
}