using System.Text.Json;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Merges appsettings with non-secret admin runtime overrides.</summary>
public sealed class SystemIncidentNotificationSettingsService
{
    private readonly IncidentNotificationOptions _options;
    private readonly SystemIncidentNotificationStore _store;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private RuntimeNotificationOverrides? _runtime;

    public SystemIncidentNotificationSettingsService(
        IncidentNotificationOptions options,
        SystemIncidentNotificationStore store)
    {
        _options = options;
        _store = store;
        ReloadRuntimeOverrides();
    }

    public IncidentNotificationOptions EffectiveOptions => BuildEffective();

    public NotificationSettingsResponse ToPublicSettings()
    {
        var effective = BuildEffective();
        return new NotificationSettingsResponse(
            effective.NotificationsEnabled,
            effective.EmailEnabled,
            effective.TelegramEnabled,
            effective.SenderEmail,
            effective.PrimaryEmailRecipients,
            effective.SecondaryEmailRecipients,
            effective.PrimaryTelegramChatIds,
            effective.SecondaryTelegramChatIds,
            effective.TelegramOnMediumSeverity,
            effective.NotifyOnLowSeverity,
            effective.IncidentPageUrlTemplate,
            effective.NotificationWorkerIntervalSeconds,
            effective.MaxConcurrentDeliveries,
            effective.MaximumDeliveryAttempts,
            effective.RetryBaseDelaySeconds,
            effective.RetryMaxDelaySeconds,
            SystemIncidentSeverity.Medium,
            effective.NotificationThrottleSeconds);
    }

    public void ApplySettingsUpdate(NotificationSettingsUpdateRequest request)
    {
        var current = _runtime ?? new RuntimeNotificationOverrides();
        if (request.NotificationsEnabled is not null)
        {
            current.NotificationsEnabled = request.NotificationsEnabled;
        }

        if (request.EmailEnabled is not null)
        {
            current.EmailEnabled = request.EmailEnabled;
        }

        if (request.TelegramEnabled is not null)
        {
            current.TelegramEnabled = request.TelegramEnabled;
        }

        if (!string.IsNullOrWhiteSpace(request.SenderEmail))
        {
            current.SenderEmail = request.SenderEmail.Trim();
        }

        if (request.PrimaryEmailRecipients is not null)
        {
            current.PrimaryEmailRecipients = SanitizeEmails(request.PrimaryEmailRecipients);
        }

        if (request.SecondaryEmailRecipients is not null)
        {
            current.SecondaryEmailRecipients = SanitizeEmails(request.SecondaryEmailRecipients);
        }

        if (request.PrimaryTelegramChatIds is not null)
        {
            current.PrimaryTelegramChatIds = request.PrimaryTelegramChatIds.Distinct().ToList();
        }

        if (request.SecondaryTelegramChatIds is not null)
        {
            current.SecondaryTelegramChatIds = request.SecondaryTelegramChatIds.Distinct().ToList();
        }

        if (request.TelegramOnMediumSeverity is not null)
        {
            current.TelegramOnMediumSeverity = request.TelegramOnMediumSeverity;
        }

        if (request.NotifyOnLowSeverity is not null)
        {
            current.NotifyOnLowSeverity = request.NotifyOnLowSeverity;
        }

        if (request.IncidentPageUrlTemplate is not null)
        {
            current.IncidentPageUrlTemplate = request.IncidentPageUrlTemplate.Trim();
        }

        if (request.NotificationThrottleSeconds is not null)
        {
            current.NotificationThrottleSeconds = Math.Clamp(request.NotificationThrottleSeconds.Value, 0, 86400);
        }

        _runtime = current;
        PersistRuntime(SystemIncidentNotificationStore.RuntimeSettingsKey, current);
    }

    public IReadOnlyList<EscalationPolicyTierResponse> GetEscalationPolicies()
    {
        ReloadRuntimeEscalation();
        var effective = BuildEffective();
        effective.EnsureDefaultEscalationPolicies();
        return Enum.GetValues<SystemIncidentSeverity>()
            .Where(s => s is not SystemIncidentSeverity.Info)
            .Select(severity =>
            {
                var steps = effective.ResolveEscalationSteps(severity)
                    .Select(s => new EscalationStepResponse(s.Kind, s.DelayMinutes, s.UseSecondaryRecipients))
                    .ToList();
                return new EscalationPolicyTierResponse(severity, steps);
            })
            .ToList();
    }

    public void ApplyEscalationPolicies(EscalationPoliciesUpdateRequest request)
    {
        var tiers = request.Tiers ?? [];
        var map = new Dictionary<string, List<EscalationStepOptions>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in tiers)
        {
            var steps = (tier.Steps ?? [])
                .Select(s => new EscalationStepOptions
                {
                    Kind = s.Kind,
                    DelayMinutes = Math.Clamp(s.DelayMinutes, 1, 1440),
                    UseSecondaryRecipients = s.UseSecondaryRecipients,
                })
                .ToList();
            map[tier.Severity.ToString()] = steps;
        }

