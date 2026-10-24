using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class DeploymentReadinessService
{
    private readonly SystemIdentityOptions _identity;
    private readonly LiveExecutionOptions _live;
    private readonly BrokerConnectionManager _brokers;
    private readonly BrokerReconciliationService _reconciliation;
    private readonly MarketDataDistributor _market;

    public DeploymentReadinessService(
        SystemIdentityOptions identity,
        LiveExecutionOptions live,
        BrokerConnectionManager brokers,
        BrokerReconciliationService reconciliation,
        MarketDataDistributor market)
    {
        _identity = identity;
        _live = live;
        _brokers = brokers;
        _reconciliation = reconciliation;
        _market = market;
    }

    public DeploymentReadinessReport BuildReport()
    {
        var checks = new List<DeploymentReadinessCheck>
        {
            CheckAuthentication(),
            CheckAnonymousUser(),
            CheckLiveTrading(),
            CheckAdminPassword(),
            CheckPaperAgentSecurity(),
            CheckBrokerConnection(),
            CheckReconciliation(),
            CheckMarketFeed(),
        };

        var overall = ResolveOverall(checks);
        return new DeploymentReadinessReport(overall, checks, DateTimeOffset.UtcNow);
    }

    private DeploymentReadinessCheck CheckAuthentication() =>
        _identity.RequireAuthentication
            ? Pass("authentication", "Authentication enabled", "RequireAuthentication=true")
            : Fail("authentication", "Authentication enabled", "RequireAuthentication must be true for friend testing.");

    private DeploymentReadinessCheck CheckAnonymousUser() =>
        !_identity.AllowAnonymousLocalUser
            ? Pass("anonymous_user", "Anonymous user disabled", "AllowAnonymousLocalUser=false")
            : Fail("anonymous_user", "Anonymous user disabled", "AllowAnonymousLocalUser must be false in hosted mode.");

    private DeploymentReadinessCheck CheckLiveTrading() =>
        !_live.Enabled
            ? Pass("live_trading", "Live trading disabled", "LiveTrading:Enabled=false")
            : Warning("live_trading", "Live trading disabled", "LiveTrading:Enabled=true — disable for Phase 1 friend testing.");

    private DeploymentReadinessCheck CheckAdminPassword() =>
        AdminPasswordValidator.IsConfiguredSecurely(_identity.AdminPassword)
            ? Pass("admin_password", "Admin password configured", "Non-default SystemIdentity:AdminPassword")
            : Fail(
                "admin_password",
                "Admin password configured",
                "Set SystemIdentity:AdminPassword via user secrets or environment (not the default).");

    private static DeploymentReadinessCheck CheckPaperAgentSecurity() =>
        Pass(
            "paper_agent_security",
            "Paper agent control",
            "POST /api/paper-agent/command and take-strategy require Admin role.");

    private DeploymentReadinessCheck CheckBrokerConnection() =>
        _brokers.HasSession
            ? Pass("broker", "Broker connected", "m.Stock session active")
            : Warning("broker", "Broker connected", "Broker session not connected — quotes may be limited.");

    private DeploymentReadinessCheck CheckReconciliation()
    {
        var snapshot = _reconciliation.GetSnapshot();
        var detail = snapshot.HealthLabel;
        if (snapshot.LastRunAt is null)
        {
            return Warning("reconciliation", "Reconciliation running", detail);
        }

        return snapshot.Health switch
        {
            BrokerReconciliationHealth.Healthy => Pass("reconciliation", "Reconciliation running", detail),
            BrokerReconciliationHealth.IssuesFound => Warning("reconciliation", "Reconciliation running", detail),
            BrokerReconciliationHealth.BrokerError => Warning("reconciliation", "Reconciliation running", detail),
            _ => Warning("reconciliation", "Reconciliation running", detail),
        };
    }

    private DeploymentReadinessCheck CheckMarketFeed()
    {
        var feed = _market.GetStatus();
        if (feed.AdminBrokerConnected && feed.LastUpdateAt is not null)
        {
            return Pass("market_feed", "Market feed healthy", feed.Message);
        }

        return Warning("market_feed", "Market feed healthy", feed.Message);
    }

    private static DeploymentReadinessCheck Pass(string id, string label, string detail) =>
        new(id, label, DeploymentCheckStatus.Pass, detail);

    private static DeploymentReadinessCheck Warning(string id, string label, string detail) =>
        new(id, label, DeploymentCheckStatus.Warning, detail);

    private static DeploymentReadinessCheck Fail(string id, string label, string detail) =>
        new(id, label, DeploymentCheckStatus.Fail, detail);

    private static string ResolveOverall(IReadOnlyList<DeploymentReadinessCheck> checks)
    {
        if (checks.Any(c => c.Status == DeploymentCheckStatus.Fail))
        {
            return DeploymentOverallStatus.Red;
        }

        if (checks.Any(c => c.Status == DeploymentCheckStatus.Warning))
        {
            return DeploymentOverallStatus.Yellow;
        }

        return DeploymentOverallStatus.Green;
    }
}
