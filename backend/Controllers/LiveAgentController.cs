using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/live-agent")]
public sealed class LiveAgentController : ControllerBase
{
    private readonly LiveExecutionCoordinator _live;

    public LiveAgentController(LiveExecutionCoordinator live)
    {
        _live = live;
    }

    [HttpGet]
    public IActionResult Get() => Ok(_live.Snapshot());

    [HttpPost("arm")]
    public IActionResult Arm([FromBody] ArmLiveRequest request) =>
        Ok(_live.Arm(request.Acknowledgement));

    [HttpPost("disarm")]
    public IActionResult Disarm() => Ok(_live.Disarm());

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmLiveTradeRequest request,
        CancellationToken ct) =>
        Ok(await _live.ConfirmAsync(request.ProposalId, request.Product, ct));

    [HttpPost("exit")]
    public async Task<IActionResult> Exit(
        [FromBody] LiveTradeActionRequest request,
        CancellationToken ct) =>
        Ok(await _live.EmergencyExitAsync(request.TradeId, ct));

    [HttpPost("eod")]
    public async Task<IActionResult> Eod(
        [FromBody] LiveEodActionRequest request,
        CancellationToken ct) =>
        Ok(await _live.HandleEodAsync(request.TradeId, request.Action, ct));
}
