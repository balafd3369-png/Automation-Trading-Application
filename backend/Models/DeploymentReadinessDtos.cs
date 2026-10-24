namespace TradeWithBeast.Models;

public static class DeploymentCheckStatus
{
    public const string Pass = "PASS";
    public const string Warning = "WARNING";
    public const string Fail = "FAIL";
}

public static class DeploymentOverallStatus
{
    public const string Green = "GREEN";
    public const string Yellow = "YELLOW";
    public const string Red = "RED";
}

public sealed record DeploymentReadinessCheck(
    string Id,
    string Label,
    string Status,
    string Detail);

public sealed record DeploymentReadinessReport(
    string Overall,
    IReadOnlyList<DeploymentReadinessCheck> Checks,
    DateTimeOffset GeneratedAt);

public sealed record DeploymentStatusSummary(
    string Overall,
    IReadOnlyList<DeploymentReadinessCheck> Checks,
    DateTimeOffset GeneratedAt);
