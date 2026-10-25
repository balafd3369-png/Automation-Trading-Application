using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TradeWithBeast.Services;

namespace TradeWithBeast.Broker;

public sealed class MStockAuthClient : IBrokerMarketClient
{
    public static readonly Dictionary<string, string[]> QuoteKeys = IndexUniverse.QuoteKeys;

    public string ProviderId => BrokerIds.MStock;
    public string DisplayName => "m.Stock";

    private readonly HttpClient _http;
    private readonly MStockOptions _options;
    private readonly MStockSessionStore _sessions;
    private readonly MStockLoginContext _login;
    private readonly ILogger<MStockAuthClient> _log;
    private readonly object _gate = new();
    private string _accessToken = "";

    public MStockAuthClient(
        IHttpClientFactory httpFactory,
        MStockOptions options,
        MStockSessionStore sessions,
        MStockLoginContext login,
        ILogger<MStockAuthClient> log)
    {
        _http = httpFactory.CreateClient("mstock");
        _options = options;
        _sessions = sessions;
        _login = login;
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
        _login.Clear();
        _log.LogWarning("m.Stock session cleared. Re-verify OTP for live quotes.");
    }

    public bool IsApiKeyConfigured() => !string.IsNullOrWhiteSpace(_login.ResolveApiKey(_options));

    public Task<JsonElement> SendOtpAsync(CancellationToken ct) =>
        SendOtpAsync(null, null, null, ct);

    public async Task<JsonElement> SendOtpAsync(string? username, string? password, string? apiKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _login.SetApiKeyOverride(apiKey);
        }

