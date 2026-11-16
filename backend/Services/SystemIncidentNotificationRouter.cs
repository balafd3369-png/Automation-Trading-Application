using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentNotificationRouter
{
    public IReadOnlyList<(IncidentNotificationChannel Channel, string Recipient)> ResolveRecipients(
        IncidentNotificationOptions options,
        SystemIncidentRecord incident,
        IncidentNotificationType notificationType,
        bool useSecondaryRecipients)
    {
        if (!options.NotificationsEnabled)
        {
            return [];
        }

        if (incident.Severity == SystemIncidentSeverity.Info && notificationType is not (
            IncidentNotificationType.Resolved or IncidentNotificationType.Closed or IncidentNotificationType.Acknowledged))
        {
            return [];
        }

        if (incident.Severity == SystemIncidentSeverity.Low && !options.NotifyOnLowSeverity
            && notificationType is IncidentNotificationType.IncidentDetected)
        {
            return [];
        }

        var list = new List<(IncidentNotificationChannel, string)>();
        var emails = useSecondaryRecipients ? options.SecondaryEmailRecipients : options.PrimaryEmailRecipients;
        var chats = useSecondaryRecipients ? options.SecondaryTelegramChatIds : options.PrimaryTelegramChatIds;

        var sendEmail = ShouldSendEmail(options, incident, notificationType);
        var sendTelegram = ShouldSendTelegram(options, incident, notificationType);

        if (sendEmail && options.EmailEnabled)
        {
            foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                list.Add((IncidentNotificationChannel.Email, email.Trim()));
            }
        }

        if (sendTelegram && options.TelegramEnabled)
        {
            foreach (var chat in chats)
            {
                list.Add((IncidentNotificationChannel.Telegram, chat.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return list;
    }

    private static bool ShouldSendEmail(IncidentNotificationOptions options, SystemIncidentRecord incident, IncidentNotificationType type)
    {
        return type switch
        {
            IncidentNotificationType.Test => true,
            IncidentNotificationType.EscalationSecondary => true,
            IncidentNotificationType.EscalationReminder => incident.Severity >= SystemIncidentSeverity.Medium,
            IncidentNotificationType.IncidentDetected or IncidentNotificationType.SeverityIncreased or IncidentNotificationType.Reopened
                => incident.Severity >= SystemIncidentSeverity.Medium,
            IncidentNotificationType.Acknowledged or IncidentNotificationType.Investigating
                or IncidentNotificationType.Resolved or IncidentNotificationType.Closed => true,
            _ => incident.Severity >= SystemIncidentSeverity.Medium,
        };
    }

    private static bool ShouldSendTelegram(IncidentNotificationOptions options, SystemIncidentRecord incident, IncidentNotificationType type)
    {
        if (type == IncidentNotificationType.Test)
        {
            return true;
        }

        if (incident.Severity >= SystemIncidentSeverity.High
            || type is IncidentNotificationType.EscalationReminder or IncidentNotificationType.EscalationSecondary)
        {
            return true;
        }

        if (incident.Severity == SystemIncidentSeverity.Medium && options.TelegramOnMediumSeverity)
        {
            return true;
        }

        if (type is IncidentNotificationType.Acknowledged or IncidentNotificationType.Investigating
            or IncidentNotificationType.Resolved or IncidentNotificationType.Closed or IncidentNotificationType.Reopened)
        {
            return incident.Severity >= SystemIncidentSeverity.Medium;
        }

        return false;
    }
}