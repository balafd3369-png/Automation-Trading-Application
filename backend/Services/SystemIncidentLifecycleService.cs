using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class SystemIncidentLifecycleService
{
    public void EnsureTransition(SystemIncidentStatus current, SystemIncidentStatus next)
    {
        if (current == next)
        {
            throw new InvalidOperationException("Incident is already in the requested status.");
        }

        var allowed = current switch
        {
            SystemIncidentStatus.Detected => next is SystemIncidentStatus.Acknowledged
                or SystemIncidentStatus.Investigating
                or SystemIncidentStatus.Resolved
                or SystemIncidentStatus.Closed,
            SystemIncidentStatus.Acknowledged => next is SystemIncidentStatus.Investigating
                or SystemIncidentStatus.Resolved
                or SystemIncidentStatus.Closed,
            SystemIncidentStatus.Investigating => next is SystemIncidentStatus.Resolved
                or SystemIncidentStatus.Closed,
            SystemIncidentStatus.Resolved => next is SystemIncidentStatus.Closed,
            SystemIncidentStatus.Closed => false,
            _ => false,
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Invalid incident transition {current} -> {next}.");
        }
    }

    public bool IsActive(SystemIncidentStatus status) =>
        status is SystemIncidentStatus.Detected
            or SystemIncidentStatus.Acknowledged
            or SystemIncidentStatus.Investigating;
}