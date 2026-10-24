using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/operations")]
public sealed class OperationsController : ControllerBase
{
    private readonly OperationsSuiteService _ops;

    public OperationsController(OperationsSuiteService ops) => _ops = ops;

    [HttpGet("risk")]
    public IActionResult Risk() => Ok(_ops.GetRiskDashboard());

    [HttpGet("broker/portfolio")]
    public async Task<IActionResult> BrokerPortfolio(CancellationToken ct) =>
        Ok(await _ops.GetBrokerPortfolioAsync(ct));

    [HttpGet("broker/health")]
    public IActionResult BrokerHealth() => Ok(_ops.GetBrokerHealth());

    [HttpGet("reconciliation")]
    public IActionResult Reconciliation() => Ok(_ops.GetReconciliationDashboard());

    [HttpGet("reconciliation/history")]
    public IActionResult ReconciliationHistory([FromQuery] int limit = 50) =>
        Ok(_ops.GetReconciliationHistory(limit));

    [HttpGet("orders")]
    public IActionResult Orders() => Ok(_ops.GetOrdersBook());

    [HttpGet("positions")]
    public IActionResult Positions() => Ok(_ops.GetPositionsBook());

    [HttpGet("holdings")]
    public IActionResult Holdings() => Ok(_ops.GetHoldings());

    [HttpGet("journal")]
    public IActionResult Journal([FromQuery] OperationsJournalQuery query) =>
        Ok(_ops.QueryJournal(query));

    [HttpPost("journal/notes")]
    public IActionResult SaveNotes([FromBody] OperationsJournalNotesRequest request)
    {
        _ops.SaveJournalNotes(request.TradeId, request.Notes ?? "");
        return Ok(new { status = "saved" });
    }

    [HttpGet("timeline")]
    public IActionResult Timeline([FromQuery] OperationsTimelineQuery query) =>
        Ok(_ops.GetTimeline(query));

    [HttpGet("audit")]
    public IActionResult Audit([FromQuery] int limit = 200) =>
        Ok(_ops.GetAuditLog(limit));

    [HttpGet("notifications")]
    public IActionResult Notifications([FromQuery] int limit = 100, [FromQuery] bool unreadOnly = false) =>
        Ok(_ops.GetNotifications(limit, unreadOnly));

    [HttpPost("notifications/{id}/read")]
    public IActionResult MarkRead(string id)
    {
        _ops.MarkNotificationRead(id);
        return Ok(new { status = "read" });
    }

    [HttpPost("emergency")]
    public async Task<IActionResult> Emergency(
        [FromBody] OperationsEmergencyRequest request,
        CancellationToken ct)
    {
        var result = await _ops.ExecuteEmergencyAsync(request, ct);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
