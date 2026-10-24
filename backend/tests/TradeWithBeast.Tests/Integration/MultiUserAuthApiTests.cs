using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Integration;

public sealed class MultiUserAuthApiTests : IClassFixture<AuthRequiredWebApplicationFactory>
{
    private readonly HttpClient _client;
    public MultiUserAuthApiTests(AuthRequiredWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Anonymous_user_dashboard_returns_401()
    {
        var response = await _client.GetAsync("/api/user/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Trader_cannot_access_admin_dashboard()
    {
        var token = await RegisterTraderAsync($"trader-{Guid.NewGuid():N}@test.local");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/dashboard");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_trader_can_load_user_dashboard()
    {
        var token = await RegisterTraderAsync($"ok-{Guid.NewGuid():N}@test.local");
        var dash = await GetDashboardAsync(token);
        Assert.Equal(100_000m, dash.GetProperty("paperCapital").GetDecimal());
        Assert.Equal(0, dash.GetProperty("tradeCount").GetInt32());
    }

    private async Task<string> RegisterTraderAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new AuthRegisterRequest(email, "password-123", "Trader"));
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        return session!.Token;
    }

    private async Task<JsonElement> GetDashboardAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/dashboard");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

public sealed class AuthRequiredWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _historyPath = Path.Combine(
        Path.GetTempPath(),
        "trade-with-beast-auth-tests",
        $"trade-history-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("TradeHistory:DatabasePath", _historyPath);
        builder.UseSetting("LiveTrading:Enabled", "false");
        builder.UseSetting("SystemIdentity:RequireAuthentication", "true");
        builder.UseSetting("SystemIdentity:AllowAnonymousLocalUser", "false");
        builder.UseSetting("SystemIdentity:AdminEmail", "admin@test.local");
        builder.UseSetting("SystemIdentity:AdminPassword", TestAuthHelper.DefaultAdminPassword);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<MStockSessionStore>();
            var sessionPath = Path.Combine(Path.GetTempPath(), "trade-with-beast-auth-tests", "mstock-session.json");
            services.AddSingleton(_ => new MStockSessionStore(sessionPath));
        });
    }
}
