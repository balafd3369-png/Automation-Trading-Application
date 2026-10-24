using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/manual-trading")]
public sealed class ManualTradingController : ControllerBase
{
    private readonly ManualTradingService _manual;

    public ManualTradingController(ManualTradingService manual) => _manual = manual;

    [HttpGet]
    public ActionResult<ManualTradingSnapshot> Get([FromQuery] string mode = "PAPER") =>
        Ok(_manual.GetSnapshot(mode));

    [HttpPost("orders")]
    public async Task<ActionResult<object>> PlaceOrder(
        [FromBody] ManualTradingPlaceOrderRequest request,
        CancellationToken ct)
    {
        var (success, message, order) = await _manual.PlaceOrderAsync(request, ct);
        if (!success)
        {
            return BadRequest(new { message, order });
        }

        return Ok(new { message, order });
    }

    [HttpPost("orders/cancel")]
    public ActionResult<object> Cancel([FromBody] ManualTradingCancelOrderRequest request)
    {
        var (success, message) = _manual.CancelOrder(request.OrderId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { message });
    }

    [HttpPost("positions/close")]
    public async Task<ActionResult<object>> ClosePosition(
        [FromBody] ManualTradingClosePositionRequest request,
        CancellationToken ct)
    {
        var (success, message) = await _manual.ClosePositionAsync(request, ct);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { message });
    }
}
