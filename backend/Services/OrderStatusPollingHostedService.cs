using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class OrderStatusPollingHostedService : BackgroundService
{
    private readonly TradingExecutionSafetyOptions _options;
    private readonly TradeHistoryStore _history;
    private readonly BrokerConnectionManager _brokers;
    private readonly TradingSafetyStore _safetyStore;
    private readonly ILogger<OrderStatusPollingHostedService> _log;
    private int _busy;

    public OrderStatusPollingHostedService(
        TradingExecutionSafetyOptions options,
        TradeHistoryStore history,
        BrokerConnectionManager brokers,
        TradingSafetyStore safetyStore,
        ILogger<OrderStatusPollingHostedService> log)
    {
        _options = options;
        _history = history;
        _brokers = brokers;
        _safetyStore = safetyStore;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.OrderStatusPollingEnabled)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(_options.OrderStatusPollingIntervalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 0)
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Order status polling cycle failed.");
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (!_brokers.HasSession)
        {
            return;
        }

        var brokerResult = await _brokers.Orders.GetOrdersAsync(ct);
        if (!brokerResult.Success || brokerResult.Data is null)
        {
            return;
        }

        var brokerBook = brokerResult.Data;
        foreach (var order in _history.ListNonTerminalLiveOrders())
        {
            if (string.IsNullOrWhiteSpace(order.BrokerOrderId))
            {
                continue;
            }

            PollLive(order, brokerBook);
        }

        foreach (var manual in _history.ListNonTerminalManualLiveOrders())
        {
            if (string.IsNullOrWhiteSpace(manual.BrokerOrderId))
            {
                continue;
            }

            PollManual(manual, brokerBook);
        }
    }

    private void PollLive(LiveOrderSnapshot order, IReadOnlyList<BrokerOrder> brokerBook)
    {
        var broker = brokerBook.FirstOrDefault(b => b.OrderId == order.BrokerOrderId);
        if (broker is null)
        {
            var unknown = order with
            {
                Status = "UNKNOWN",
                BrokerMessage = "Absent from broker book during poll.",
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _history.UpsertLiveOrder(unknown);
            _safetyStore.RecordPoll("live", order.Id, false, "broker missing");
            return;
        }

        var normalized = BrokerLiveOrderSettlement.NormalizeStatus(broker.Status);
        _history.UpsertLiveOrder(order with
        {
            Status = normalized,
            FilledQuantity = broker.FilledQuantity,
            AveragePrice = broker.AveragePrice,
            BrokerMessage = broker.StatusMessage ?? broker.Status,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _safetyStore.RecordPoll("live", order.Id, true, null);
    }

    private void PollManual(ManualOrderSnapshot manual, IReadOnlyList<BrokerOrder> brokerBook)
    {
        var broker = brokerBook.FirstOrDefault(b => b.OrderId == manual.BrokerOrderId);
        if (broker is null)
        {
            _history.UpsertManualOrder(manual with
            {
                Status = "UNKNOWN",
                Message = "Absent from broker book during poll.",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            _safetyStore.RecordPoll("manual", manual.Id, false, "broker missing");
            return;
        }

        var normalized = BrokerLiveOrderSettlement.NormalizeStatus(broker.Status);
        _history.UpsertManualOrder(manual with
        {
            Status = normalized,
            FilledQuantity = broker.FilledQuantity,
            AveragePrice = broker.AveragePrice,
            Message = broker.StatusMessage ?? broker.Status,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _safetyStore.RecordPoll("manual", manual.Id, true, null);
    }
}