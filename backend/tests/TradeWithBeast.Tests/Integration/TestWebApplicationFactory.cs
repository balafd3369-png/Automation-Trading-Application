using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(),
        "trade-with-beast-tests",
        $"trade-history-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("TradeHistory:DatabasePath", _historyPath);
        builder.UseSetting("LiveTrading:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<MStockSessionStore>();
            var sessionPath = Path.Combine(Path.GetTempPath(), "trade-with-beast-tests", "mstock-session.json");
            services.AddSingleton(_ => new MStockSessionStore(sessionPath));
        });
    }
}
