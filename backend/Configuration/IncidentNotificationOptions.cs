using TradeWithBeast.Models;

namespace TradeWithBeast.Configuration;

/// <summary>Incident notification, email/Telegram, and escalation settings.</summary>
public sealed class IncidentNotificationOptions
{
    public const string Section = "IncidentNotification";

    public bool NotificationsEnabled { get; set; }

    public bool EmailEnabled { get; set; }

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string SmtpUsername { get; set; } = "";

    public string SmtpPassword { get; set; } = "";

    public string SenderEmail { get; set; } = "";

    public string SenderDisplayName { get; set; } = "Trade With Beast";

    public List<string> PrimaryEmailRecipients { get; set; } = [];

    public List<string> SecondaryEmailRecipients { get; set; } = [];

    public bool TelegramEnabled { get; set; }

    public string TelegramBotToken { get; set; } = "";

    public List<long> PrimaryTelegramChatIds { get; set; } = [];

    public List<long> SecondaryTelegramChatIds { get; set; } = [];

    public bool TelegramOnMediumSeverity { get; set; } = true;

    public bool NotifyOnLowSeverity { get; set; }

    public string IncidentPageUrlTemplate { get; set; } = "";

    public int NotificationWorkerIntervalSeconds { get; set; } = 5;

    public int MaxConcurrentDeliveries { get; set; } = 4;

    public int MaximumDeliveryAttempts { get; set; } = 5;

    public int RetryBaseDelaySeconds { get; set; } = 30;

    public int RetryMaxDelaySeconds { get; set; } = 900;

    public int NotificationThrottleSeconds { get; set; } = 120;

    public int TelegramHttpTimeoutSeconds { get; set; } = 20;

    public Dictionary<string, List<EscalationStepOptions>> EscalationPolicies { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Clamp()
    {
        SmtpPort = Math.Clamp(SmtpPort, 1, 65535);
        NotificationWorkerIntervalSeconds = Math.Clamp(NotificationWorkerIntervalSeconds, 2, 120);
        MaxConcurrentDeliveries = Math.Clamp(MaxConcurrentDeliveries, 1, 16);
        MaximumDeliveryAttempts = Math.Clamp(MaximumDeliveryAttempts, 1, 20);
        RetryBaseDelaySeconds = Math.Clamp(RetryBaseDelaySeconds, 5, 3600);
        RetryMaxDelaySeconds = Math.Clamp(RetryMaxDelaySeconds, RetryBaseDelaySeconds, 86400);
        NotificationThrottleSeconds = Math.Clamp(NotificationThrottleSeconds, 0, 86400);
        TelegramHttpTimeoutSeconds = Math.Clamp(TelegramHttpTimeoutSeconds, 3, 120);
        EnsureDefaultEscalationPolicies();
    }

    public IReadOnlyList<EscalationStepOptions> ResolveEscalationSteps(SystemIncidentSeverity severity)
    {
        EnsureDefaultEscalationPolicies();
        var key = severity.ToString();
        if (EscalationPolicies.TryGetValue(key, out var steps) && steps.Count > 0)
        {
            return steps;
        }

        return [];
    }

    public void EnsureDefaultEscalationPolicies()
    {
        EscalationPolicies.TryAdd(nameof(SystemIncidentSeverity.Critical), DefaultCriticalSteps());
        EscalationPolicies.TryAdd(nameof(SystemIncidentSeverity.High), DefaultHighSteps());
        EscalationPolicies.TryAdd(nameof(SystemIncidentSeverity.Medium), DefaultMediumSteps());
    }

    private static List<EscalationStepOptions> DefaultCriticalSteps() =>
    [
        new() { Kind = EscalationStepKind.UnacknowledgedReminder, DelayMinutes = 2 },
        new() { Kind = EscalationStepKind.SecondaryRecipients, DelayMinutes = 5, UseSecondaryRecipients = true },
        new() { Kind = EscalationStepKind.UnacknowledgedReminder, DelayMinutes = 10 },
    ];

    private static List<EscalationStepOptions> DefaultHighSteps() =>
    [
        new() { Kind = EscalationStepKind.UnacknowledgedReminder, DelayMinutes = 5 },
        new() { Kind = EscalationStepKind.SecondaryRecipients, DelayMinutes = 15, UseSecondaryRecipients = true },
    ];

    private static List<EscalationStepOptions> DefaultMediumSteps() =>
    [
        new() { Kind = EscalationStepKind.UnresolvedReminder, DelayMinutes = 30 },
    ];
}

public sealed class EscalationStepOptions
{
    public EscalationStepKind Kind { get; set; }

    public int DelayMinutes { get; set; } = 5;

    public bool UseSecondaryRecipients { get; set; }
}