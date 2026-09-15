using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/broker")]
public sealed class BrokerAuthController : ControllerBase
{
    private readonly MStockAuthClient _auth;

    public BrokerAuthController(MStockAuthClient auth)
    {
        _auth = auth;
    }

    public sealed record OtpBody(string Otp);

    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp(CancellationToken ct)
    {
        var result = await _auth.SendOtpAsync(ct);
        var ok = result.TryGetProperty("status", out var status) && status.GetString() == "success";
        var message = result.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        return Ok(new { status = ok ? "success" : "error", message });
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Otp))
        {
            return BadRequest(new { status = "error", message = "OTP is required." });
        }

        var result = await _auth.VerifyOtpAsync(body.Otp, ct);
        if (result.TryGetProperty("status", out var status) && status.GetString() == "success")
        {
            return Ok(new { status = "success", connected = true, message = "m.Stock session is active until midnight." });
        }

        var message = result.TryGetProperty("message", out var msg) ? msg.GetString() : "OTP verify failed.";
        return Ok(new { status = "error", connected = false, message });
    }

    [HttpGet("session")]
    public IActionResult Session()
        => Ok(new { connected = _auth.HasSession, broker = "m.Stock Type A" });
}
