using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TradeWithBeast.Broker;
using TradeWithBeast.Services;
using TradeWithBeast.Tests.TestSupport;

namespace TradeWithBeast.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(),
        "trade-with-beast-tests",
        $"trade-history-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        IntegrationTestAppConfiguration.AddIdentityAndDatabase(
            builder,
            _historyPath,
            requireAuthentication: false,
            allowAnonymousLocalUser: true);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<MStockSessionStore>();
            var sessionPath = Path.Combine(Path.GetTempPath(), "trade-with-beast-tests", "mstock-session.json");
            services.AddSingleton(_ => new MStockSessionStore(sessionPath));
            services.AddSingleton<TrapFallFormationService>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        return host.WarmUpIdentityStore();
    }
}
