using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TradeWithBeast.Broker;

public sealed record BrokerOrderRequest(
    string Exchange,
    string TradingSymbol,
    string TransactionType,
    int Quantity,
    string Product,
    string OrderType = "LIMIT",
    decimal Price = 0,
    decimal TriggerPrice = 0,
    string Validity = "DAY",
    string Variety = "regular",
    string Tag = "");

public sealed record BrokerOrder(
    string OrderId,
    string ExchangeOrderId,
    string Exchange,
    string TradingSymbol,
    string TransactionType,
    string Product,
    string OrderType,
    string Status,
    string StatusMessage,
    string StatusMessageRaw,
    int Quantity,
    int FilledQuantity,
    int PendingQuantity,
    int CancelledQuantity,
    decimal Price,
    decimal TriggerPrice,
    decimal AveragePrice,
    string Tag,
    DateTimeOffset? UpdatedAt);

public sealed record BrokerTrade(
    string TradeId,
    string OrderId,
    string Exchange,
    string TradingSymbol,
    string TransactionType,
    string Product,
    int Quantity,
    decimal AveragePrice,
    DateTimeOffset? TradedAt);

public sealed record BrokerPosition(
    string Exchange,
    string TradingSymbol,
    string Product,
    int Quantity,
    int OvernightQuantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal Pnl);

public sealed record BrokerMargin(decimal TotalMargin, decimal? Span, decimal? Exposure);

public sealed record BrokerApiResult<T>(
    bool Success,
    T? Data,
    string Message,
    string ErrorType = "",
    HttpStatusCode StatusCode = HttpStatusCode.OK)
{
    public static BrokerApiResult<T> Fail(string message, string type = "", HttpStatusCode code = HttpStatusCode.BadRequest) =>
        new(false, default, message, type, code);
}

public interface IMStockOrderClient
{
    Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct);
    Task<BrokerApiResult<string>> ModifyOrderAsync(string orderId, BrokerOrderRequest order, int remainingQuantity, CancellationToken ct);
    Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct);
    Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct);
    Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct);
    Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct);
    Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct);
    Task<BrokerApiResult<bool>> ConvertPositionAsync(
        string tradingSymbol, string exchange, string transactionType, int quantity,
        string oldProduct, string newProduct, CancellationToken ct);
}

public sealed class MStockOrderClient : IMStockOrderClient
{
    private readonly HttpClient _http;
    private readonly MStockOptions _options;
    private readonly MStockSessionStore _sessions;
    private readonly ILogger<MStockOrderClient> _log;

    public MStockOrderClient(
        IHttpClientFactory httpFactory,
        MStockOptions options,
        MStockSessionStore sessions,
        ILogger<MStockOrderClient> log)
    {
        _http = httpFactory.CreateClient("mstock");
        _http.BaseAddress = new Uri(options.RestBaseUrl.TrimEnd('/') + "/");
        _options = options;
        _sessions = sessions;
        _log = log;
    }

