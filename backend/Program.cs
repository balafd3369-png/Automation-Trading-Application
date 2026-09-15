using TradeWithBeast.Broker;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);

builder.WebHost.UseUrls("http://0.0.0.0:45211");
builder.Services.AddControllers();

var mstock = builder.Configuration.GetSection(MStockOptions.Section).Get<MStockOptions>() ?? new MStockOptions();
builder.Services.AddSingleton(mstock);
builder.Services.AddSingleton<MStockSessionStore>();
builder.Services.AddHttpClient("mstock");
builder.Services.AddSingleton<MStockAuthClient>();
builder.Services.AddSingleton<MStockOrderClient>();
builder.Services.AddSingleton<IMStockOrderClient>(sp => sp.GetRequiredService<MStockOrderClient>());
var liveTrading = builder.Configuration.GetSection(LiveExecutionOptions.Section).Get<LiveExecutionOptions>()
    ?? new LiveExecutionOptions();
builder.Services.AddSingleton(liveTrading);

builder.Services.AddSingleton<MockMarketService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MockMarketService>());
builder.Services.AddSingleton<TradeHistoryStore>();
builder.Services.AddSingleton<MarketContextService>();
builder.Services.AddSingleton<IPaperStrategy, StockZoneLongStrategy>();
builder.Services.AddSingleton<IPaperStrategy, IndexOptionStrategy>();
builder.Services.AddSingleton<PaperDecisionEngine>();
builder.Services.AddSingleton<PortfolioRiskManager>();
builder.Services.AddSingleton<PaperTradingAgent>();
builder.Services.AddSingleton<IPaperProposalSource>(sp => sp.GetRequiredService<PaperTradingAgent>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PaperTradingAgent>());
builder.Services.AddSingleton<LiveExecutionCoordinator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveExecutionCoordinator>());
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/api/board"));
app.Run();

public partial class Program { }
