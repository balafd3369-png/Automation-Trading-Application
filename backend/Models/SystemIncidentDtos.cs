using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemIncidentSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemIncidentStatus
{
    Detected = 0,
    Acknowledged = 1,
    Investigating = 2,
    Resolved = 3,
    Closed = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemIncidentCategory
{
    WorkerHeartbeatTimeout = 0,
    WorkerStopped = 1,
    WorkerUnresponsive = 2,
    WorkerStalled = 3,
    WorkerDegraded = 4,
    BrokerDisconnected = 5,
    BrokerAuthFailure = 6,
    BrokerCommunicationTimeout = 7,
    BrokerApiFailures = 8,
    MarketDataStale = 9,
    MarketDataFeedDisconnected = 10,
    SignalRDegradation = 11,
    SignalRUnavailable = 12,
    DatabaseUnavailable = 13,
    DatabaseReadFailure = 14,
    DatabaseWriteFailure = 15,
    RuntimeUnobservedExceptions = 16,
    ComponentRecovered = 17,
    OrderReconciliationMismatch = 18,
    OrderStateUnknown = 19,
    OrderReconciliationUnavailable = 20,
    DatabaseBackupFailed = 21,
    EmergencyGateActive = 22,
    CircuitBreakerOpen = 23,
    StuckSubmittedOrder = 24,
    DuplicateOrderAttempt = 25,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemAlertStatus
{
    Pending = 0,
    Dispatched = 1,
    Acknowledged = 2,
    Suppressed = 3,
    Failed = 4,
}

public sealed record SystemIncidentRecord(
    string Id,
    string Fingerprint,
    string Title,
    string Description,
    string Component,
    string ComponentIdentifier,
    SystemIncidentCategory Category,
    SystemIncidentSeverity Severity,
    SystemIncidentStatus Status,
    DateTimeOffset FirstDetectedUtc,
    DateTimeOffset LastOccurrenceUtc,
    DateTimeOffset LastUpdatedUtc,
    DateTimeOffset? AcknowledgedUtc,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedUtc,
    DateTimeOffset? ClosedUtc,
    string? ClosedBy,
    int OccurrenceCount,
    string? LastError,
    string MetadataJson);

public sealed record SystemIncidentEventRecord(
    string Id,
    string IncidentId,
    string EventType,
    SystemIncidentStatus? PreviousStatus,
    SystemIncidentStatus? NewStatus,
    SystemIncidentSeverity Severity,
    string Message,
    string? Actor,
    DateTimeOffset TimestampUtc,
    string? MetadataJson);

public sealed record SystemIncidentNoteRecord(
    string Id,
    string IncidentId,
    string Note,
    string CreatedBy,
    DateTimeOffset CreatedUtc);

public sealed record SystemAlertRecord(
    string Id,
    string IncidentId,
    string Fingerprint,
    SystemIncidentSeverity Severity,
    SystemAlertStatus Status,
    string Title,
    string Message,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DispatchedUtc,
    DateTimeOffset? AcknowledgedUtc,
    string? AcknowledgedBy,
    int AttemptCount,
    string? LastError);

public sealed record SystemIncidentStatistics(
    int TotalIncidents,
    int ActiveIncidents,
    int CriticalIncidents,
    int HighIncidents,
    int AcknowledgedIncidents,
    int InvestigatingIncidents,
    int ResolvedIncidents,
    int ClosedIncidents,
    IReadOnlyDictionary<string, int> CountsByComponent,
    IReadOnlyDictionary<string, int> CountsBySeverity,
    IReadOnlyDictionary<string, int> CountsByCategory,
    double? AverageSecondsToAcknowledge,
    double? AverageSecondsToResolve,
    int RepeatedIncidentFingerprints,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? FilterFromUtc,
    DateTimeOffset? FilterToUtc);

public sealed record SystemIncidentListQuery(
    SystemIncidentStatus? Status = null,
    SystemIncidentSeverity? MinSeverity = null,
    string? Component = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 50,
    int Offset = 0);

public sealed record SystemAlertListQuery(
    SystemAlertStatus? Status = null,
    SystemIncidentSeverity? MinSeverity = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 50,
    int Offset = 0);

public sealed record IncidentEvaluationCandidate(
    string Fingerprint,
    string Title,
    string Description,
    string Component,
    string ComponentIdentifier,
    SystemIncidentCategory Category,
    SystemIncidentSeverity Severity,
    string? LastError,
    int SourceOccurrenceCount,
    bool IsHealthy,
    string? MetadataJson);

public static class SystemIncidentComponents
{
    public const string Workers = "workers";
    public const string Broker = "broker";
    public const string MarketData = "market-data";
    public const string SignalR = "signalr";
    public const string Database = "database";
    public const string Runtime = "runtime";
    public const string OrderSafety = "order-safety";
    public const string Production = "production";
}

public static class SystemIncidentEventTypes
{
    public const string Detected = "DETECTED";
    public const string OccurrenceUpdated = "OCCURRENCE_UPDATED";
    public const string Acknowledged = "ACKNOWLEDGED";
    public const string Investigating = "INVESTIGATING";
    public const string Resolved = "RESOLVED";
    public const string Closed = "CLOSED";
    public const string Recovered = "RECOVERED";
    public const string NoteAdded = "NOTE_ADDED";
    public const string AlertCreated = "ALERT_CREATED";
}