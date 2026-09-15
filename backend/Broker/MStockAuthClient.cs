using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TradeWithBeast.Services;

namespace TradeWithBeast.Broker;

public sealed class MStockAuthClient
{
    public static readonly Dictionary<string, string[]> QuoteKeys = IndexUniverse.QuoteKeys;

    private readonly HttpClient _http;
    private readonly MStockOptions _options;
    private readonly MStockSessionStore _sessions;
    private readonly ILogger<MStockAuthClient> _log;
    private readonly object _gate = new();
    private string _accessToken = "";

    public MStockAuthClient(
        IHttpClientFactory httpFactory,
        MStockOptions options,
        MStockSessionStore sessions,
        ILogger<MStockAuthClient> log)
    {
        _http = httpFactory.CreateClient("mstock");
        _options = options;
        _sessions = sessions;
        _log = log;
        _http.BaseAddress = new Uri(options.RestBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _accessToken = FirstNonEmpty(options.AccessToken, sessions.ReadToken());
    }

    public bool HasSession
    {
        get
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(_accessToken))
                {
                    _accessToken = _sessions.ReadToken() ?? "";
                }

                return !string.IsNullOrWhiteSpace(_accessToken);
            }
        }
    }

    public void ClearSession()
    {
        lock (_gate)
        {
            _accessToken = "";
        }

        _sessions.ClearToken();
        _log.LogWarning("m.Stock session cleared. Re-verify OTP for live quotes.");
    }

    public async Task<JsonElement> SendOtpAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "openapi/typea/connect/login");
        ApplyCommonHeaders(req);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _options.Username,
            ["password"] = _options.Password,
        });
        return await SendJson(req, ct);
    }

    public async Task<JsonElement> VerifyOtpAsync(string otp, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "openapi/typea/session/token");
        ApplyCommonHeaders(req);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = _options.ApiKey,
            ["request_token"] = otp.Trim(),
            ["checksum"] = "L",
        });

        var root = await SendJson(req, ct);
        if (TryReadAccessToken(root, out var token))
        {
            lock (_gate)
            {
                _accessToken = token;
            }

            _sessions.SaveToken(token);
            _log.LogInformation("m.Stock session stored locally.");
        }
        else
        {
            var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "no token";
            _log.LogWarning("m.Stock OTP verify did not store a session: {Message}", message);
        }

        return root;
    }

    public async Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, QuoteSnap>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in QuoteKeys.SelectMany(kv => kv.Value).Distinct().Chunk(12))
        {
            var query = string.Join("&", chunk.Select(key => "i=" + Uri.EscapeDataString(key)));
            using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ohlc?" + query);
            ApplyCommonHeaders(req);
            ApplyAuth(req);
            var root = await SendJson(req, ct);
            foreach (var kv in ParseQuotes(root))
            {
                merged[kv.Key] = kv.Value;
            }
        }

        if (merged.Count == 0)
        {
            _log.LogWarning("m.Stock OHLC empty after batched requests.");
        }

        return merged;
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in QuoteKeys.SelectMany(kv => kv.Value).Distinct().Chunk(12))
        {
            var query = string.Join("&", chunk.Select(key => "i=" + Uri.EscapeDataString(key)));
            using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ltp?" + query);
            ApplyCommonHeaders(req);
            ApplyAuth(req);
            var root = await SendJson(req, ct);
            foreach (var kv in ParseLtpQuotes(root))
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    public async Task<OptionQuoteSnap?> GetOptionQuoteAsync(
        string underlying,
        DateTime expiry,
        decimal strike,
        string side,
        CancellationToken ct)
    {
        var aliases = OptionSymbols.QuoteKeys(underlying, expiry, strike, side);
        var query = string.Join("&", aliases.Select(key => "i=" + Uri.EscapeDataString(key)));
        using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ohlc?" + query);
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var alias in aliases)
        {
            if (!TryGetProp(data, alias, out var item))
            {
                continue;
            }
            var last = item.TryGetProperty("last_price", out var lp) ? ToDec(lp) : 0m;
            var token = item.TryGetProperty("instrument_token", out var it) ? (int)ToDec(it) : 0;
            decimal open = 0;
            decimal close = 0;
            if (item.TryGetProperty("ohlc", out var ohlc))
            {
                open = ohlc.TryGetProperty("open", out var o) ? ToDec(o) : 0m;
                close = ohlc.TryGetProperty("close", out var c) ? ToDec(c) : 0m;
            }
            if (last > 0 || token > 0)
            {
                return new OptionQuoteSnap(alias, token, last, open, close, alias.Split(':')[0]);
            }
        }

        _log.LogWarning(
            "No m.Stock option quote for {Underlying} {Expiry:yyyy-MM-dd} {Strike} {Side}; tried {Aliases}",
            underlying,
            expiry,
            strike,
            side,
            string.Join(", ", aliases));
        return null;
    }

    public async Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(
        IReadOnlyList<OptionQuoteRequest> contracts,
        CancellationToken ct)
    {
        var requested = contracts
            .Select(contract => new
            {
                Contract = contract,
                Symbol = OptionSymbols.QuoteKeys(
                    contract.Underlying,
                    contract.Expiry,
                    contract.Strike,
                    contract.Side)[0],
            })
            .ToList();
        var quotes = new List<OptionMarketQuote>();
        foreach (var chunk in requested.Chunk(12))
        {
            var query = string.Join("&", chunk.Select(row => "i=" + Uri.EscapeDataString(row.Symbol)));
            using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ohlc?" + query);
            ApplyCommonHeaders(req);
            ApplyAuth(req);
            var root = await SendJson(req, ct);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var row in chunk)
            {
                if (!TryGetProp(data, row.Symbol, out var item))
                {
                    continue;
                }
                var last = item.TryGetProperty("last_price", out var lp) ? ToDec(lp) : 0m;
                decimal open = 0;
                decimal close = 0;
                if (item.TryGetProperty("ohlc", out var ohlc))
                {
                    open = ohlc.TryGetProperty("open", out var o) ? ToDec(o) : 0m;
                    close = ohlc.TryGetProperty("close", out var c) ? ToDec(c) : 0m;
                }
                if (last > 0)
                {
                    quotes.Add(new OptionMarketQuote(
                        row.Contract.Strike,
                        row.Contract.Side.ToUpperInvariant(),
                        row.Symbol,
                        last,
                        open,
                        close));
                }
            }
        }
        return quotes;
    }

    public async Task<IReadOnlyList<DayCandle>> GetDayCandlesAsync(string exchange, int instrumentToken, CancellationToken ct) =>
        await GetCandlesAsync(exchange, instrumentToken, "day", ct);

    public async Task<IReadOnlyList<DayCandle>> GetIntradayCandlesAsync(
        string exchange,
        int instrumentToken,
        string interval,
        CancellationToken ct)
    {
        var exchangeId = exchange.ToUpperInvariant() switch
        {
            "NSE" => 1,
            "NFO" => 2,
            "CDS" => 3,
            "BSE" => 4,
            "BFO" => 5,
            _ => 1,
        };
        var path = $"openapi/typea/instruments/intraday/{exchangeId}/{instrumentToken}/{interval}";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        var candles = new List<DayCandle>();
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("candles", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "no candle data";
            _log.LogWarning(
                "No intraday candles for {Exchange}/{Token}/{Interval}: {Message}",
                exchange,
                instrumentToken,
                interval,
                message);
            return candles;
        }
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 5)
            {
                continue;
            }
            candles.Add(new DayCandle(
                ChartSessionMerger.ParseIst(row[0].GetString() ?? DateTimeOffset.Now.ToString("O")),
                ToDec(row[1]),
                ToDec(row[2]),
                ToDec(row[3]),
                ToDec(row[4])));
        }
        return candles.OrderBy(candle => candle.Time).ToList();
    }

    public async Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) =>
        await GetCandlesAsync(exchange, instrumentToken, interval, null, null, ct);

    public async Task<IReadOnlyList<DayCandle>> GetCandlesAsync(
        string exchange,
        int instrumentToken,
        string interval,
        DateTimeOffset? fromIst,
        DateTimeOffset? toIst,
        CancellationToken ct)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
        var lookbackDays = interval is "day" or "week" ? 80 : interval.Contains("60") ? 20 : interval.Contains("15") ? 10 : interval.Contains("5") ? 8 : 3;
        var fromLocal = fromIst.HasValue ? TimeZoneInfo.ConvertTime(fromIst.Value, ist) : now.Date.AddDays(-lookbackDays).AddHours(9).AddMinutes(15);
        var toLocal = toIst.HasValue ? TimeZoneInfo.ConvertTime(toIst.Value, ist) : now.Date.AddDays(1).AddHours(15).AddMinutes(30);
        var from = fromLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var to = toLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var segment = string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange;
        var path =
            $"openapi/typea/instruments/historical/{segment}/{instrumentToken}/{interval}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        var candles = new List<DayCandle>();
        if (!root.TryGetProperty("data", out var data))
        {
            return candles;
        }

        if (!data.TryGetProperty("candles", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            var status = root.TryGetProperty("status", out var st) ? st.GetString() : "?";
            var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "";
            _log.LogWarning("No candles array for {Exchange}/{Token}/{Interval}: {Status} {Message}", segment, instrumentToken, interval, status, message);
            return candles;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() < 5)
            {
                continue;
            }

            candles.Add(new DayCandle(
                ChartSessionMerger.ParseIst(row[0].GetString() ?? from),
                ToDec(row[1]),
                ToDec(row[2]),
                ToDec(row[3]),
                ToDec(row[4])));
        }

        return candles.OrderBy(c => c.Time).ToList();
    }

    private async Task<JsonElement> SendJson(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            ClearSession();
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    private void ApplyCommonHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("X-Mirae-Version", "1");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            req.Headers.TryAddWithoutValidation("X-PrivateKey", _options.ApiKey);
        }
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        string token;
        lock (_gate)
        {
            token = _accessToken;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.TryAddWithoutValidation("Authorization", $"token {_options.ApiKey}:{token}");
        }
    }

    private static bool TryReadAccessToken(JsonElement root, out string token)
    {
        token = "";
        if (!root.TryGetProperty("data", out var data))
        {
            return false;
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("access_token", out var value))
        {
            token = value.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(token);
        }

        return false;
    }

    private static Dictionary<string, QuoteSnap> ParseQuotes(JsonElement root)
    {
        var map = new Dictionary<string, QuoteSnap>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var kv in QuoteKeys)
        {
            foreach (var alias in kv.Value)
            {
                if (!TryGetProp(data, alias, out var item))
                {
                    continue;
                }

                var last = item.TryGetProperty("last_price", out var lp) ? ToDec(lp) : 0m;
                var token = item.TryGetProperty("instrument_token", out var it) ? (int)ToDec(it) : 0;
                decimal open = 0;
                decimal close = 0;
                if (item.TryGetProperty("ohlc", out var ohlc))
                {
                    if (ohlc.TryGetProperty("open", out var o))
                    {
                        open = ToDec(o);
                    }

                    if (ohlc.TryGetProperty("close", out var c))
                    {
                        close = ToDec(c);
                    }
                }

                if (last <= 0 && token <= 0)
                {
                    continue;
                }

                map[kv.Key] = new QuoteSnap(token, last, open, close, alias.Split(':')[0]);
                break;
            }
        }

        return map;
    }

    private static Dictionary<string, decimal> ParseLtpQuotes(JsonElement root)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var kv in QuoteKeys)
        {
            foreach (var alias in kv.Value)
            {
                if (!TryGetProp(data, alias, out var item))
                {
                    continue;
                }

                var last = item.TryGetProperty("last_price", out var lp) ? ToDec(lp) : 0m;
                if (last <= 0)
                {
                    continue;
                }

                map[kv.Key] = last;
                break;
            }
        }

        return map;
    }

    private static bool TryGetProp(JsonElement data, string name, out JsonElement value)
    {
        foreach (var prop in data.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static decimal ToDec(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDecimal(out var d) ? d : Convert.ToDecimal(el.GetDouble()),
            JsonValueKind.String => decimal.TryParse(el.GetString(), out var parsed) ? parsed : 0m,
            _ => 0m,
        };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

public sealed record QuoteSnap(int InstrumentToken, decimal Last, decimal Open, decimal Close, string Exchange);

public sealed record OptionQuoteSnap(
    string TradingSymbol,
    int InstrumentToken,
    decimal Last,
    decimal Open,
    decimal Close,
    string Exchange);

public sealed record OptionQuoteRequest(
    string Underlying,
    DateTime Expiry,
    decimal Strike,
    string Side);

public sealed record OptionMarketQuote(
    decimal Strike,
    string Side,
    string TradingSymbol,
    decimal Last,
    decimal Open,
    decimal PreviousClose);

public sealed record DayCandle(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close);
