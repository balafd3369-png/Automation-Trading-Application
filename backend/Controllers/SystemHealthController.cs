using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin/system-health")]
[AdminAuthorize]
public sealed class SystemHealthController : ControllerBase
{
    private readonly SystemHealthService _health;

    public SystemHealthController(SystemHealthService health)
    {
        _health = health;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(_health.GetLiveSnapshot());

    [HttpGet("brokers")]
    public IActionResult Brokers() => Ok(_health.GetBrokerHealth());

    [HttpGet("market-data")]
    public IActionResult MarketData() => Ok(_health.GetMarketDataHealth());

    [HttpGet("signalr")]
    public IActionResult SignalR() => Ok(_health.GetSignalRHealth());

    [HttpGet("workers")]
    public IActionResult Workers() => Ok(_health.GetWorkerHealth());

    [HttpGet("components")]
    public IActionResult Components() => Ok(_health.GetComponentSummaries());

    [HttpGet("components/{component}")]
    public IActionResult Component(string component)
    {
        var summary = _health.GetComponentSummary(component);
        return summary is null ? NotFound() : Ok(summary);
    }

    [HttpPost("probe/database")]
    public IActionResult ProbeDatabase() => Ok(_health.ProbeDatabaseNow());

    [HttpPost("probe/broker")]
    public async Task<IActionResult> ProbeBroker(CancellationToken ct) =>
        Ok(await _health.ProbeBrokerNowAsync(ct));

    [HttpPost("probe/market-data")]
    public async Task<IActionResult> ProbeMarketData(CancellationToken ct) =>
        Ok(await _health.ProbeMarketDataNowAsync(ct));

    [HttpPost("probe/signalr")]
    public async Task<IActionResult> ProbeSignalR(CancellationToken ct) =>
        Ok(await _health.ProbeSignalRNowAsync(ct));

    [HttpPost("reports/comprehensive")]
    public async Task<IActionResult> RunComprehensive(CancellationToken ct) =>
        Ok(await _health.RunAndPersistComprehensiveReportAsync(ct));

    [HttpGet("reports")]
    public IActionResult Reports([FromQuery] int limit = 50) => Ok(_health.ListHistoricalReports(limit));

    [HttpGet("reports/{id}")]
    public IActionResult Report(string id)
    {
        var entry = _health.GetHistoricalReport(id);
        return entry is null ? NotFound() : Ok(entry);
    }
}