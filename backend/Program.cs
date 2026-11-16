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



if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
}



InstrumentLotMetadata.Configure(builder.Configuration);







SystemHealthExceptionTracker? startupExceptionTracker = null;
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.Error.WriteLine($"Unobserved task error: {e.Exception.Message}");
    startupExceptionTracker?.RecordUnobservedTaskException();
    e.SetObserved();
};



const int defaultListenPort = 45211;

var listenPort = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var portFromEnv) && portFromEnv > 0

    ? portFromEnv

    : defaultListenPort;

builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");



builder.Services.AddControllers();
builder.Services.AddMemoryCache();

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

builder.Services.AddSingleton<SignalRServerHealthTracker>();
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
builder.Services.AddSingleton<LiveTradingEmergencyGate>();
builder.Services.AddSingleton(sp => new Lazy<BrokerConnectionManager>(() => sp.GetRequiredService<BrokerConnectionManager>()));
builder.Services.AddSingleton<BrokerCircuitBreaker>();
builder.Services.AddSingleton<TradingSafetyStore>();
builder.Services.AddSingleton<LiveTradingExecutionSafetyService>();
builder.Services.AddSingleton<BrokerLiveOrderSettlement>();

builder.Services.AddSingleton<IBrokerOrderClient>(sp =>
{
    var inner = sp.GetRequiredService<MStockOrderClient>();
    var breaker = sp.GetRequiredService<BrokerCircuitBreaker>();
    return new CircuitBreakerBrokerOrderClient(inner, breaker);
});



builder.Services.AddSingleton<IMStockOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());



builder.Services.AddSingleton<BrokerConnectionManager>();



builder.Services.AddSingleton<EquitySymbolCatalog>();

builder.Services.AddSingleton<MockMarketService>();
builder.Services.AddSingleton<MStockMarketDataWebSocketHealth>();



builder.Services.AddHostedService(sp => sp.GetRequiredService<MockMarketService>());
builder.Services.AddHostedService<MStockMarketDataWebSocketService>();
builder.Services.AddHostedService<OptionChartLiveHostedService>();



builder.Services.AddSingleton<ICurrentUserAccessor, CurrentUserAccessor>();



builder.Services.AddSingleton<UserIdentityStore>();

builder.Services.AddSingleton<AuthService>();

builder.Services.AddSingleton<MarketDataDistributor>();

builder.Services.AddSingleton<UserDashboardService>();

builder.Services.AddSingleton<AdminPlatformService>();

builder.Services.AddSingleton<DeploymentReadinessService>();

