using Microsoft.AspNetCore.Mvc;
using TradeWithBeast.Authorization;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Controllers;

[ApiController]
[Route("api/admin")]
[AdminAuthorize]
public sealed class SystemNotificationController : ControllerBase
{
    private readonly SystemIncidentNotificationStore _store;
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly SystemIncidentNotificationProcessor _processor;
    private readonly SystemIncidentNotificationService _notifications;
    private readonly SystemIncidentStore _incidents;

    public SystemNotificationController(
        SystemIncidentNotificationStore store,
        SystemIncidentNotificationSettingsService settings,
        SystemIncidentNotificationProcessor processor,
        SystemIncidentNotificationService notifications,
        SystemIncidentStore incidents)
    {
        _store = store;
        _settings = settings;
        _processor = processor;
        _notifications = notifications;
        _incidents = incidents;
    }

    [HttpGet("notifications")]
    public IActionResult List(
        [FromQuery] string? incidentId,
        [FromQuery] IncidentNotificationChannel? channel,
        [FromQuery] IncidentNotificationDeliveryStatus? status,
        [FromQuery] IncidentNotificationType? notificationType,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) =>
        Ok(_store.ListNotifications(new IncidentNotificationListQuery(
            incidentId, channel, status, notificationType, fromUtc, toUtc, limit, offset)));

    [HttpGet("notifications/{id}")]
    public IActionResult Get(string id)
    {
        var notification = _store.GetNotification(id);
        return notification is null ? NotFound() : Ok(Sanitize(notification));
    }

    [HttpGet("incidents/{incidentId}/notifications")]
    public IActionResult ByIncident(string incidentId, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        if (_incidents.GetById(incidentId) is null)
        {
            return NotFound();
        }

        return Ok(_store.ListNotifications(new IncidentNotificationListQuery(incidentId, null, null, null, null, null, limit, offset)));
    }

    [HttpGet("notification-settings")]
    public IActionResult GetSettings()
    {
        _settings.ReloadRuntimeOverrides();
        return Ok(_settings.ToPublicSettings());
    }

    [HttpPut("notification-settings")]
    public IActionResult PutSettings([FromBody] NotificationSettingsUpdateRequest body)
    {
        if (body is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        _settings.ApplySettingsUpdate(body);
        return Ok(_settings.ToPublicSettings());
    }

    [HttpGet("escalation-policies")]
    public IActionResult GetEscalationPolicies()
    {
        _settings.ReloadRuntimeOverrides();
        return Ok(_settings.GetEscalationPolicies());
    }

    [HttpPut("escalation-policies")]
    public IActionResult PutEscalationPolicies([FromBody] EscalationPoliciesUpdateRequest body)
    {
        if (body is null || body.Tiers is null || body.Tiers.Count == 0)
        {
            return BadRequest(new { message = "At least one escalation tier is required." });
        }

        _settings.ApplyEscalationPolicies(body);
        return Ok(_settings.GetEscalationPolicies());
    }

    [HttpPost("notifications/test-email")]
    public IActionResult TestEmail([FromBody] TestNotificationRequest? body)
    {
        var options = _settings.EffectiveOptions;
        var recipient = body?.RecipientOverride?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = options.PrimaryEmailRecipients.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return BadRequest(new { message = "No email recipient configured." });
        }

        var incident = RequireSampleIncident();
        var queued = _notifications.QueueTestNotification(IncidentNotificationChannel.Email, recipient, incident);
        return Ok(Sanitize(queued));
    }

    [HttpPost("notifications/test-telegram")]
    public IActionResult TestTelegram([FromBody] TestNotificationRequest? body)
    {
        var options = _settings.EffectiveOptions;
        var chatId = body?.ChatIdOverride ?? options.PrimaryTelegramChatIds.FirstOrDefault();
        if (chatId == 0)
        {
            return BadRequest(new { message = "No Telegram chat id configured." });
        }

        var incident = RequireSampleIncident();
        var queued = _notifications.QueueTestNotification(
            IncidentNotificationChannel.Telegram,
            chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            incident);
        return Ok(Sanitize(queued));
    }

    [HttpPost("notifications/retry/{id}")]
    public IActionResult Retry(string id)
    {
        try
        {
            var updated = _processor.RetryNow(id, DateTimeOffset.UtcNow);
            return Ok(Sanitize(updated));
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound()
                : Conflict(new { message = ex.Message });
        }
    }

    private SystemIncidentRecord RequireSampleIncident()
    {
        var active = _incidents.ListActive(1).FirstOrDefault();
        if (active is not null)
        {
            return active;
        }

        var fingerprint = "notification-test|RuntimeUnobservedExceptions|notification-test";
        return _incidents.CreateOrUpdateOccurrence(
            new IncidentEvaluationCandidate(
                fingerprint,
                "Notification test incident",
                "Synthetic incident used for channel tests.",
                SystemIncidentComponents.Runtime,
                "notification-test",
                SystemIncidentCategory.RuntimeUnobservedExceptions,
                SystemIncidentSeverity.High,
                null,
                1,
                false,
                "{}"),
            DateTimeOffset.UtcNow);
    }

    private static IncidentNotificationRecord Sanitize(IncidentNotificationRecord record) => record;
}