using System.Net;
using System.Text;
using System.Text.Json;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class TelegramBotNotificationProvider : IIncidentNotificationProvider
{
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramBotNotificationProvider> _log;

    public TelegramBotNotificationProvider(
        SystemIncidentNotificationSettingsService settings,
        IHttpClientFactory httpClientFactory,
        ILogger<TelegramBotNotificationProvider> log)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public IncidentNotificationChannel Channel => IncidentNotificationChannel.Telegram;

    public async Task<NotificationProviderResult> SendAsync(IncidentNotificationRecord notification, CancellationToken ct)
    {
        var options = _settings.EffectiveOptions;
        if (!options.TelegramEnabled || string.IsNullOrWhiteSpace(options.TelegramBotToken))
        {
            return new NotificationProviderResult(false, false, "Telegram provider is not configured.", null);
        }

        if (!long.TryParse(notification.Recipient, out var chatId))
        {
            return new NotificationProviderResult(false, false, "Invalid Telegram chat id.", null);
        }

        var client = _httpClientFactory.CreateClient(nameof(TelegramBotNotificationProvider));
        client.Timeout = TimeSpan.FromSeconds(options.TelegramHttpTimeoutSeconds);

        var text = ConvertToTelegramHtml(notification.BodyText);
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true,
        };

        var url = $"https://api.telegram.org/bot{options.TelegramBotToken}/sendMessage";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationProviderResult(
                    true,
                    false,
                    null,
                    SystemIncidentNotificationStore.SanitizeMetadata(new { channel = "telegram", chatId, responseStatus = (int)response.StatusCode }));
            }

            var transient = response.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
            _log.LogWarning(
                "Telegram delivery failed for notification {NotificationId} with HTTP {StatusCode}.",
                notification.Id,
                (int)response.StatusCode);
            return new NotificationProviderResult(false, transient, TrimError(body), SystemIncidentNotificationStore.SanitizeMetadata(new { status = (int)response.StatusCode }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            _log.LogWarning(ex, "Transient Telegram failure for notification {NotificationId}.", notification.Id);
            return new NotificationProviderResult(false, true, ex.Message, null);
        }
    }

    private static string ConvertToTelegramHtml(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        return $"<pre>{escaped}</pre>";
    }

    private static string TrimError(string body) =>
        body.Length <= 300 ? body : body[..300];
}