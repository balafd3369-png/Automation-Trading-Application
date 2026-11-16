namespace TradeWithBeast.Services;

/// <summary>Polls live option premiums for watched option charts (~1s) and pushes chartLive.</summary>
public sealed class OptionChartLiveHostedService : BackgroundService
{
    private readonly MockMarketService _market;
    private readonly ILogger<OptionChartLiveHostedService> _log;

    public OptionChartLiveHostedService(MockMarketService market, ILogger<OptionChartLiveHostedService> log)
    {
        _market = market;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!NseCashSession.IsLiveIntradayWindowNow())
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            try
            {
                await _market.PublishWatchedOptionChartsLiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Option chart live tick failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}