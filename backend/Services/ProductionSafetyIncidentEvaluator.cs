using System.Text.Json;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ProductionSafetyIncidentEvaluator : IProductionSafetyIncidentEvaluator
{
    private readonly ProductionSafetyOptions _options;
    private readonly ProductionSafetyStore _store;
    private readonly BrokerReconciliationService _reconciliation;
    private readonly SystemIncidentDeduplicationService _dedup;

    public ProductionSafetyIncidentEvaluator(
        ProductionSafetyOptions options,
        ProductionSafetyStore store,
        BrokerReconciliationService reconciliation,
        SystemIncidentDeduplicationService dedup)
    {
        _options = options;
        _store = store;
        _reconciliation = reconciliation;
        _dedup = dedup;
    }

    public IReadOnlyList<IncidentEvaluationCandidate> Evaluate()
    {
        if (!_options.Enabled)
        {
            return [];
        }

        var candidates = new List<IncidentEvaluationCandidate>();
        candidates.AddRange(EvaluateBackups());
        candidates.AddRange(EvaluateReconciliation());
        candidates.AddRange(EvaluateStartupAudit());
        return candidates;
    }

    private IEnumerable<IncidentEvaluationCandidate> EvaluateBackups()
    {
        var last = _store.LastBackup();
        if (last is null)
        {
            yield break;
        }

        var healthy = last.Status is DatabaseBackupResultStatus.Succeeded or DatabaseBackupResultStatus.Verified;
        var fingerprint = _dedup.BuildFingerprint(
            SystemIncidentComponents.Production,
            SystemIncidentCategory.DatabaseBackupFailed.ToString(),
            "sqlite");
        if (healthy)
        {
            yield return Healthy(fingerprint, SystemIncidentCategory.DatabaseBackupFailed);
            yield break;
        }

        if (last.Status is not (DatabaseBackupResultStatus.Failed or DatabaseBackupResultStatus.VerificationFailed))
        {
            yield break;
        }

        yield return new IncidentEvaluationCandidate(
            fingerprint,
            "Database backup failed",
            last.FailureReason ?? "Latest scheduled or manual backup did not succeed.",
            SystemIncidentComponents.Production,
            "sqlite",
            SystemIncidentCategory.DatabaseBackupFailed,
            SystemIncidentSeverity.High,
            last.FailureReason,
            1,
            false,
            JsonSerializer.Serialize(new { backupId = last.Id, status = last.Status.ToString() }));
    }

    private IEnumerable<IncidentEvaluationCandidate> EvaluateReconciliation()
    {
        if (!_options.ReconciliationIncidentsEnabled)
        {
            yield break;
        }

        var snapshot = _reconciliation.GetSnapshot();
        var fingerprintMismatch = _dedup.BuildFingerprint(
            SystemIncidentComponents.OrderSafety,
            SystemIncidentCategory.OrderReconciliationMismatch.ToString(),
            "broker-reconciliation");
        var fingerprintUnavailable = _dedup.BuildFingerprint(
            SystemIncidentComponents.OrderSafety,
            SystemIncidentCategory.OrderReconciliationUnavailable.ToString(),
            "broker-reconciliation");

        if (snapshot.Health == BrokerReconciliationHealth.Healthy || snapshot.Health == BrokerReconciliationHealth.Skipped)
        {
            yield return Healthy(fingerprintMismatch, SystemIncidentCategory.OrderReconciliationMismatch);
            yield return Healthy(fingerprintUnavailable, SystemIncidentCategory.OrderReconciliationUnavailable);
            yield break;
        }

        if (snapshot.Health == BrokerReconciliationHealth.BrokerError)
        {
            yield return new IncidentEvaluationCandidate(
                fingerprintUnavailable,
                "Order reconciliation unavailable",
                snapshot.BrokerFetchMessage ?? "Broker positions/orders could not be retrieved.",
                SystemIncidentComponents.OrderSafety,
                "broker-reconciliation",
                SystemIncidentCategory.OrderReconciliationUnavailable,
                SystemIncidentSeverity.High,
                snapshot.BrokerFetchMessage,
                1,
                false,
                null);
            yield return Healthy(fingerprintMismatch, SystemIncidentCategory.OrderReconciliationMismatch);
            yield break;
        }

        if (snapshot.IssueCount > 0)
        {
            var summary = $"{snapshot.IssueCount} broker/local mismatch(es). No corrective orders are submitted automatically.";
            yield return new IncidentEvaluationCandidate(
                fingerprintMismatch,
                "Broker reconciliation mismatch",
                summary,
                SystemIncidentComponents.OrderSafety,
                "broker-reconciliation",
                SystemIncidentCategory.OrderReconciliationMismatch,
                SystemIncidentSeverity.High,
                snapshot.Mismatches.FirstOrDefault()?.Reason,
                snapshot.IssueCount,
                false,
                JsonSerializer.Serialize(new { issueCount = snapshot.IssueCount, checkedAt = snapshot.LastRunAt }));
            yield return Healthy(fingerprintUnavailable, SystemIncidentCategory.OrderReconciliationUnavailable);
        }
    }

    private IEnumerable<IncidentEvaluationCandidate> EvaluateStartupAudit()
    {
        var audit = _store.LatestStartupAudit();
        if (audit is null)
        {
            yield break;
        }

        var uncertain = audit.Findings
            .Where(f => f.FindingKind is "BROKER_NOT_FOUND" or "MISSING_BROKER_ID" or "UNCERTAIN")
            .ToList();
        var fingerprint = _dedup.BuildFingerprint(
            SystemIncidentComponents.OrderSafety,
            SystemIncidentCategory.OrderStateUnknown.ToString(),
            "startup-audit");
        if (uncertain.Count == 0)
        {
            yield return Healthy(fingerprint, SystemIncidentCategory.OrderStateUnknown);
            yield break;
        }

        yield return new IncidentEvaluationCandidate(
            fingerprint,
            "Uncertain live order state after startup",
            $"{uncertain.Count} order(s) require manual investigation. Automatic resubmission is disabled.",
            SystemIncidentComponents.OrderSafety,
            "startup-audit",
            SystemIncidentCategory.OrderStateUnknown,
            SystemIncidentSeverity.Critical,
            uncertain[0].Message,
            uncertain.Count,
            false,
            JsonSerializer.Serialize(new { auditId = audit.Id, correlationId = audit.CorrelationId }));
    }

    private static IncidentEvaluationCandidate Healthy(string fingerprint, SystemIncidentCategory category) =>
        new(
            fingerprint,
            $"Healthy: {category}",
            "Component healthy.",
            category switch
            {
                SystemIncidentCategory.DatabaseBackupFailed => SystemIncidentComponents.Production,
                _ => SystemIncidentComponents.OrderSafety,
            },
            "n/a",
            category,
            SystemIncidentSeverity.Info,
            null,
            0,
            true,
            null);
}