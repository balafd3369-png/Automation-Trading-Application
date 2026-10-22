using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

internal static class HistoricalChartLoader
{
    /// <summary>Loads intraday candles in chunks (broker APIs often cap per request).</summary>
    public static async Task<IReadOnlyList<DayCandle>> LoadChunkedAsync(
        IBrokerMarketClient broker,
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset fromIst,
        DateTimeOffset toIst,
        int chunkCalendarDays,
        CancellationToken ct)
    {
        if (!broker.HasSession || instrumentToken == 0)
        {
            return [];
        }

        var merged = new Dictionary<long, DayCandle>();
        var cursor = fromIst;
        while (cursor < toIst)
        {
            var chunkEnd = cursor.AddDays(chunkCalendarDays);
            if (chunkEnd > toIst)
            {
                chunkEnd = toIst;
            }

            var chunk = await FetchChunkAsync(broker, null, exchange, instrumentToken, interval, cursor, chunkEnd, ct);
            foreach (var candle in chunk)
            {
                merged[candle.Time.ToUnixTimeSeconds()] = candle;
            }

            if (chunkEnd <= cursor)
            {
                break;
            }

            cursor = chunkEnd;
        }

        return merged.Values.OrderBy(c => c.Time).ToList();
    }

    public static Task<IReadOnlyList<DayCandle>> LoadChunkedForBoardAsync(
        IBrokerMarketClient broker,
        string boardName,
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset fromIst,
        DateTimeOffset toIst,
        int chunkCalendarDays,
        CancellationToken ct)
    {
        if (!broker.HasSession || string.IsNullOrWhiteSpace(boardName))
        {
            return Task.FromResult<IReadOnlyList<DayCandle>>([]);
        }

        return LoadChunkedCoreAsync(
            broker,
            boardName,
            exchange,
            instrumentToken,
            interval,
            fromIst,
            toIst,
            chunkCalendarDays,
            ct);
    }

    private static async Task<IReadOnlyList<DayCandle>> LoadChunkedCoreAsync(
        IBrokerMarketClient broker,
        string? boardName,
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset fromIst,
        DateTimeOffset toIst,
        int chunkCalendarDays,
        CancellationToken ct)
    {
        var merged = new Dictionary<long, DayCandle>();
        var cursor = fromIst;
        while (cursor < toIst)
        {
            var chunkEnd = cursor.AddDays(chunkCalendarDays);
            if (chunkEnd > toIst)
            {
                chunkEnd = toIst;
            }

            var chunk = await FetchChunkAsync(
                broker,
                boardName,
                exchange,
                instrumentToken,
                interval,
                cursor,
                chunkEnd,
                ct);
            foreach (var candle in chunk)
            {
                merged[candle.Time.ToUnixTimeSeconds()] = candle;
            }

            if (chunkEnd <= cursor)
            {
                break;
            }

            cursor = chunkEnd;
        }

        return merged.Values.OrderBy(c => c.Time).ToList();
    }

    private static Task<IReadOnlyList<DayCandle>> FetchChunkAsync(
        IBrokerMarketClient broker,
        string? boardName,
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset fromIst,
        DateTimeOffset toIst,
        CancellationToken ct)
    {
        return broker.GetCandlesAsync(exchange, instrumentToken, interval, fromIst, toIst, ct);
    }
}
