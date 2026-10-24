using TradeWithBeast.Broker;
using TradeWithBeast.Middleware;
using TradeWithBeast.Models;
using TradeWithBeast.Services;



var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSignalR();
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(ChartOutputCachePolicy.PolicyName, policy =>
    {
        policy.Expire(TimeSpan.FromSeconds(40));
        policy.SetVaryByQuery("interval", "depth");
    });
});
builder.Services.AddSingleton<ChartLiveStreamService>();



var mstock = builder.Configuration.GetSection(MStockOptions.Section).Get<MStockOptions>() ?? new MStockOptions();

builder.Services.AddSingleton(mstock);

builder.Services.AddSingleton<MStockSessionStore>();

builder.Services.AddSingleton<MStockLoginContext>();

builder.Services.AddHttpClient("mstock", client => client.Timeout = TimeSpan.FromSeconds(35));

builder.Services.AddSingleton<MStockAuthClient>();

builder.Services.AddSingleton<IBrokerMarketClient>(sp => sp.GetRequiredService<MStockAuthClient>());

builder.Services.AddSingleton<MStockOrderClient>();

builder.Services.AddSingleton<IBrokerOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());

builder.Services.AddSingleton<IMStockOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());

builder.Services.AddSingleton<BrokerConnectionManager>();



var liveTrading = builder.Configuration.GetSection(LiveExecutionOptions.Section).Get<LiveExecutionOptions>()

    ?? new LiveExecutionOptions();

builder.Services.AddSingleton(liveTrading);

var tradingPlatform = builder.Configuration.GetSection(TradingPlatformOptions.Section).Get<TradingPlatformOptions>()
    ?? new TradingPlatformOptions();
builder.Services.AddSingleton(tradingPlatform);

builder.Services.AddSingleton<EquitySymbolCatalog>();
builder.Services.AddSingleton<MockMarketService>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<MockMarketService>());

var systemIdentity = builder.Configuration.GetSection(SystemIdentityOptions.Section).Get<SystemIdentityOptions>()
    ?? new SystemIdentityOptions();
builder.Services.AddSingleton(systemIdentity);
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

        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

});



var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Deployment");
DeploymentStartupValidator.ValidateForStartup(
    app.Environment,
    systemIdentity,
    liveTrading,
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


