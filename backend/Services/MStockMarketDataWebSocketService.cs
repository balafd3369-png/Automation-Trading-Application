using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Services;

/// <summary>
/// m.Stock Type A WebSocket → intraday ticks → board LTP + SignalR chart live updates.
/// </summary>
public sealed class MStockMarketDataWebSocketService : BackgroundService
{
    private static readonly TimeSpan SubscribeRefresh = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleReconnect = TimeSpan.FromSeconds(45);

    private readonly MStockAuthClient _auth;
    private readonly MockMarketService _market;
    private readonly MStockMarketDataWebSocketHealth _health;
    private readonly ILogger<MStockMarketDataWebSocketService> _log;

    public MStockMarketDataWebSocketService(
        MStockAuthClient auth,
        MockMarketService market,
        MStockMarketDataWebSocketHealth health,
        ILogger<MStockMarketDataWebSocketService> log)
    {
        _auth = auth;
        _market = market;
        _health = health;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_auth.HasSession || !NseCashSession.IsLiveIntradayWindowNow())
            {
                _health.Clear();
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            if (!_auth.TryBuildWebSocketUri(out var uri))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await RunConnectionAsync(uri, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "m.Stock market WebSocket session ended; reconnecting.");
                _health.Clear();
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunConnectionAsync(Uri uri, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(uri, ct);
        _log.LogInformation("m.Stock market WebSocket connected.");

        await SendJsonAsync(socket, new { a = "mode", v = new[] { "quote" } }, ct);
        var tokens = _market.GetWebSocketInstrumentTokens();
        if (tokens.Count > 0)
        {
            await SendJsonAsync(socket, new { a = "subscribe", v = tokens }, ct);
        }

        var lastSubscribe = DateTimeOffset.UtcNow;
        var lastTick = DateTimeOffset.UtcNow;

        using var receiveBuffer = new MemoryStream(16 * 1024);
        var chunk = new byte[8192];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            if (!_auth.HasSession)
            {
                break;
            }

            if (DateTimeOffset.UtcNow - lastSubscribe >= SubscribeRefresh)
            {
                var refreshTokens = _market.GetWebSocketInstrumentTokens();
                if (refreshTokens.Count > 0)
                {
                    await SendJsonAsync(socket, new { a = "subscribe", v = refreshTokens }, ct);
                    lastSubscribe = DateTimeOffset.UtcNow;
                }
            }

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(2));
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(chunk, readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - lastTick >= IdleReconnect)
                {
                    _log.LogDebug("m.Stock WebSocket idle; reconnecting.");
                    break;
                }

                continue;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType != WebSocketMessageType.Binary || result.Count == 0)
            {
                continue;
            }

            receiveBuffer.Write(chunk, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var ticks = MStockTypeATickParser.ParseMessage(receiveBuffer.GetBuffer().AsSpan(0, (int)receiveBuffer.Length));
            receiveBuffer.SetLength(0);
            if (ticks.Count > 0)
            {
                lastTick = DateTimeOffset.UtcNow;
                _health.RecordTick();
                await _market.IngestWebSocketTicksAsync(ticks, ct);
            }
        }

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}