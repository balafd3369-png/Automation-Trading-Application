using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DatabaseBackupResultStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Verified = 3,
    VerificationFailed = 4,
}

public sealed record DatabaseBackupRecord(
    string Id,
    string SourceDatabasePath,
    string BackupFilePath,
    DatabaseBackupResultStatus Status,
    long FileSizeBytes,
    string? FailureReason,
    bool IntegrityVerified,
    string CorrelationId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc);

public sealed record ProductionSafetyStatusSnapshot(
    bool Enabled,
    DateTimeOffset? LastBackupUtc,
    DatabaseBackupResultStatus? LastBackupStatus,
    long? LastBackupSizeBytes,
    int BackupCount,
    DateTimeOffset? LastStartupAuditUtc,
    int UnresolvedOrderFindings,
    int ReconciliationIssueCount,
    string? ReconciliationHealth,
    DateTimeOffset GeneratedUtc);

public sealed record OrderSafetyAuditFinding(
    string OrderId,
    string TradeId,
    string Symbol,
    string Status,
    string? BrokerOrderId,
    string FindingKind,
    string Message,
    DateTimeOffset DetectedUtc);

public sealed record OrderStartupAuditSummary(
    string Id,
    int FindingCount,
    int ReconciledCount,
    int StillUncertainCount,
    string CorrelationId,
    DateTimeOffset CompletedUtc,
    IReadOnlyList<OrderSafetyAuditFinding> Findings);