using Microsoft.AspNetCore.SignalR;
using TradeWithBeast.Services;

namespace TradeWithBeast.Hubs;

public sealed class MarketChartHub : Hub
{
    public const string LiveEvent = "chartLive";
    private readonly ChartLiveStreamService _streams;

    public MarketChartHub(ChartLiveStreamService streams)
    {
        _streams = streams;
    }

    public static string GroupKey(string name, string interval) =>
        $"chart:{name.ToUpperInvariant()}:{NormalizeInterval(interval)}";

    public Task WatchChart(string name, string interval)
    {
        var key = GroupKey(name, interval);
        ChartLiveStreamService.AddWatch(key);
        _streams.TrackConnectionWatch(Context.ConnectionId, key);
        return Groups.AddToGroupAsync(Context.ConnectionId, key);
    }

    public Task UnwatchChart(string name, string interval)
    {
        var key = GroupKey(name, interval);
        ChartLiveStreamService.RemoveWatch(key);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        ChartLiveStreamService.ClearConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static string NormalizeInterval(string interval) =>
        interval switch
        {
            "1m" or "minute" => "minute",
            "15m" or "15minute" => "15minute",
            "1h" or "60minute" => "60minute",
            "1D" or "day" => "day",
            _ => "5minute",
        };
}
