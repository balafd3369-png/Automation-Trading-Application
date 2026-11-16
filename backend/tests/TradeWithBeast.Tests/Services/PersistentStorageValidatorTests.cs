using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Configuration;
using TradeWithBeast.Services;
using Xunit;

namespace TradeWithBeast.Tests.Services;

public sealed class PersistentStorageValidatorTests
{
    [Fact]
    public void Production_fails_when_sqlite_path_outside_persistent_root()
    {
        var env = new TestHostEnvironment("Production");
        var infrastructure = new InfrastructureOptions
        {
            RequirePersistentDataMount = true,
            PersistentDataRoot = "/var/lib/tradewithbeast",
        };
        var database = new DatabaseOptions
        {
            Provider = "Sqlite",
            SqlitePath = "/tmp/trade.sqlite",
            TradeHistoryDatabasePath = "/var/lib/tradewithbeast/trade.sqlite",
        };
        var safety = new ProductionSafetyOptions
        {
            DatabaseBackupDirectory = "/var/lib/tradewithbeast/backups",
        };

        Assert.Throws<InvalidOperationException>(() =>
            PersistentStorageValidator.ValidateForProduction(
                env,
                infrastructure,
                database,
                safety,
                NullLogger.Instance));
    }

    [Fact]
    public void Skips_mount_check_when_not_required()
    {
        var env = new TestHostEnvironment("Production");
        PersistentStorageValidator.ValidateForProduction(
            env,
            new InfrastructureOptions { RequirePersistentDataMount = false },
            new DatabaseOptions { Provider = "Sqlite" },
            new ProductionSafetyOptions(),
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