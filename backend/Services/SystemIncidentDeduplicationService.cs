namespace TradeWithBeast.Services;

public sealed class SystemIncidentDeduplicationService
{
    public string BuildFingerprint(string component, string category, string componentIdentifier)
    {
        var normalizedComponent = component.Trim().ToLowerInvariant();
        var normalizedCategory = category.Trim().ToLowerInvariant();
        var normalizedIdentifier = componentIdentifier.Trim().ToLowerInvariant();
        return $"{normalizedComponent}|{normalizedCategory}|{normalizedIdentifier}";
    }
}