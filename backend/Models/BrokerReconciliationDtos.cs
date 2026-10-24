namespace TradeWithBeast.Models;

public enum BrokerReconciliationHealth
{
    Skipped = 0,
    Healthy = 1,
    IssuesFound = 2,
    BrokerError = 3,
}

public sealed record BrokerReconciliationMismatch(
    string Kind,
    string Symbol,
    string ApplicationState,
    string BrokerState,
    string Reason,
    string? SuggestedAction);

public sealed record BrokerReconciliationSnapshot(
    BrokerReconciliationHealth Health,
    string HealthLabel,
    bool BrokerConnected,
    int PositionMatches,
    int PositionMismatches,
    int OrderMatches,
    int OrderMismatches,
    int IssueCount,
    DateTimeOffset? LastRunAt,
    string? BrokerFetchMessage,
    IReadOnlyList<BrokerReconciliationMismatch> Mismatches,
    IReadOnlyList<string> SuggestedActions);

public sealed record BrokerReconciliationHistoryEntry(
    string Id,
    DateTimeOffset At,
    BrokerReconciliationHealth Result,
    string ResultLabel,
    int IssueCount,
    int PositionMatches,
    int PositionMismatches,
    int OrderMatches,
    int OrderMismatches,
    IReadOnlyList<BrokerReconciliationMismatch> Mismatches);
