using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ProductionSafetyStatusService
{
    private readonly ProductionSafetyOptions _options;
    private readonly ProductionSafetyStore _store;
    private readonly BrokerReconciliationService _reconciliation;

    public ProductionSafetyStatusService(
        ProductionSafetyOptions options,
        ProductionSafetyStore store,
        BrokerReconciliationService reconciliation)
    {
        _options = options;
        _store = store;
        _reconciliation = reconciliation;
    }

    public ProductionSafetyStatusSnapshot GetStatus()
    {
        var lastBackup = _store.LastBackup();
        var backups = _store.ListBackups(200);
        var audit = _store.LatestStartupAudit();
        var recon = _reconciliation.GetSnapshot();
        var uncertain = audit?.StillUncertainCount ?? audit?.FindingCount ?? 0;
        return new ProductionSafetyStatusSnapshot(
            _options.Enabled,
            lastBackup?.CompletedUtc ?? lastBackup?.StartedUtc,
            lastBackup?.Status,
            lastBackup?.FileSizeBytes,
            backups.Count,
            audit?.CompletedUtc,
            uncertain,
            recon.IssueCount,
            recon.HealthLabel,
            DateTimeOffset.UtcNow);
    }
}