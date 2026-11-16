using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using TradeWithBeast.Tests.Integration;

namespace TradeWithBeast.Tests.TestSupport;

internal static class IntegrationTestAppConfiguration
{
    public const string AdminEmail = "admin@test.local";

    public static void AddIdentityAndDatabase(
        IWebHostBuilder builder,
        string historyPath,
        bool requireAuthentication,
        bool allowAnonymousLocalUser)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TradeHistory:DatabasePath"] = historyPath,
                ["SystemIdentity:RequireAuthentication"] = requireAuthentication ? "true" : "false",
                ["SystemIdentity:AllowAnonymousLocalUser"] = allowAnonymousLocalUser ? "true" : "false",
                ["SystemIdentity:AdminEmail"] = AdminEmail,
                ["SystemIdentity:AdminPassword"] = TestAuthHelper.DefaultAdminPassword,
                ["SystemIdentity:RegistrationMode"] = "Open",
                ["LiveTrading:Enabled"] = "false",
            });
        });
    }
}