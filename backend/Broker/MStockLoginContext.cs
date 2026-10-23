namespace TradeWithBeast.Broker;

/// <summary>
/// Holds API key from the latest login attempt (UI or config) for OTP verify and authorized calls.
/// </summary>
public sealed class MStockLoginContext
{
    private string? _apiKeyOverride;

    public void SetApiKeyOverride(string? apiKey)
    {
        _apiKeyOverride = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public void Clear()
    {
        _apiKeyOverride = null;
    }

    public string ResolveApiKey(MStockOptions options) =>
        FirstNonEmpty(_apiKeyOverride, options.ApiKey) ?? "";

    private static string? FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
        {
            return a.Trim();
        }

        return string.IsNullOrWhiteSpace(b) ? null : b.Trim();
    }
}
