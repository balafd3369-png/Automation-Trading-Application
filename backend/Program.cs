using TradeWithBeast.Broker;

using TradeWithBeast.Configuration;

using TradeWithBeast.Middleware;

using TradeWithBeast.Models;

using TradeWithBeast.Services;







var builder = WebApplication.CreateBuilder(args);



if (builder.Environment.IsDevelopment())

{

    LocalEnvFileLoader.LoadIfPresent(builder.Environment.ContentRootPath);

}



builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);



InstrumentLotMetadata.Configure(builder.Configuration);







TaskScheduler.UnobservedTaskException += (_, e) =>



{



    Console.Error.WriteLine($"Unobserved task error: {e.Exception.Message}");



    e.SetObserved();



};



const int defaultListenPort = 45211;

var listenPort = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var portFromEnv) && portFromEnv > 0

    ? portFromEnv

    : defaultListenPort;

builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");



builder.Services.AddControllers();

builder.Services.AddSignalR(options =>

{

    var infra = builder.Configuration.GetSection(InfrastructureOptions.Section).Get<InfrastructureOptions>()

        ?? new InfrastructureOptions();

    options.EnableDetailedErrors = infra.SignalREnableDetailedErrors

        || builder.Environment.IsDevelopment();

});

builder.Services.AddOutputCache(options =>

{

    options.AddPolicy(ChartOutputCachePolicy.PolicyName, policy =>

    {

        policy.Expire(TimeSpan.FromSeconds(40));

        policy.SetVaryByQuery("interval", "depth");

    });

});

builder.Services.AddSingleton<ChartLiveStreamService>();



var appConfig = AppConfigurationBinder.Bind(builder);



builder.Services.AddHttpClient("mstock", client =>

{

    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(appConfig.Infrastructure.BrokerHttpTimeoutSeconds, 5, 120));

});



builder.Services.AddSingleton<MStockSessionStore>();



builder.Services.AddSingleton<MStockLoginContext>();



builder.Services.AddSingleton<MStockAuthClient>();



builder.Services.AddSingleton<IBrokerMarketClient>(sp => sp.GetRequiredService<MStockAuthClient>());



builder.Services.AddSingleton<MStockOrderClient>();



builder.Services.AddSingleton<IBrokerOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());



builder.Services.AddSingleton<IMStockOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());



builder.Services.AddSingleton<BrokerConnectionManager>();



builder.Services.AddSingleton<EquitySymbolCatalog>();

builder.Services.AddSingleton<MockMarketService>();



builder.Services.AddHostedService(sp => sp.GetRequiredService<MockMarketService>());



builder.Services.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();



builder.Services.AddSingleton<UserIdentityStore>();

builder.Services.AddSingleton<AuthService>();

builder.Services.AddSingleton<MarketDataDistributor>();

builder.Services.AddSingleton<UserDashboardService>();

builder.Services.AddSingleton<AdminPlatformService>();

builder.Services.AddSingleton<DeploymentReadinessService>();



builder.Services.AddSingleton<TradeHistoryStore>();



builder.Services.AddSingleton<MarketContextService>();



builder.Services.AddSingleton<ZoneSessionTracker>();



builder.Services.AddSingleton<ZoneBoardSessionTracker>();



builder.Services.AddSingleton<ZoneBoardBrokerOptionPremiumService>();



builder.Services.AddSingleton<TenAmSessionTracker>();



builder.Services.AddSingleton<StrategyRecommendationEngine>();



builder.Services.AddSingleton<IPaperStrategy, ZoneBoardIntradayStrategy>();



builder.Services.AddSingleton<IPaperStrategy, StockB1RejectionStrategy>();



builder.Services.AddSingleton<IPaperStrategy, TenAmIntradayStrategy>();



builder.Services.AddSingleton<IPaperStrategy, IndexOptionStrategy>();



builder.Services.AddSingleton<IPaperStrategy, StockNakedOptionStrategy>();



builder.Services.AddSingleton<IPaperStrategy, NakedFutureStrategy>();



builder.Services.AddSingleton<PaperDecisionEngine>();



builder.Services.AddSingleton<PortfolioRiskManager>();



builder.Services.AddSingleton<PaperTradingAgent>();



builder.Services.AddSingleton<IPaperProposalSource>(sp => sp.GetRequiredService<PaperTradingAgent>());



builder.Services.AddHostedService(sp => sp.GetRequiredService<PaperTradingAgent>());



builder.Services.AddSingleton<ManualOrderCapitalValidator>();



builder.Services.AddSingleton<ManualTradingService>();



builder.Services.AddSingleton<OperationsStore>();



builder.Services.AddSingleton<BrokerReconciliationService>();



builder.Services.AddHostedService(sp => sp.GetRequiredService<BrokerReconciliationService>());



builder.Services.AddSingleton<OperationsSuiteService>();



builder.Services.AddSingleton<LiveExecutionCoordinator>();



builder.Services.AddSingleton<Lazy<IZoneLiveAutoGateway>>(sp =>



    new Lazy<IZoneLiveAutoGateway>(() => sp.GetRequiredService<LiveExecutionCoordinator>()));



builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveExecutionCoordinator>());



builder.Services.AddCors(options =>

{

    options.AddDefaultPolicy(policy =>

    {

        var origins = appConfig.Cors.AllowedOrigins

            .Where(o => !string.IsNullOrWhiteSpace(o))

            .Select(o => o.Trim())

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToArray();



        if (origins.Length > 0)

        {

            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();

            return;

        }



        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();

    });

});







var app = builder.Build();



var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Deployment");

DeploymentStartupValidator.ValidateForStartup(

    app.Environment,

    appConfig.SystemIdentity,

    appConfig.LiveTrading,

    appConfig.MStock,

    appConfig.Database,

    appConfig.Cors,

    startupLogger);

var readiness = app.Services.GetRequiredService<DeploymentReadinessService>();

DeploymentStartupValidator.LogReadinessSummary(readiness.BuildReport(), startupLogger);



app.UseCors();

app.UseMiddleware<SessionAuthenticationMiddleware>();

app.UseMiddleware<RequireAuthenticationMiddleware>();

app.UseOutputCache();



app.MapControllers();

app.MapHub<TradeWithBeast.Hubs.MarketChartHub>("/hubs/market-chart");



app.MapGet("/", () => Results.Redirect("/api/board"));



app.MapGet("/api/health", () => Results.Ok(new { status = "ok", port = listenPort }));



app.Run();







public partial class Program { }


