using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.TestSupport;

internal static class WebApplicationFactoryExtensions
{
    /// <summary>Eagerly construct <see cref="UserIdentityStore"/> so seeded admin exists before HTTP tests log in.</summary>
    public static IHost WarmUpIdentityStore(this IHost host)
    {
        _ = host.Services.GetRequiredService<UserIdentityStore>();
        return host;
    }
}