using System.Net;
using System.Net.Mail;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SmtpEmailNotificationProvider : IIncidentNotificationProvider
{
    private readonly SystemIncidentNotificationSettingsService _settings;
    private readonly ILogger<SmtpEmailNotificationProvider> _log;

    public SmtpEmailNotificationProvider(
        SystemIncidentNotificationSettingsService settings,
        ILogger<SmtpEmailNotificationProvider> log)
    {
        _settings = settings;
        _log = log;
    }

    public IncidentNotificationChannel Channel => IncidentNotificationChannel.Email;

    public async Task<NotificationProviderResult> SendAsync(IncidentNotificationRecord notification, CancellationToken ct)
    {
        var options = _settings.EffectiveOptions;
        if (!options.EmailEnabled || string.IsNullOrWhiteSpace(options.SmtpHost) || string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            return new NotificationProviderResult(false, false, "Email provider is not configured.", null);
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(options.SenderEmail, options.SenderDisplayName),
                Subject = notification.Subject,
                Body = notification.BodyHtml,
                IsBodyHtml = true,
            };
            message.To.Add(notification.Recipient);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                notification.BodyText,
                null,
                "text/plain"));

            using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
            {
                EnableSsl = options.SmtpUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
            };

            if (!string.IsNullOrWhiteSpace(options.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(options.SmtpUsername, options.SmtpPassword);
            }

            await Task.Run(() => client.Send(message), ct);
            return new NotificationProviderResult(
                true,
                false,
                null,
                SystemIncidentNotificationStore.SanitizeMetadata(new { channel = "email", recipient = notification.Recipient }));
        }
        catch (SmtpException ex)
        {
            _log.LogWarning(ex, "Permanent SMTP failure for notification {NotificationId}.", notification.Id);
            var transient = ex.StatusCode is SmtpStatusCode.GeneralFailure or SmtpStatusCode.ServiceNotAvailable;
            return new NotificationProviderResult(false, transient, ex.Message, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or WebException)
        {
            _log.LogWarning(ex, "Transient SMTP failure for notification {NotificationId}.", notification.Id);
            return new NotificationProviderResult(false, true, ex.Message, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SMTP failure for notification {NotificationId}.", notification.Id);
            return new NotificationProviderResult(false, true, ex.Message, null);
        }
    }
}