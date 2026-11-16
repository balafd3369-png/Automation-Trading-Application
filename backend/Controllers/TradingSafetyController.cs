using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin/trading-safety")]
[AdminAuthorize]
public sealed class TradingSafetyController : ControllerBase
{
    private readonly LiveTradingExecutionSafetyService _safety;
    private readonly BrokerCircuitBreaker _breaker;
    private readonly TradingSafetyStore _store;
    private readonly TradeHistoryStore _history;
    private readonly BrokerReconciliationService _reconciliation;
    private readonly ICurrentUserAccessor _user;

    public TradingSafetyController(
        LiveTradingExecutionSafetyService safety,
        BrokerCircuitBreaker breaker,
        TradingSafetyStore store,
        TradeHistoryStore history,
        BrokerReconciliationService reconciliation,
        ICurrentUserAccessor user)
    {
        _safety = safety;
        _breaker = breaker;
        _store = store;
        _history = history;
        _reconciliation = reconciliation;
        _user = user;
    }

    [HttpGet("summary")]
    public IActionResult Summary()
    {
        var manual = _history.ListNonTerminalManualLiveOrders();
        var live = _history.ListNonTerminalLiveOrders();
        var unknown = manual.Count(o => o.Status is "UNKNOWN" or "TIMEOUT")
            + live.Count(o => o.Status is "UNKNOWN" or "TIMEOUT");
        var stuck = manual.Count(o => o.Status is "SUBMITTED" or "OPEN" or "PENDING")
            + live.Count(o => o.Status is "SUBMITTED" or "OPEN" or "PENDING");
        var recon = _reconciliation.GetSnapshot();
        return Ok(new TradingSafetySummary(
            _safety.GateSnapshot(),
            _breaker.Snapshot(),
            manual.Count,
            live.Count,
            unknown,
            stuck,
            DateTimeOffset.UtcNow));
    }

    [HttpGet("emergency-gate/audit")]
    public IActionResult GateAudit([FromQuery] int limit = 50) => Ok(_store.ListGateAudit(limit));

    [HttpPost("emergency-gate/halt")]
    public IActionResult Halt([FromBody] EmergencyGateChangeRequest body)
    {
        if (!string.Equals(body.Confirmation?.Trim(), LiveTradingExecutionSafetyService.EmergencyConfirmPhrase, StringComparison.Ordinal))
        {
            return BadRequest(new { message = $"Type exactly: {LiveTradingExecutionSafetyService.EmergencyConfirmPhrase}" });
        }

        _safety.Halt(body.Reason ?? "Administrator halt", _user.UserId ?? "admin");
        return Ok(_safety.GateSnapshot());
    }

    [HttpPost("emergency-gate/clear")]
    public IActionResult Clear([FromBody] EmergencyGateChangeRequest body)
    {
        if (!string.Equals(body.Confirmation?.Trim(), LiveTradingExecutionSafetyService.EmergencyConfirmPhrase, StringComparison.Ordinal))
        {
            return BadRequest(new { message = $"Type exactly: {LiveTradingExecutionSafetyService.EmergencyConfirmPhrase}" });
        }

        _safety.Clear(_user.UserId ?? "admin", body.Reason ?? "Cleared");
        return Ok(_safety.GateSnapshot());
    }

    [HttpPost("circuit-breaker/reset")]
    public IActionResult ResetBreaker([FromBody] CircuitBreakerResetRequest body)
    {
        if (!string.Equals(body.Confirmation?.Trim(), LiveTradingExecutionSafetyService.EmergencyConfirmPhrase, StringComparison.Ordinal))
        {
            return BadRequest(new { message = $"Type exactly: {LiveTradingExecutionSafetyService.EmergencyConfirmPhrase}" });
        }

        _breaker.ResetManual(body.Reason);
        return Ok(_breaker.Snapshot());
    }

    [HttpPost("orders/{id}/resolve")]
    public IActionResult ResolveManual(string id, [FromBody] ManualOrderResolutionRequest body)
    {
        if (!string.Equals(body.Confirmation?.Trim(), LiveTradingExecutionSafetyService.EmergencyConfirmPhrase, StringComparison.Ordinal))
        {
            return BadRequest(new { message = $"Type exactly: {LiveTradingExecutionSafetyService.EmergencyConfirmPhrase}" });
        }

        var order = _history.GetManualOrder(id);
        if (order is null)
        {
            return NotFound();
        }

        var updated = order with
        {
            Status = "RESOLVED",
            Message = $"{body.Resolution} | evidence: {body.Evidence}",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _history.UpsertManualOrder(updated);
        _store.AppendGateAudit("ORDER_RESOLVE", $"Manual order {id}: {body.Resolution}", _user.UserId ?? "admin");
        return Ok(updated);
    }
}