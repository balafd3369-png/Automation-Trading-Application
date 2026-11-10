using TradeWithBeast.Services;

namespace TradeWithBeast.Broker;

/// <summary>Records broker outcomes for the live-order circuit breaker.</summary>
public sealed class CircuitBreakerBrokerOrderClient : IBrokerOrderClient
{
    private readonly IBrokerOrderClient _inner;
    private readonly BrokerCircuitBreaker _breaker;

    public CircuitBreakerBrokerOrderClient(IBrokerOrderClient inner, BrokerCircuitBreaker breaker)
    {
        _inner = inner;
        _breaker = breaker;
    }

    public async Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct)
    {
        var result = await _inner.PlaceOrderAsync(order, ct);
        Track(result.Success, result.Message);
        return result;
    }

    public async Task<BrokerApiResult<string>> ModifyOrderAsync(
        string orderId,
        BrokerOrderRequest order,
        int remainingQuantity,
        CancellationToken ct)
    {
        var result = await _inner.ModifyOrderAsync(orderId, order, remainingQuantity, ct);
        Track(result.Success, result.Message);
        return result;
    }

    public async Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct)
    {
        var result = await _inner.CancelOrderAsync(orderId, ct);
        Track(result.Success, result.Message);
        return result;
    }

    public async Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct)
    {
        var result = await _inner.GetOrdersAsync(ct);
        if (result.Success)
        {
            _breaker.RecordHeartbeat();
        }

        return result;
    }

    public async Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct)
    {
        var result = await _inner.GetTradesAsync(ct);
        if (result.Success)
        {
            _breaker.RecordHeartbeat();
        }

        return result;
    }

    public async Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct)
    {
        var result = await _inner.GetPositionsAsync(ct);
        if (result.Success)
        {
            _breaker.RecordHeartbeat();
        }

        return result;
    }

    public async Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct)
    {
        var result = await _inner.CalculateMarginAsync(order, ct);
        if (result.Success)
        {
            _breaker.RecordHeartbeat();
        }

        return result;
    }

    public async Task<BrokerApiResult<bool>> ConvertPositionAsync(
        string tradingSymbol,
        string exchange,
        string transactionType,
        int quantity,
        string oldProduct,
        string newProduct,
        CancellationToken ct)
    {
        var result = await _inner.ConvertPositionAsync(
            tradingSymbol,
            exchange,
            transactionType,
            quantity,
            oldProduct,
            newProduct,
            ct);
        Track(result.Success, result.Message);
        return result;
    }

    private void Track(bool success, string message)
    {
        if (success)
        {
            _breaker.RecordBrokerSuccess();
        }
        else
        {
            _breaker.RecordBrokerFailure(message);
        }
    }
}