    public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"openapi/typea/orders/{order.Variety}", OrderFields(order), ReadOrderId, ct);

    public Task<BrokerApiResult<string>> ModifyOrderAsync(
        string orderId,
        BrokerOrderRequest order,
        int remainingQuantity,
        CancellationToken ct)
    {
        var fields = OrderFields(order);
        fields["modqty_remng"] = remainingQuantity.ToString(CultureInfo.InvariantCulture);
        return SendAsync(HttpMethod.Put, $"openapi/typea/orders/regular/{Uri.EscapeDataString(orderId)}", fields, ReadOrderId, ct);
    }

    public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"openapi/typea/orders/regular/{Uri.EscapeDataString(orderId)}", null, ReadOrderId, ct);

    public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "openapi/typea/orders", null, ReadOrders, ct);

    public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "openapi/typea/tradebook", null, ReadTrades, ct);

    public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "openapi/typea/portfolio/positions", null, ReadPositions, ct);

    public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Post,
            "openapi/typea/margins/orders",
            OrderFields(order),
            ReadMargin,
            ct,
            jsonContent: true);

    public Task<BrokerApiResult<bool>> ConvertPositionAsync(
        string tradingSymbol,
        string exchange,
        string transactionType,
        int quantity,
        string oldProduct,
        string newProduct,
        CancellationToken ct) =>
        SendAsync(
            HttpMethod.Post,
            "openapi/typea/portfolio/convertposition",
            new()
            {
                ["tradingsymbol"] = tradingSymbol,
                ["exchange"] = exchange,
                ["transaction_type"] = transactionType,
                ["position_type"] = "DAY",
                ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
                ["old_product"] = oldProduct,
                ["new_product"] = newProduct,
            },
            _ => true,
            ct);

    private async Task<BrokerApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        Dictionary<string, string>? fields,
        Func<JsonElement, T> read,
        CancellationToken ct,
        bool jsonContent = false)
    {
        var token = string.IsNullOrWhiteSpace(_options.AccessToken) ? _sessions.ReadToken() : _options.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return BrokerApiResult<T>.Fail("m.Stock session is not authenticated.", "TokenException", HttpStatusCode.Unauthorized);
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Mirae-Version", "1");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {_options.ApiKey}:{token}");
        if (fields is not null)
        {
            request.Content = jsonContent
                ? JsonContent.Create(fields)
                : new FormUrlEncodedContent(fields);
        }

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            var status = Text(root, "status");
            var message = First(Text(root, "message"), response.ReasonPhrase, "m.Stock request failed");
            if (!response.IsSuccessStatusCode || status.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                return BrokerApiResult<T>.Fail(message, Text(root, "error_type"), response.StatusCode);
            }
            try
            {
                return new(true, read(root), message, StatusCode: response.StatusCode);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to parse m.Stock response for {Path}", path);
                return BrokerApiResult<T>.Fail("m.Stock returned an unexpected response.", "ParseException", response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return BrokerApiResult<T>.Fail("m.Stock request timed out.", "TimeoutException", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "m.Stock order request failed for {Path}", path);
            return BrokerApiResult<T>.Fail(ex.Message, ex.GetType().Name, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static Dictionary<string, string> OrderFields(BrokerOrderRequest order) => new()
    {
        ["exchange"] = order.Exchange,
        ["tradingsymbol"] = order.TradingSymbol,
        ["transaction_type"] = order.TransactionType,
        ["quantity"] = order.Quantity.ToString(CultureInfo.InvariantCulture),
        ["product"] = order.Product,
        ["order_type"] = order.OrderType,
        ["price"] = order.Price.ToString(CultureInfo.InvariantCulture),
        ["trigger_price"] = order.TriggerPrice.ToString(CultureInfo.InvariantCulture),
        ["validity"] = order.Validity,
        ["variety"] = order.Variety,
        ["tag"] = order.Tag,
    };

    private static string ReadOrderId(JsonElement root) =>
        root.TryGetProperty("data", out var data) ? Text(data, "order_id") : "";

    private static IReadOnlyList<BrokerOrder> ReadOrders(JsonElement root)
    {
        var result = new List<BrokerOrder>();
        foreach (var item in DataArray(root))
        {
            result.Add(new(
                Text(item, "order_id"), Text(item, "exchange_order_id"), Text(item, "exchange"),
                Text(item, "tradingsymbol"), Text(item, "transaction_type"), Text(item, "product"),
                Text(item, "order_type"), Text(item, "status"), Text(item, "status_message"),
                Text(item, "status_message_raw"), Int(item, "quantity"), Int(item, "filled_quantity"),
                Int(item, "pending_quantity"), Int(item, "cancelled_quantity"), Number(item, "price"),
                Number(item, "trigger_price"), Number(item, "average_price"), Text(item, "tag"),
                Date(item, "exchange_update_timestamp", "exchange_timestamp", "order_timestamp")));
        }
        return result;
    }

    private static IReadOnlyList<BrokerTrade> ReadTrades(JsonElement root)
    {
        var result = new List<BrokerTrade>();
        foreach (var item in DataArray(root))
        {
            result.Add(new(
                First(Text(item, "trade_id"), Text(item, "TRADE_NUMBER")),
                First(Text(item, "order_id"), Text(item, "ORDER_NUMBER")),
                First(Text(item, "exchange"), Text(item, "EXCHANGE")),
                First(Text(item, "tradingsymbol"), Text(item, "FULL_SYMBOL")),
                First(Text(item, "transaction_type"), Text(item, "BUY_SELL")),
                First(Text(item, "product"), Text(item, "PRODUCT")),
                Math.Max(Int(item, "quantity"), Int(item, "QUANTITY")),
                Number(item, "average_price") is var price && price > 0 ? price : Number(item, "PRICE"),
                Date(item, "exchange_timestamp", "ORDER_DATE_TIME")));
        }
        return result;
    }

    private static IReadOnlyList<BrokerPosition> ReadPositions(JsonElement root)
    {
        var result = new List<BrokerPosition>();
        if (!root.TryGetProperty("data", out var data)) return result;
        IEnumerable<JsonElement> rows = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("net", out var net) && net.ValueKind == JsonValueKind.Array
                ? net.EnumerateArray()
                : data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : [];
        foreach (var item in rows)
        {
            result.Add(new(
                Text(item, "exchange"), Text(item, "tradingsymbol"), Text(item, "product"),
                Int(item, "quantity"), Int(item, "overnight_quantity"), Number(item, "average_price"),
                Number(item, "last_price"), Number(item, "pnl")));
        }
        return result;
    }

    private static BrokerMargin ReadMargin(JsonElement root)
    {
        var data = root.TryGetProperty("data", out var value) ? value : root;
        if (data.ValueKind == JsonValueKind.Array) data = data.EnumerateArray().FirstOrDefault();
        return new(
            FirstNumber(data, "total", "total_margin", "TOTAL_REQ_MARGIN", "required_margin"),
            OptionalNumber(data, "span", "span_margin"),
            OptionalNumber(data, "exposure", "exposure_margin"));
    }

    private static IEnumerable<JsonElement> DataArray(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return [];
        return data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : [];
    }

    private static string Text(JsonElement item, string name) =>
        TryProperty(item, name, out var value) ? value.ToString() : "";

    private static int Int(JsonElement item, string name) => (int)Number(item, name);
    private static decimal Number(JsonElement item, string name) =>
        TryProperty(item, name, out var value)
            && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number : 0;

    private static decimal FirstNumber(JsonElement item, params string[] names) =>
        names.Select(name => Number(item, name)).FirstOrDefault(value => value > 0);

    private static decimal? OptionalNumber(JsonElement item, params string[] names)
    {
        var value = FirstNumber(item, names);
        return value > 0 ? value : null;
    }

    private static DateTimeOffset? Date(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(item, name, out var value)
                && DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            {
                return date;
            }
        }
        return null;
    }

    private static bool TryProperty(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
