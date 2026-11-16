using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin/production-safety")]
[AdminAuthorize]
public sealed class ProductionSafetyController : ControllerBase
{
    private readonly ProductionSafetyStatusService _status;
    private readonly ProductionSafetyStore _store;
    private readonly SqliteDatabaseBackupService _backups;
    private readonly BrokerReconciliationService _reconciliation;

    public ProductionSafetyController(
        ProductionSafetyStatusService status,
        ProductionSafetyStore store,
        SqliteDatabaseBackupService backups,
        BrokerReconciliationService reconciliation)
    {
        _status = status;
        _store = store;
        _backups = backups;
        _reconciliation = reconciliation;
    }

    [HttpGet("status")]
    public IActionResult Status() => Ok(_status.GetStatus());

    [HttpGet("backups")]
    public IActionResult Backups([FromQuery] int limit = 50) => Ok(_store.ListBackups(limit));

    [HttpPost("backups")]
    public async Task<IActionResult> TriggerBackup(CancellationToken ct)
    {
        var record = await _backups.CreateBackupAsync("Administrator backup request", ct);
        return record.Status is DatabaseBackupResultStatus.Failed
            ? StatusCode(500, record)
            : Ok(record);
    }

    [HttpPost("backups/{id}/verify")]
    public IActionResult VerifyBackup(string id)
    {
        var record = _store.GetBackup(id);
        if (record is null)
        {
            return NotFound();
        }

        var verified = _backups.VerifyBackupFile(record.BackupFilePath);
        return Ok(new { backupId = id, verified, path = Path.GetFileName(record.BackupFilePath) });
    }

    [HttpGet("order-audit/latest")]
    public IActionResult LatestOrderAudit() =>
        _store.LatestStartupAudit() is { } audit ? Ok(audit) : NotFound();

    [HttpGet("reconciliation")]
    public IActionResult Reconciliation()
    {
        var snapshot = _reconciliation.GetSnapshot();
        return Ok(snapshot);
    }
}