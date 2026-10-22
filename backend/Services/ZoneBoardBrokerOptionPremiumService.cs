using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>
/// Spot zone signals stay on 5m; option entry/exit premiums use broker <b>15minute</b> closes when available.
/// </summary>
public sealed class ZoneBoardBrokerOptionPremiumService
{
    private const string Interval = "15minute";
    private readonly BrokerConnectionManager _brokers;
    private readonly ILogger<ZoneBoardBrokerOptionPremiumService> _log;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, IReadOnlyList<DayCandle>> _sessionCandles = new(StringComparer.Ordinal);

    public ZoneBoardBrokerOptionPremiumService(
        BrokerConnectionManager brokers,
        ILogger<ZoneBoardBrokerOptionPremiumService> log)
    {
        _brokers = brokers;
        _log = log;
    }

    public async Task<ZoneBoardOptionEstimator.TradeOptionEstimate> EstimateAsync(
        ZoneBoardPattern.ZoneBoardTradeJournalEntry trade,
        CancellationToken ct)
    {
        var plan = ZoneBoardOptionEstimator.PlanStrikes(trade);
        if (!_brokers.HasSession)
        {
            return ZoneBoardOptionEstimator.EstimateModeled(trade, plan);
        }

        var broker = _brokers.Market;
        var itm = await TryBrokerLegAsync(broker, trade, plan, plan.ItmStrike, "ITM", ct);
        var otm = await TryBrokerLegAsync(broker, trade, plan, plan.OtmStrike, "OTM", ct);

        var modeled = ZoneBoardOptionEstimator.EstimateModeled(trade, plan);
        itm ??= modeled.Itm;
        otm ??= modeled.Otm;

        var notes = itm.PremiumSource == ZoneBoardOptionEstimator.SourceBroker15M
            || otm.PremiumSource == ZoneBoardOptionEstimator.SourceBroker15M
            ? "Spot signal 5m; option premium from broker 15m close (nearest bar at entry/exit). Missing legs use MODELED."
            : modeled.Notes;

        return new ZoneBoardOptionEstimator.TradeOptionEstimate(
            plan.OptionRight,
            plan.ExpiryDate,
            plan.LotSize,
            plan.AtmStrike,
            itm,
            otm,
            notes);
    }

    private async Task<ZoneBoardOptionEstimator.OptionLegEstimate?> TryBrokerLegAsync(
        IBrokerMarketClient broker,
        ZoneBoardPattern.ZoneBoardTradeJournalEntry trade,
        ZoneBoardOptionEstimator.StrikePlan plan,
        decimal strike,
        string moneyness,
        CancellationToken ct)
    {
        try
        {
            var quote = await broker.GetOptionQuoteAsync(
                plan.Symbol,
                plan.ExpiryDate.ToDateTime(TimeOnly.MinValue),
                strike,
                plan.OptionRight,
                ct);
            if (quote is null || quote.InstrumentToken <= 0)
            {
                return null;
            }

            var entryPx = await PremiumAtAsync(broker, quote.Exchange, quote.InstrumentToken, trade.EntryTime, ct);
            var exitPx = await PremiumAtAsync(broker, quote.Exchange, quote.InstrumentToken, trade.ExitTime, ct);
            if (entryPx is null || exitPx is null || entryPx <= 0)
            {
                return null;
            }

            return ZoneBoardOptionEstimator.BuildBrokerLeg(
                moneyness,
                strike,
                entryPx.Value,
                exitPx.Value,
                plan.LotSize,
                ZoneBoardOptionEstimator.SourceBroker15M);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Broker 15m premium failed for {Symbol} {Strike} {Right}", plan.Symbol, strike, plan.OptionRight);
            return null;
        }
    }

    private async Task<decimal?> PremiumAtAsync(
        IBrokerMarketClient broker,
        string exchange,
        int token,
        DateTimeOffset timeIst,
        CancellationToken ct)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(timeIst, ist);
        var sessionDate = DateOnly.FromDateTime(local.DateTime);
        var candles = await LoadSessionCandlesAsync(broker, exchange, token, sessionDate, ct);
        if (candles.Count == 0)
        {
            return null;
        }

        DayCandle? match = null;
        foreach (var candle in candles.OrderBy(c => c.Time))
        {
            var candleIst = TimeZoneInfo.ConvertTime(candle.Time, ist);
            if (candleIst <= local)
            {
                match = candle;
                continue;
            }

            break;
        }

        return match?.Close;
    }

    private async Task<IReadOnlyList<DayCandle>> LoadSessionCandlesAsync(
        IBrokerMarketClient broker,
        string exchange,
        int token,
        DateOnly sessionDate,
        CancellationToken ct)
    {
        var cacheKey = $"{exchange}|{token}|{sessionDate:yyyy-MM-dd}|{Interval}";
        lock (_cacheGate)
        {
            if (_sessionCandles.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var day = sessionDate.ToDateTime(TimeOnly.MinValue);
        var fromIst = new DateTimeOffset(day.AddHours(9).AddMinutes(15), ist.GetUtcOffset(day.AddHours(9).AddMinutes(15)));
        var toIst = new DateTimeOffset(day.AddHours(15).AddMinutes(30), ist.GetUtcOffset(day.AddHours(15).AddMinutes(30)));

        var candles = await broker.GetCandlesAsync(exchange, token, Interval, fromIst, toIst, ct);
        if (candles.Count == 0)
        {
            candles = await HistoricalChartLoader.LoadChunkedAsync(
                broker,
                exchange,
                token,
                Interval,
                fromIst.AddDays(-2),
                toIst.AddDays(1),
                chunkCalendarDays: 3,
                ct);
            candles = candles
                .Where(c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.Time, ist).DateTime) == sessionDate)
                .OrderBy(c => c.Time)
                .ToList();
        }

        lock (_cacheGate)
        {
            _sessionCandles[cacheKey] = candles;
        }

        return candles;
    }
}
