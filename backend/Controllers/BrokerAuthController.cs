using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/broker")]
    public sealed class BrokerAuthController : ControllerBase
    {
    private readonly BrokerConnectionManager _brokers;
    private readonly MStockAuthClient _mstock;
    private readonly MockMarketService _market;
    private readonly TradingPlatformOptions _platform;

    public BrokerAuthController(
        BrokerConnectionManager brokers,
        MStockAuthClient mstock,
        MockMarketService market,
        TradingPlatformOptions platform)
    {
        _brokers = brokers;
        _mstock = mstock;
        _market = market;
        _platform = platform;
    }

    public sealed record OtpBody(string Otp, string? ApiKey);

    public sealed record OtpSendBody(string? Username, string? Password, string? ApiKey);

    [HttpGet("capabilities")]
    public IActionResult Capabilities() =>
        Ok(new
        {
            version = 2,
            mstockOtp = true,
            apiKeyConfigured = _mstock.IsApiKeyConfigured(),
            futuresEnabled = _platform.FuturesEnabled,
            defaultInstrumentType = InstrumentTypeNormalizer.DefaultApiValue(),
        });

    [HttpGet("providers")]
    public IActionResult Providers() =>
        Ok(_brokers.Providers.Select(provider => new
        {
            id = provider.Id,
            displayName = provider.DisplayName,
            authKind = provider.AuthKind.ToString(),
            exchanges = provider.SupportedExchanges,
        }));

    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] OtpSendBody? body, CancellationToken ct)
    {
        _brokers.SetActiveProvider(BrokerIds.MStock);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(28));
            var result = await _mstock.SendOtpAsync(body?.Username, body?.Password, body?.ApiKey, deadline.Token);
            var ok = MStockJson.IsOtpSendAcknowledged(result);
            var message = MStockJson.ReadMessage(result);
            if (!ok && string.IsNullOrWhiteSpace(message))
            {
                message = "m.Stock did not confirm OTP delivery. Check user ID, password, and API key.";
            }

            if (ok && string.IsNullOrWhiteSpace(message))
            {
                message = "OTP sent to your m.Stock registered mobile number.";
            }

            return Ok(new { status = ok ? "success" : "error", message, provider = BrokerIds.MStock });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Ok(new
            {
                status = "success",
                message = "OTP request is taking longer than usual. If SMS arrived on your phone, enter it below.",
                provider = BrokerIds.MStock,
            });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Ok(new
            {
                status = "success",
                message = "m.Stock is slow to respond. If SMS arrived on your phone, enter the OTP below and click Submit.",
                provider = BrokerIds.MStock,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new
            {
                status = "error",
                message = ex.Message,
                provider = BrokerIds.MStock,
            });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new
            {
                status = "error",
                message = ex.Message,
                provider = BrokerIds.MStock,
            });
        }
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Otp))
        {
            return BadRequest(new { status = "error", message = "OTP is required." });
        }

        _brokers.SetActiveProvider(BrokerIds.MStock);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(28));
            var result = await _mstock.VerifyOtpAsync(body.Otp, body.ApiKey, deadline.Token);
            if (_brokers.HasSession)
            {
                _brokers.OnMStockSessionEstablished();
                _ = Task.Run(() => _market.NotifyBrokerSessionEstablished());
                return Ok(new
                {
                    status = "success",
                    connected = true,
                    provider = BrokerIds.MStock,
                    displayName = _mstock.DisplayName,
                    message = "m.Stock session is active until midnight.",
                });
            }

            var message = MStockJson.ReadMessage(result)
                ?? "OTP verify failed. Click Request OTP again and use only the latest SMS code.";
            return Ok(new { status = "error", connected = false, provider = BrokerIds.MStock, message });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Ok(new
            {
                status = "error",
                connected = _brokers.HasSession,
                provider = BrokerIds.MStock,
                message = "OTP verify timed out. If the board shows live prices, you are connected; otherwise request a fresh OTP.",
            });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new
            {
                status = "error",
                connected = _brokers.HasSession,
                provider = BrokerIds.MStock,
                message = ex.Message,
            });
        }
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var market = _brokers.Market;
        var connected = market.HasSession;
        decimal? availableFunds = null;
        decimal? utilizedFunds = null;
        if (connected)
        {
            var funds = await market.GetFundSummaryAsync(ct);
            if (funds is not null)
            {
                availableFunds = funds.AvailableBalance;
                utilizedFunds = funds.AmountUtilized;
            }
        }

        return Ok(new
        {
            connected,
            provider = connected ? BrokerIds.MStock : null,
            displayName = connected ? market.DisplayName : null,
            availableFunds,
            utilizedFunds,
        });
    }

    [HttpPost("session/clear")]
    public IActionResult ClearSession()
    {
        _brokers.ClearActiveSession();
        return Ok(new
        {
            status = "success",
            connected = false,
            message = "Broker session cleared. Connect again to resume live data and trading.",
        });
    }
}
