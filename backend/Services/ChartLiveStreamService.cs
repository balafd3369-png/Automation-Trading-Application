using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TradeWithBeast.Hubs;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ChartLiveStreamService
{
    private static readonly ConcurrentDictionary<string, int> WatchRefCounts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HashSet<string>> ConnectionWatches = new();

    private readonly IHubContext<MarketChartHub> _hub;
    private readonly ILogger<ChartLiveStreamService> _log;

    public ChartLiveStreamService(IHubContext<MarketChartHub> hub, ILogger<ChartLiveStreamService> log)
    {
        _hub = hub;
        _log = log;
    }

    public static void AddWatch(string groupKey)
    {
        WatchRefCounts.AddOrUpdate(groupKey, 1, static (_, count) => count + 1);
    }

    public static void RemoveWatch(string groupKey)
    {
        WatchRefCounts.AddOrUpdate(groupKey, 0, static (_, count) => Math.Max(0, count - 1));
    }

    public static void ClearConnection(string connectionId)
    {
        if (!ConnectionWatches.TryRemove(connectionId, out var keys))
        {
            return;
        }

        foreach (var key in keys)
        {
            RemoveWatch(key);
        }
    }

    public void TrackConnectionWatch(string connectionId, string groupKey)
    {
        var set = ConnectionWatches.GetOrAdd(connectionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (set)
        {
            set.Add(groupKey);
        }
    }

    public bool HasWatchers(string name)
    {
        var prefix = $"chart:{name.ToUpperInvariant()}:";
        foreach (var key in WatchRefCounts.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && WatchRefCounts.TryGetValue(key, out var count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> WatchedIntervals(string name)
    {
        var prefix = $"chart:{name.ToUpperInvariant()}:";
        var list = new List<string>();
        foreach (var key in WatchRefCounts.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !WatchRefCounts.TryGetValue(key, out var count)
                || count <= 0)
            {
                continue;
            }

            var interval = key[(prefix.Length)..];
            if (interval.Length > 0)
            {
                list.Add(interval);
            }
        }

        return list;
    }

    public async Task PublishAsync(ChartLiveUpdate update, CancellationToken ct = default)
    {
        var group = MarketChartHub.GroupKey(update.Name, update.Interval);
        if (!WatchRefCounts.TryGetValue(group, out var count) || count <= 0)
        {
            return;
        }

        try
        {
            await _hub.Clients.Group(group).SendAsync(MarketChartHub.LiveEvent, update, ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Chart live push failed for {Group}", group);
        }
    }
}