builder.Services.AddSingleton<SystemHealthExceptionTracker>();
builder.Services.AddSingleton<WorkerHeartbeatRegistry>();
builder.Services.AddSingleton<SystemHealthStore>();
builder.Services.AddSingleton<ISystemHealthBoardProbeSource>(sp => sp.GetRequiredService<MockMarketService>());
builder.Services.AddSingleton<SystemHealthInfrastructureProbes>();
builder.Services.AddSingleton<SystemHealthService>();
builder.Services.AddHostedService<SystemHealthMonitoringHostedService>();
builder.Services.AddSingleton<SystemIncidentDeduplicationService>();
builder.Services.AddSingleton<SystemIncidentStore>();
builder.Services.AddSingleton<SystemIncidentLifecycleService>();
builder.Services.AddSingleton<SystemIncidentEvaluator>();
builder.Services.AddSingleton<IIncidentAlertDispatcher, DatabaseIncidentAlertDispatcher>();
builder.Services.AddSingleton<SystemIncidentAlertService>();
builder.Services.AddSingleton<SystemIncidentService>();
builder.Services.AddHostedService<SystemIncidentEvaluationHostedService>();
builder.Services.AddSingleton<SystemIncidentNotificationStore>();
builder.Services.AddSingleton<SystemIncidentNotificationSettingsService>();
builder.Services.AddSingleton<SystemIncidentNotificationRouter>();
builder.Services.AddSingleton<IncidentNotificationContentBuilder>();
builder.Services.AddSingleton<SystemIncidentEscalationService>();
builder.Services.AddSingleton<SystemIncidentNotificationService>();
builder.Services.AddSingleton<SystemIncidentNotificationProcessor>();
builder.Services.AddSingleton<IIncidentNotificationProvider, SmtpEmailNotificationProvider>();
builder.Services.AddSingleton<IIncidentNotificationProvider, TelegramBotNotificationProvider>();
builder.Services.AddHttpClient(nameof(TelegramBotNotificationProvider));
builder.Services.AddHostedService<SystemIncidentNotificationHostedService>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemCoordinationStore>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemInstanceRegistryService>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemLeaderElectionService>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemRecoveryPolicyService>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemRecoveryService>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.SystemWorkerRegistryPublisher>();
builder.Services.AddSingleton<TradeWithBeast.Services.Coordination.IDistributedLockService, TradeWithBeast.Services.Coordination.SqliteDistributedLockService>();
builder.Services.AddHostedService<TradeWithBeast.Services.Coordination.SystemReliabilityCoordinatorHostedService>();
builder.Services.AddSingleton<ProductionSafetyStore>();
builder.Services.AddSingleton<SqliteDatabaseBackupService>();
builder.Services.AddSingleton<OrderStartupSafetyScanner>();
builder.Services.AddSingleton<TradingSafetyIncidentEvaluator>();
builder.Services.AddSingleton<ProductionSafetyIncidentEvaluator>();
builder.Services.AddSingleton<IProductionSafetyIncidentEvaluator, CompositeProductionSafetyIncidentEvaluator>();
builder.Services.AddHostedService<OrderStatusPollingHostedService>();
builder.Services.AddSingleton<ProductionSafetyStatusService>();
builder.Services.AddHostedService<ProductionSafetyHostedService>();



builder.Services.AddSingleton<TradeHistoryStore>();



builder.Services.AddSingleton<MarketContextService>();



builder.Services.AddSingleton<ZoneSessionTracker>();



builder.Services.AddSingleton<ZoneBoardSessionTracker>();



builder.Services.AddSingleton<ZoneBoardBrokerOptionPremiumService>();



builder.Services.AddSingleton<TenAmSessionTracker>();

builder.Services.AddSingleton<GapSessionTracker>();

builder.Services.AddSingleton<ZoneAnalyzerSignalStore>();
builder.Services.AddSingleton<ZoneAnalyzerDetailService>();
builder.Services.AddSingleton<ZoneStockAnalyzerService>();
builder.Services.AddSingleton<GapAnalyzerBenchmarkCache>();
builder.Services.AddSingleton<GapAnalyzerService>();
builder.Services.AddSingleton<TrapFallFormationService>();

builder.Services.AddSingleton<StrategyRecommendationEngine>();



builder.Services.AddSingleton<IPaperStrategy, ZoneBoardIntradayStrategy>();



builder.Services.AddSingleton<IPaperStrategy, StockB1RejectionStrategy>();

builder.Services.AddSingleton<IPaperStrategy, GapFailedRallyPaperStrategy>();

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

startupExceptionTracker = app.Services.GetRequiredService<SystemHealthExceptionTracker>();



var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Deployment");

DeploymentStartupValidator.ValidateForStartup(

    app.Environment,

    appConfig.SystemIdentity,

    appConfig.LiveTrading,

    appConfig.MStock,

    appConfig.Database,

    appConfig.Cors,

    appConfig.Infrastructure,

    appConfig.ProductionSafety,

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



app.MapGet("/api/health", (
    SystemHealthService health,
    SystemMonitoringOptions monitoring,
    SystemHealthStore store) =>
{
    var payload = new Dictionary<string, object?>
    {
        ["status"] = "ok",
        ["port"] = listenPort,
        ["instanceId"] = health.GetLiveSnapshot().InstanceId,
    };

    if (monitoring.Enabled && monitoring.PublicHealthIncludeDatabaseProbe)
    {
        var db = store.ProbeDatabase();
        payload["database"] = new
        {
            healthy = db.CanConnect && db.CanRead && db.CanWrite,
            latencyMs = db.LatencyMs,
        };
        if (!db.CanConnect)
        {
            payload["status"] = "degraded";
        }
    }

    return Results.Ok(payload);
});



app.Run();







public partial class Program { }


