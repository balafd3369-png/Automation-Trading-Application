namespace TradeWithBeast.Services;

public static class AdminPasswordValidator
{
    public static bool IsConfiguredSecurely(string? configuredPassword) =>
        !IsWeakOrDefault(configuredPassword);

    public static bool IsWeakOrDefault(string? configuredPassword) =>
        string.IsNullOrWhiteSpace(configuredPassword)
        || string.Equals(
            configuredPassword.Trim(),
            SystemIdentityOptions.DefaultAdminPassword,
            StringComparison.Ordinal);
}
