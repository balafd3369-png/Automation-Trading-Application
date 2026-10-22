namespace TradeWithBeast.Services;

public static class PaperExitAnalytics
{
    public static bool IsTargetHit(string? exitReason) =>
        !string.IsNullOrWhiteSpace(exitReason)
        && exitReason.Contains("target", StringComparison.OrdinalIgnoreCase);

    public static bool IsStopLossHit(string? exitReason) =>
        !string.IsNullOrWhiteSpace(exitReason)
        && (exitReason.Equals("hard stop", StringComparison.OrdinalIgnoreCase)
            || exitReason.Contains("hard stop", StringComparison.OrdinalIgnoreCase)
            || exitReason.Contains("stop-loss", StringComparison.OrdinalIgnoreCase));

    public static bool IsTrailingStopHit(string? exitReason) =>
        !string.IsNullOrWhiteSpace(exitReason)
        && exitReason.Contains("trailing stop", StringComparison.OrdinalIgnoreCase);
}