        PersistRuntime(SystemIncidentNotificationStore.RuntimeEscalationKey, map);
        _options.EscalationPolicies = map;
    }

    public void ReloadRuntimeOverrides()
    {
        var json = _store.LoadRuntimeJson(SystemIncidentNotificationStore.RuntimeSettingsKey);
        _runtime = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<RuntimeNotificationOverrides>(json, JsonOptions);

        var escalationJson = _store.LoadRuntimeJson(SystemIncidentNotificationStore.RuntimeEscalationKey);
        if (!string.IsNullOrWhiteSpace(escalationJson))
        {
            var policies = JsonSerializer.Deserialize<Dictionary<string, List<EscalationStepOptions>>>(escalationJson, JsonOptions);
            if (policies is not null)
            {
                _options.EscalationPolicies = policies;
            }
        }
    }

    private void ReloadRuntimeEscalation() => ReloadRuntimeOverrides();

    private IncidentNotificationOptions BuildEffective()
    {
        var clone = CloneOptions(_options);
        clone.Clamp();
        if (_runtime is null)
        {
            return clone;
        }

        if (_runtime.NotificationsEnabled is not null)
        {
            clone.NotificationsEnabled = _runtime.NotificationsEnabled.Value;
        }

        if (_runtime.EmailEnabled is not null)
        {
            clone.EmailEnabled = _runtime.EmailEnabled.Value;
        }

        if (_runtime.TelegramEnabled is not null)
        {
            clone.TelegramEnabled = _runtime.TelegramEnabled.Value;
        }

        if (!string.IsNullOrWhiteSpace(_runtime.SenderEmail))
        {
            clone.SenderEmail = _runtime.SenderEmail!;
        }

        if (_runtime.PrimaryEmailRecipients is not null)
        {
            clone.PrimaryEmailRecipients = _runtime.PrimaryEmailRecipients;
        }

        if (_runtime.SecondaryEmailRecipients is not null)
        {
            clone.SecondaryEmailRecipients = _runtime.SecondaryEmailRecipients;
        }

        if (_runtime.PrimaryTelegramChatIds is not null)
        {
            clone.PrimaryTelegramChatIds = _runtime.PrimaryTelegramChatIds;
        }

        if (_runtime.SecondaryTelegramChatIds is not null)
        {
            clone.SecondaryTelegramChatIds = _runtime.SecondaryTelegramChatIds;
        }

        if (_runtime.TelegramOnMediumSeverity is not null)
        {
            clone.TelegramOnMediumSeverity = _runtime.TelegramOnMediumSeverity.Value;
        }

        if (_runtime.NotifyOnLowSeverity is not null)
        {
            clone.NotifyOnLowSeverity = _runtime.NotifyOnLowSeverity.Value;
        }

        if (_runtime.IncidentPageUrlTemplate is not null)
        {
            clone.IncidentPageUrlTemplate = _runtime.IncidentPageUrlTemplate;
        }

        if (_runtime.NotificationThrottleSeconds is not null)
        {
            clone.NotificationThrottleSeconds = _runtime.NotificationThrottleSeconds.Value;
        }

        return clone;
    }

    private void PersistRuntime<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        _store.SaveRuntimeJson(key, json);
    }

    private static IncidentNotificationOptions CloneOptions(IncidentNotificationOptions source) => new()
    {
        NotificationsEnabled = source.NotificationsEnabled,
        EmailEnabled = source.EmailEnabled,
        SmtpHost = source.SmtpHost,
        SmtpPort = source.SmtpPort,
        SmtpUseSsl = source.SmtpUseSsl,
        SmtpUsername = source.SmtpUsername,
        SmtpPassword = source.SmtpPassword,
        SenderEmail = source.SenderEmail,
        SenderDisplayName = source.SenderDisplayName,
        PrimaryEmailRecipients = new List<string>(source.PrimaryEmailRecipients),
        SecondaryEmailRecipients = new List<string>(source.SecondaryEmailRecipients),
        TelegramEnabled = source.TelegramEnabled,
        TelegramBotToken = source.TelegramBotToken,
        PrimaryTelegramChatIds = new List<long>(source.PrimaryTelegramChatIds),
        SecondaryTelegramChatIds = new List<long>(source.SecondaryTelegramChatIds),
        TelegramOnMediumSeverity = source.TelegramOnMediumSeverity,
        NotifyOnLowSeverity = source.NotifyOnLowSeverity,
        IncidentPageUrlTemplate = source.IncidentPageUrlTemplate,
        NotificationWorkerIntervalSeconds = source.NotificationWorkerIntervalSeconds,
        MaxConcurrentDeliveries = source.MaxConcurrentDeliveries,
        MaximumDeliveryAttempts = source.MaximumDeliveryAttempts,
        RetryBaseDelaySeconds = source.RetryBaseDelaySeconds,
        RetryMaxDelaySeconds = source.RetryMaxDelaySeconds,
        NotificationThrottleSeconds = source.NotificationThrottleSeconds,
        TelegramHttpTimeoutSeconds = source.TelegramHttpTimeoutSeconds,
        EscalationPolicies = source.EscalationPolicies.ToDictionary(
            x => x.Key,
            x => x.Value.Select(s => new EscalationStepOptions
            {
                Kind = s.Kind,
                DelayMinutes = s.DelayMinutes,
                UseSecondaryRecipients = s.UseSecondaryRecipients,
            }).ToList(),
            StringComparer.OrdinalIgnoreCase),
    };

    private static List<string> SanitizeEmails(IEnumerable<string> emails) =>
        emails.Select(e => e.Trim()).Where(e => e.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private sealed class RuntimeNotificationOverrides
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
}