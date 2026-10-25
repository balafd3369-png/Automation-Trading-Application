using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Configuration;

public sealed class AppConfigurationBundle
{
    public required MStockOptions MStock { get; init; }
    public required LiveExecutionOptions LiveTrading { get; init; }
    public required TradingPlatformOptions TradingPlatform { get; init; }
    public required SystemIdentityOptions SystemIdentity { get; init; }
    public required DatabaseOptions Database { get; init; }
    public required BrokerOptions Broker { get; init; }
    public required TradingOptions Trading { get; init; }
    public required SecurityOptions Security { get; init; }
    public required CorsOptions Cors { get; init; }
    public required InfrastructureOptions Infrastructure { get; init; }
}

public static class AppConfigurationBinder
{
    public static AppConfigurationBundle Bind(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        var mstock = configuration.GetSection(MStockOptions.Section).Get<MStockOptions>() ?? new MStockOptions();
        var broker = configuration.GetSection(BrokerOptions.Section).Get<BrokerOptions>() ?? new BrokerOptions();
        ApplyBrokerOverrides(mstock, broker);

        var bundle = new AppConfigurationBundle
        {
            MStock = mstock,
            LiveTrading = configuration.GetSection(LiveExecutionOptions.Section).Get<LiveExecutionOptions>()
                ?? new LiveExecutionOptions(),
            TradingPlatform = configuration.GetSection(TradingPlatformOptions.Section).Get<TradingPlatformOptions>()
                ?? new TradingPlatformOptions(),
            SystemIdentity = configuration.GetSection(SystemIdentityOptions.Section).Get<SystemIdentityOptions>()
                ?? new SystemIdentityOptions(),
            Database = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()
                ?? new DatabaseOptions(),
            Broker = broker,
            Trading = configuration.GetSection(TradingOptions.Section).Get<TradingOptions>() ?? new TradingOptions(),
            Security = configuration.GetSection(SecurityOptions.Section).Get<SecurityOptions>() ?? new SecurityOptions(),
            Cors = configuration.GetSection(CorsOptions.Section).Get<CorsOptions>() ?? new CorsOptions(),
            Infrastructure = configuration.GetSection(InfrastructureOptions.Section).Get<InfrastructureOptions>()
                ?? new InfrastructureOptions(),
        };

        RegisterSingletons(builder.Services, bundle, configuration);
        return bundle;
    }

    public static void ApplyBrokerOverrides(MStockOptions mstock, BrokerOptions broker)
    {
        if (!string.IsNullOrWhiteSpace(broker.ApiKey))
        {
            mstock.ApiKey = broker.ApiKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.ApiSecret))
        {
            mstock.Password = broker.ApiSecret.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.ClientCode))
        {
            mstock.Username = broker.ClientCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.AccessToken))
        {
            mstock.AccessToken = broker.AccessToken.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.RestBaseUrl))
        {
            mstock.RestBaseUrl = broker.RestBaseUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.WsUrl))
        {
            mstock.WsUrl = broker.WsUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.TotpSecret))
        {
            mstock.TotpSecret = broker.TotpSecret.Trim();
        }

        if (!string.IsNullOrWhiteSpace(broker.RedirectUrl))
        {
            mstock.RedirectUrl = broker.RedirectUrl.Trim();
        }
    }

    public static bool BrokerCredentialsConfigured(MStockOptions mstock) =>
        !string.IsNullOrWhiteSpace(mstock.ApiKey)
        && (!string.IsNullOrWhiteSpace(mstock.Username) || !string.IsNullOrWhiteSpace(mstock.Password)
            || !string.IsNullOrWhiteSpace(mstock.AccessToken));

    private static void RegisterSingletons(
        IServiceCollection services,
        AppConfigurationBundle bundle,
        IConfiguration configuration)
    {
        services.AddSingleton(bundle.Database);
        services.AddSingleton(bundle.Broker);
        services.AddSingleton(bundle.Trading);
        services.AddSingleton(bundle.Security);
        services.AddSingleton(bundle.Cors);
        services.AddSingleton(bundle.Infrastructure);
        services.AddSingleton(bundle.MStock);
        services.AddSingleton(bundle.LiveTrading);
        services.AddSingleton(bundle.TradingPlatform);
        services.AddSingleton(bundle.SystemIdentity);
        services.AddSingleton(configuration);
    }
}
