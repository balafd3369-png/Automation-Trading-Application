using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class DeploymentStartupValidatorTests
{
    [Fact]
    public void Production_fails_when_live_enabled_without_broker_credentials()
    {
        var identity = new SystemIdentityOptions { AdminPassword = "secure-password-123" };
        var live = new LiveExecutionOptions { Enabled = true };
        var broker = new MStockOptions();
        var database = new DatabaseOptions();
        var cors = new CorsOptions();

        var infrastructure = new InfrastructureOptions();
        var productionSafety = new ProductionSafetyOptions();

        Assert.Throws<InvalidOperationException>(() =>
            DeploymentStartupValidator.ValidateForStartup(
                new TestHostEnvironment("Production"),
                identity,
                live,
                broker,
                database,
                cors,
                infrastructure,
                productionSafety,
                NullLogger.Instance));
    }

    [Fact]
    public void Production_passes_when_live_disabled_and_admin_configured()
    {
        var identity = new SystemIdentityOptions { AdminPassword = "secure-password-123" };
        var live = new LiveExecutionOptions { Enabled = false };
        var broker = new MStockOptions();
        var database = new DatabaseOptions();
        var cors = new CorsOptions();

        var infrastructure = new InfrastructureOptions();
        var productionSafety = new ProductionSafetyOptions();

        DeploymentStartupValidator.ValidateForStartup(
            new TestHostEnvironment("Production"),
            identity,
            live,
            broker,
            database,
            cors,
            infrastructure,
            productionSafety,
            NullLogger.Instance);
    }

    private sealed class TestHostEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