        var user = FirstNonEmpty(username?.Trim(), _options.Username);
        var pass = FirstNonEmpty(password, _options.Password);
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            throw new InvalidOperationException(
                "m.Stock user ID and password are required. Enter them in the Broker panel or set MStock:Username and MStock:Password in server config.");
        }

        var resolvedApiKey = _login.ResolveApiKey(_options);
        if (string.IsNullOrWhiteSpace(resolvedApiKey))
        {
            throw new InvalidOperationException(
                "m.Stock Type A API key is missing. Generate one at trade.mstock.com → API, then set MStock:ApiKey in backend user secrets or paste it under One-time setup in the Broker panel.");
        }

        _log.LogInformation("m.Stock login OTP request for user {Username}.", user);

        using var req = new HttpRequestMessage(HttpMethod.Post, "openapi/typea/connect/login");
        ApplyCommonHeaders(req);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = user,
            ["password"] = pass,
        });
        var result = await SendJson(req, ct);
        if (!MStockJson.IsOtpSendAcknowledged(result))
        {
            var message = MStockJson.ReadMessage(result) ?? "login failed";
            _log.LogWarning("m.Stock login did not send OTP: {Message}", message);
        }

        return result;
    }

    public Task<JsonElement> VerifyOtpAsync(string otp, CancellationToken ct) =>
        VerifyOtpAsync(otp, null, ct);

    public async Task<JsonElement> VerifyOtpAsync(string otp, string? apiKey, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _login.SetApiKeyOverride(apiKey);
        }

        var last = await ExchangeOtpForSessionAsync(otp, "L", ct);
        if (TryReadAccessToken(last, out var token))
        {
            StoreSessionToken(token, "L");
            return last;
        }

        var message = MStockJson.ReadMessage(last) ?? "";
        if (message.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("m.Stock OTP verify rejected: {Message}", message);
            return last;
        }

        last = await ExchangeOtpForSessionAsync(otp, "W", ct);
        if (TryReadAccessToken(last, out token))
        {
            StoreSessionToken(token, "W");
            return last;
        }

        message = MStockJson.ReadMessage(last) ?? "no token";
        _log.LogWarning("m.Stock OTP verify did not store a session: {Message}", message);
        return last;
    }

    private async Task<JsonElement> ExchangeOtpForSessionAsync(string otp, string checksum, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "openapi/typea/session/token");
        ApplyCommonHeaders(req);
        var apiKey = _login.ResolveApiKey(_options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("m.Stock API key is missing. Request OTP again after entering your API key.");
        }

        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = apiKey,
            ["request_token"] = otp.Trim(),
            ["checksum"] = checksum,
        });
        return await SendJson(req, ct);
    }

    private void StoreSessionToken(string token, string checksum)
    {
        lock (_gate)
        {
            _accessToken = token;
        }

        _sessions.SaveToken(token);
        _log.LogInformation("m.Stock session stored locally (checksum {Checksum}).", checksum);
    }

    public async Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct) =>
        await FetchQuoteBatchesAsync("openapi/typea/instruments/quote/ohlc?", ParseQuotes, ct);

    public async Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct) =>
        await FetchQuoteBatchesAsync("openapi/typea/instruments/quote/ltp?", ParseLtpQuotes, ct);

    public async Task<IReadOnlyDictionary<string, decimal>> GetLtpForBoardNamesAsync(
        IReadOnlyList<string> boardNames,
        CancellationToken ct)
    {
        if (boardNames.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var keys = boardNames
            .SelectMany(name => QuoteKeys.TryGetValue(name, out var aliases) ? aliases : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        return await FetchQuoteBatchesAsync("openapi/typea/instruments/quote/ltp?", ParseLtpQuotes, ct, keys);
    }

    public async Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct)
    {
        if (!IndexUniverse.TryRegisterEquity(boardName, out var aliases))
        {
            return null;
        }

        var query = string.Join("&", aliases.Select(key => "i=" + Uri.EscapeDataString(key)));
        using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ohlc?" + query);
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryReadBoardQuote(data, aliases);
    }

    private async Task<IReadOnlyDictionary<string, TValue>> FetchQuoteBatchesAsync<TValue>(
        string pathPrefix,
        Func<JsonElement, Dictionary<string, TValue>> parse,
        CancellationToken ct,
        IEnumerable<string>? instrumentKeys = null)
    {
        var merged = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        var keys = (instrumentKeys ?? QuoteKeys.SelectMany(kv => kv.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var chunks = keys.Chunk(12).ToArray();
        await Parallel.ForEachAsync(
            chunks,
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
            async (chunk, token) =>
            {
                var query = string.Join("&", chunk.Select(key => "i=" + Uri.EscapeDataString(key)));
                using var req = new HttpRequestMessage(HttpMethod.Get, pathPrefix + query);
                ApplyCommonHeaders(req);
                ApplyAuth(req);
                var root = await SendJson(req, token);
                foreach (var kv in parse(root))
                {
                    lock (merged)
                    {
                        merged[kv.Key] = kv.Value;
                    }
                }
            });

        if (merged.Count == 0)
        {
            _log.LogWarning("m.Stock quote batch returned no mapped symbols for {Path}.", pathPrefix);
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

        if (!TryReadOptionOhlc(data, aliases, out var matchedAlias, out var snap))
        {
            _log.LogWarning(
                "No m.Stock option quote for {Underlying} {Expiry:yyyy-MM-dd} {Strike} {Side}; tried {Aliases}",
                underlying,
                expiry,
                strike,
                side,
                string.Join(", ", aliases));
            return null;
        }

        return snap with { TradingSymbol = matchedAlias };
    }

    private const int OptionQuoteInstrumentsPerRequest = 12;
    private static readonly TimeSpan OptionQuoteBatchGap = TimeSpan.FromMilliseconds(75);

    public async Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(
        IReadOnlyList<OptionQuoteRequest> contracts,
        CancellationToken ct)
    {
        if (contracts.Count == 0)
        {
            return [];
        }

        var rows = contracts
            .Select(contract => new OptionQuoteRow(
                contract,
                OptionSymbols.QuoteKeys(
                    contract.Underlying,
                    contract.Expiry,
                    contract.Strike,
                    contract.Side)))
            .ToList();

        var filled = new Dictionary<(decimal Strike, string Side), OptionMarketQuote>();
        await FetchOptionQuotePassAsync(rows, primaryAliasOnly: true, filled, ct);
        var missing = rows
            .Where(row => !filled.ContainsKey(QuoteKey(row.Contract)))
            .ToList();
        if (missing.Count > 0)
        {
            await FetchOptionQuotePassAsync(missing, primaryAliasOnly: false, filled, ct);
        }

        return filled.Values
            .OrderBy(quote => quote.Strike)
            .ThenBy(quote => quote.Side, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (decimal Strike, string Side) QuoteKey(OptionQuoteRequest contract) =>
        (contract.Strike, contract.Side.ToUpperInvariant());

    private sealed record OptionQuoteRow(OptionQuoteRequest Contract, IReadOnlyList<string> Aliases);

    private async Task FetchOptionQuotePassAsync(
        IReadOnlyList<OptionQuoteRow> rows,
        bool primaryAliasOnly,
        Dictionary<(decimal Strike, string Side), OptionMarketQuote> filled,
        CancellationToken ct)
    {
        var contractsPerChunk = primaryAliasOnly ? OptionQuoteInstrumentsPerRequest : 4;
        var batch = 0;
        foreach (var chunk in rows.Chunk(contractsPerChunk))
        {
            if (batch++ > 0)
            {
                await Task.Delay(OptionQuoteBatchGap, ct);
            }

            var aliasKeys = primaryAliasOnly
                ? chunk.Select(row => row.Aliases[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : chunk.SelectMany(row => row.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (aliasKeys.Count == 0)
            {
                continue;
            }

            JsonElement? data;
            try
            {
                data = await FetchOptionOhlcDataAsync(aliasKeys, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(
                    ex,
                    "Option OHLC batch failed ({AliasMode}, {Contracts} contracts)",
                    primaryAliasOnly ? "primary" : "all-aliases",
                    chunk.Length);
                continue;
            }

            if (data is null)
            {
                continue;
            }

            foreach (var row in chunk)
            {
                var key = QuoteKey(row.Contract);
                if (filled.ContainsKey(key))
                {
                    continue;
                }

                if (!TryReadOptionOhlc(data.Value, row.Aliases, out var matchedAlias, out var snap))
                {
                    continue;
                }

                filled[key] = new OptionMarketQuote(
                    row.Contract.Strike,
                    key.Side,
                    matchedAlias,
                    snap.Last,
                    snap.Open,
                    snap.Close,
                    snap.Bid,
                    snap.Ask,
                    snap.Volume,
                    snap.OpenInterest,
                    snap.Timestamp);
            }
        }
    }

    private async Task<JsonElement?> FetchOptionOhlcDataAsync(IReadOnlyList<string> aliasKeys, CancellationToken ct)
    {
        var query = string.Join("&", aliasKeys.Select(key => "i=" + Uri.EscapeDataString(key)));
        using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/instruments/quote/ohlc?" + query);
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return data.Clone();
    }

    public async Task<FundSummary?> GetFundSummaryAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "openapi/typea/user/fundsummary");
        ApplyCommonHeaders(req);
        ApplyAuth(req);
        var root = await SendJson(req, ct);
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }
        var row = data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().FirstOrDefault()
            : data;
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        static decimal Field(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) ? ToDec(value) : 0m;
        return new FundSummary(
            Field(row, "AVAILABLE_BALANCE"),
            Field(row, "CLEAR_BALANCE"),
            Field(row, "AMOUNT_UTILIZED"),
            Field(row, "COLLATERALS"),
            Field(row, "REALISED_PROFITS"));
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
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning(
                "m.Stock HTTP {Status} for {Method} {Path}: {Body}",
                (int)res.StatusCode,
                req.Method,
                req.RequestUri?.PathAndQuery ?? "?",
                json.Length > 500 ? json[..500] + "…" : json);
        }

        var payload = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _log.LogWarning(
                ex,
                "m.Stock returned non-JSON for {Method} {Path} (HTTP {Status}): {Body}",
                req.Method,
                req.RequestUri?.PathAndQuery ?? "?",
                (int)res.StatusCode,
                payload.Length > 500 ? payload[..500] + "…" : payload);
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private void ApplyCommonHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("X-Mirae-Version", "1");
        var apiKey = _login.ResolveApiKey(_options);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.TryAddWithoutValidation("X-PrivateKey", apiKey);
        }
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        string token;
        lock (_gate)
        {
            token = _accessToken;
        }

        var apiKey = _login.ResolveApiKey(_options);
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{token}");
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

    private static QuoteSnap? TryReadBoardQuote(JsonElement data, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (!TryGetProp(data, alias, out var item))
            {
                continue;
            }

            if (!TryParseQuoteItem(item, alias.Split(':')[0], out var snap))
            {
                continue;
            }

            return snap;
        }

        return null;
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

                if (!TryParseQuoteItem(item, alias.Split(':')[0], out var snap))
                {
                    continue;
                }

                map[kv.Key] = snap;
                break;
            }
        }

        MergeQuotesFromInstrumentKeys(data, map);
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

                if (!TryParseQuoteItem(item, alias.Split(':')[0], out var snap) || snap.Last <= 0)
                {
                    continue;
                }

                map[kv.Key] = snap.Last;
                break;
            }
        }

        var ohlcFallback = new Dictionary<string, QuoteSnap>(StringComparer.OrdinalIgnoreCase);
        MergeQuotesFromInstrumentKeys(data, ohlcFallback);
        foreach (var (boardName, snap) in ohlcFallback)
        {
            if (snap.Last > 0 && !map.ContainsKey(boardName))
            {
                map[boardName] = snap.Last;
            }
        }

        return map;
    }

    private static void MergeQuotesFromInstrumentKeys(
        JsonElement data,
        Dictionary<string, QuoteSnap> map)
    {
        foreach (var prop in data.EnumerateObject())
        {
            if (!IndexUniverse.TryResolveBoardFromInstrumentKey(prop.Name, out var boardName)
                || map.ContainsKey(boardName))
            {
                continue;
            }

            var exchange = prop.Name.Contains(':')
                ? prop.Name.Split(':')[0]
                : "NSE";
            if (!TryParseQuoteItem(prop.Value, exchange, out var snap))
            {
                continue;
            }

            map[boardName] = snap;
        }
    }

    private static bool TryParseQuoteItem(JsonElement item, string exchange, out QuoteSnap snap)
    {
        snap = default!;
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

        if (last <= 0 && close > 0)
        {
            last = close;
        }

        if (last <= 0 && open > 0)
        {
            last = open;
        }

        if (last <= 0 && token <= 0)
        {
            return false;
        }

        var liquidity = ParseLiquidity(item);
        snap = new QuoteSnap(
            token,
            last,
            open,
            close,
            exchange,
            liquidity.Bid,
            liquidity.Ask,
            liquidity.Volume,
            liquidity.OpenInterest,
            liquidity.Timestamp);
        return true;
    }

    private static bool TryReadOptionOhlc(
        JsonElement data,
        IReadOnlyList<string> aliases,
        out string matchedAlias,
        out OptionQuoteSnap snap)
    {
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

            if (last <= 0 && token <= 0)
            {
                continue;
            }

            var liquidity = ParseLiquidity(item);
            matchedAlias = alias;
            snap = new OptionQuoteSnap(
                alias,
                token,
                last,
                open,
                close,
                alias.Split(':')[0],
                liquidity.Bid,
                liquidity.Ask,
                liquidity.Volume,
                liquidity.OpenInterest,
                liquidity.Timestamp);
            return true;
        }

        matchedAlias = string.Empty;
        snap = default!;
        return false;
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

    private static (decimal? Bid, decimal? Ask, long? Volume, long? OpenInterest, DateTimeOffset? Timestamp)
        ParseLiquidity(JsonElement item)
    {
        decimal? bid = ReadPositive(item, "bid_price", "best_bid_price", "buy_price");
        decimal? ask = ReadPositive(item, "ask_price", "best_ask_price", "sell_price");
        if (item.TryGetProperty("depth", out var depth) && depth.ValueKind == JsonValueKind.Object)
        {
            bid ??= FirstDepthPrice(depth, "buy", "bids");
            ask ??= FirstDepthPrice(depth, "sell", "asks");
        }

        var volume = ReadNonNegativeLong(item, "volume", "volume_traded", "total_traded_volume");
        var oi = ReadNonNegativeLong(item, "oi", "open_interest");
        DateTimeOffset? timestamp = null;
        foreach (var name in new[] { "timestamp", "last_trade_time", "exchange_timestamp" })
        {
            if (item.TryGetProperty(name, out var value)
                && DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                timestamp = parsed;
                break;
            }
        }
        return (bid, ask, volume, oi, timestamp);
    }

    private static decimal? ReadPositive(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && ToDec(value) is var number && number > 0)
            {
                return number;
            }
        }
        return null;
    }

    private static long? ReadNonNegativeLong(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value))
            {
                var number = ToDec(value);
                if (number >= 0)
                {
                    return (long)number;
                }
            }
        }
        return null;
    }

    private static decimal? FirstDepthPrice(JsonElement depth, params string[] names)
    {
        foreach (var name in names)
        {
            if (!depth.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var first = rows.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                return ReadPositive(first, "price");
            }
        }
        return null;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}

public sealed record QuoteSnap(
    int InstrumentToken,
    decimal Last,
    decimal Open,
    decimal Close,
    string Exchange,
    decimal? Bid = null,
    decimal? Ask = null,
    long? Volume = null,
    long? OpenInterest = null,
    DateTimeOffset? Timestamp = null);

public sealed record OptionQuoteSnap(
    string TradingSymbol,
    int InstrumentToken,
    decimal Last,
    decimal Open,
    decimal Close,
    string Exchange,
    decimal? Bid = null,
    decimal? Ask = null,
    long? Volume = null,
    long? OpenInterest = null,
    DateTimeOffset? Timestamp = null);

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
    decimal PreviousClose,
    decimal? Bid = null,
    decimal? Ask = null,
    long? Volume = null,
    long? OpenInterest = null,
    DateTimeOffset? Timestamp = null);

public sealed record FundSummary(
    decimal AvailableBalance,
    decimal ClearBalance,
    decimal AmountUtilized,
    decimal Collaterals,
    decimal RealisedProfits);

public sealed record DayCandle(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close);
