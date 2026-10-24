using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class DeploymentStartupValidator
{
    public static void ValidateForStartup(
        IHostEnvironment environment,
        SystemIdentityOptions identity,
        LiveExecutionOptions live,
        ILogger logger)
    {
        LogCheck(logger, identity.RequireAuthentication, "RequireAuthentication", "Authentication required for APIs");
        LogCheck(logger, !identity.AllowAnonymousLocalUser, "AllowAnonymousLocalUser", "Anonymous local-user fallback disabled");

        if (live.Enabled)
        {
            logger.LogWarning(
                "DEPLOYMENT CHECK WARNING: LiveTrading:Enabled=true. Set LiveTrading:Enabled=false for Phase 1 friend testing.");
        }
        else
        {
            logger.LogInformation("DEPLOYMENT CHECK PASS: LiveTrading:Enabled=false");
        }

        var adminOk = AdminPasswordValidator.IsConfiguredSecurely(identity.AdminPassword);
        if (!adminOk)
        {
            var message =
                "SystemIdentity:AdminPassword is missing or still the default. "
                + "Set a strong password via user secrets or environment before hosting.";
            if (environment.IsProduction())
            {
                throw new InvalidOperationException($"DEPLOYMENT CHECK FAIL: {message}");
            }

            logger.LogWarning("DEPLOYMENT CHECK WARNING: {Message}", message);
        }
        else
        {
            logger.LogInformation("DEPLOYMENT CHECK PASS: Admin password is configured (non-default)");
        }

        if (!identity.RequireAuthentication || identity.AllowAnonymousLocalUser)
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "DEPLOYMENT CHECK FAIL: Production requires RequireAuthentication=true and AllowAnonymousLocalUser=false.");
            }

            logger.LogWarning(
                "DEPLOYMENT CHECK WARNING: Auth settings are not in hosted friend-testing mode.");
        }
        else
        {
            logger.LogInformation("DEPLOYMENT CHECK PASS: Hosted authentication settings");
        }
    }

    public static void LogReadinessSummary(DeploymentReadinessReport report, ILogger logger)
    {
        logger.LogInformation("Deployment readiness overall: {Overall}", report.Overall);
        foreach (var check in report.Checks)
        {
            logger.LogInformation(
                "Deployment readiness [{Status}] {Label}: {Detail}",
                check.Status,
                check.Label,
                check.Detail);
        }
    }

    private static void LogCheck(ILogger logger, bool ok, string name, string description)
    {
        if (ok)
        {
            logger.LogInformation("DEPLOYMENT CHECK PASS: {Name} — {Description}", name, description);
        }
        else
        {
            logger.LogWarning("DEPLOYMENT CHECK FAIL: {Name} — {Description}", name, description);
        }
    }
}
