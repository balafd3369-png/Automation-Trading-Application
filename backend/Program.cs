using TradeWithBeast.Broker;
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

builder.Services.AddSingleton<MockMarketService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MockMarketService>());
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
