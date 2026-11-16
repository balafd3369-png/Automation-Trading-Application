using System.Net;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class IncidentNotificationContentBuilder
{
    public (string Subject, string Html, string Text) Build(
        SystemIncidentRecord incident,
        IncidentNotificationType notificationType,
        string? incidentUrl,
        string? extraLine = null)
    {
        var subject = $"[{incident.Severity}] {Headline(notificationType)} — {incident.Title}";
        var investigation = Recommendation(incident);
        var urlLine = string.IsNullOrWhiteSpace(incidentUrl)
            ? "Incident management URL is not configured."
            : $"Open incident: {incidentUrl}";
        var html = $"""
            <html><body>
            <h2>{WebUtility.HtmlEncode(Headline(notificationType))}</h2>
            <p><strong>Incident ID:</strong> {WebUtility.HtmlEncode(incident.Id)}</p>
            <p><strong>Title:</strong> {WebUtility.HtmlEncode(incident.Title)}</p>
            <p><strong>Severity:</strong> {incident.Severity}</p>
            <p><strong>Status:</strong> {incident.Status}</p>
            <p><strong>Component:</strong> {WebUtility.HtmlEncode(incident.Component)} / {WebUtility.HtmlEncode(incident.ComponentIdentifier)}</p>
            <p><strong>Detected (UTC):</strong> {incident.FirstDetectedUtc:O}</p>
            <p><strong>Description:</strong> {WebUtility.HtmlEncode(incident.Description)}</p>
            <p><strong>Recommended investigation:</strong> {WebUtility.HtmlEncode(investigation)}</p>
            {(extraLine is null ? "" : $"<p>{WebUtility.HtmlEncode(extraLine)}</p>")}
            <p>{WebUtility.HtmlEncode(urlLine)}</p>
            </body></html>
            """;
        var text = $"""
            {Headline(notificationType)}
            Incident ID: {incident.Id}
            Title: {incident.Title}
            Severity: {incident.Severity}
            Status: {incident.Status}
            Component: {incident.Component} / {incident.ComponentIdentifier}
            Detected (UTC): {incident.FirstDetectedUtc:O}
            Description: {incident.Description}
            Recommended investigation: {investigation}
            {(extraLine is null ? "" : extraLine + Environment.NewLine)}
            {urlLine}
            """;
        return (subject, html, text);
    }

    private static string Headline(IncidentNotificationType type) => type switch
    {
        IncidentNotificationType.IncidentDetected => "Incident detected",
        IncidentNotificationType.SeverityIncreased => "Incident severity increased",
        IncidentNotificationType.Acknowledged => "Incident acknowledged",
        IncidentNotificationType.Investigating => "Incident under investigation",
        IncidentNotificationType.Resolved => "Incident resolved",
        IncidentNotificationType.Reopened => "Incident reopened",
        IncidentNotificationType.Closed => "Incident closed",
        IncidentNotificationType.EscalationReminder => "Incident escalation reminder",
        IncidentNotificationType.EscalationSecondary => "Incident escalated to secondary recipients",
        IncidentNotificationType.Test => "Notification test",
        _ => "Incident notification",
    };

    private static string Recommendation(SystemIncidentRecord incident) => incident.Component switch
    {
        SystemIncidentComponents.Database => "Verify database connectivity, disk space, and migration health.",
        SystemIncidentComponents.Broker => "Check broker session, credentials rotation, and API status.",
        SystemIncidentComponents.MarketData => "Validate market data subscriptions and session calendar.",
        SystemIncidentComponents.SignalR => "Inspect SignalR hub connectivity and client subscriptions.",
        SystemIncidentComponents.Workers => "Review worker heartbeats, logs, and recent exceptions.",
        SystemIncidentComponents.Runtime => "Review background service exceptions and unobserved tasks.",
        _ => "Review incident details and related health dashboards.",
    };
}