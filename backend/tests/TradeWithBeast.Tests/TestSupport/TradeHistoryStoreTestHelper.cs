using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Configuration;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.TestSupport;

internal static class TradeHistoryStoreTestHelper
{
    public static TradeHistoryStore Create(string databasePath) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TradeHistory:DatabasePath"] = databasePath,
                })
                .Build(),
            new DatabaseOptions(),
            new CurrentUserAccessor(),
            new SystemIdentityOptions { AllowAnonymousLocalUser = true },
            NullLogger<TradeHistoryStore>.Instance);
}