using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentNotificationChannel
{
    Email = 0,
    Telegram = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentNotificationType
{
    IncidentDetected = 0,
    SeverityIncreased = 1,
    Acknowledged = 2,
    Investigating = 3,
    Resolved = 4,
    Reopened = 5,
    Closed = 6,
    EscalationReminder = 7,
    EscalationSecondary = 8,
    Test = 9,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentNotificationDeliveryStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3,
    RetryScheduled = 4,
    Cancelled = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EscalationJobStatus
{
    Pending = 0,
    Executed = 1,
    Cancelled = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EscalationStepKind
{
    UnacknowledgedReminder = 0,
    SecondaryRecipients = 1,
    UnresolvedReminder = 2,
}

public sealed record IncidentNotificationRecord(
    string Id,
    string IncidentId,
    string IdempotencyKey,
    IncidentNotificationChannel Channel,
    string Recipient,
    IncidentNotificationType NotificationType,
    string Subject,
    string BodyHtml,
    string BodyText,
    IncidentNotificationDeliveryStatus DeliveryStatus,
    int AttemptCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SentUtc,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? NextRetryUtc,
    string? FailureReason,
    string? ProviderMetadataJson,
    string? ProcessingOwner = null,
    DateTimeOffset? ProcessingLeaseExpiresUtc = null,
    string? ClaimToken = null,
    long? ClaimFencingToken = null);

public sealed record IncidentEscalationJobRecord(
    string Id,
    string IncidentId,
    EscalationStepKind StepKind,
    int StepIndex,
    string IdempotencyKey,
    EscalationJobStatus Status,
    DateTimeOffset ScheduledUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ExecutedUtc);

public sealed record IncidentNotificationListQuery(
    string? IncidentId = null,
    IncidentNotificationChannel? Channel = null,
    IncidentNotificationDeliveryStatus? Status = null,
    IncidentNotificationType? NotificationType = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 50,
    int Offset = 0);

public sealed record NotificationSettingsResponse(
    bool NotificationsEnabled,
    bool EmailEnabled,
    bool TelegramEnabled,
    string SenderEmail,
    IReadOnlyList<string> PrimaryEmailRecipients,
    IReadOnlyList<string> SecondaryEmailRecipients,
    IReadOnlyList<long> PrimaryTelegramChatIds,
    IReadOnlyList<long> SecondaryTelegramChatIds,
    bool TelegramOnMediumSeverity,
    bool NotifyOnLowSeverity,
    string IncidentPageUrlTemplate,
    int NotificationWorkerIntervalSeconds,
    int MaxConcurrentDeliveries,
    int MaximumDeliveryAttempts,
    int RetryBaseDelaySeconds,
    int RetryMaxDelaySeconds,
    SystemIncidentSeverity AlertMinimumSeverityForEmail,
    int NotificationThrottleSeconds);

public sealed record NotificationSettingsUpdateRequest
{
    public bool? NotificationsEnabled { get; set; }
    public bool? EmailEnabled { get; set; }
    public bool? TelegramEnabled { get; set; }
    public string? SenderEmail { get; set; }
    public List<string>? PrimaryEmailRecipients { get; set; }
    public List<string>? SecondaryEmailRecipients { get; set; }
    public List<long>? PrimaryTelegramChatIds { get; set; }
    public List<long>? SecondaryTelegramChatIds { get; set; }
    public bool? TelegramOnMediumSeverity { get; set; }
    public bool? NotifyOnLowSeverity { get; set; }
    public string? IncidentPageUrlTemplate { get; set; }
    public int? NotificationThrottleSeconds { get; set; }
}

public sealed record EscalationPolicyTierResponse(
    SystemIncidentSeverity Severity,
    IReadOnlyList<EscalationStepResponse> Steps);

public sealed record EscalationStepResponse(
    EscalationStepKind Kind,
    int DelayMinutes,
    bool UseSecondaryRecipients);

public sealed record EscalationPoliciesUpdateRequest
{
    public List<EscalationPolicyTierUpdate>? Tiers { get; set; }
}

public sealed record EscalationPolicyTierUpdate
{
    public SystemIncidentSeverity Severity { get; set; }
    public List<EscalationStepUpdate>? Steps { get; set; }
}

public sealed record EscalationStepUpdate
{
    public EscalationStepKind Kind { get; set; }
    public int DelayMinutes { get; set; }
    public bool UseSecondaryRecipients { get; set; }
}

public sealed record TestNotificationRequest
{
    public string? RecipientOverride { get; set; }
    public long? ChatIdOverride { get; set; }
}