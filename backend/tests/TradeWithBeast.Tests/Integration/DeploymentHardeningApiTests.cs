using System.Net;
using System.Net.Http.Json;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Integration;

public sealed class DeploymentHardeningApiTests : IClassFixture<AuthRequiredWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DeploymentHardeningApiTests(AuthRequiredWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Platform_features_reports_live_trading_disabled()
    {
        var features = await _client.GetFromJsonAsync<PlatformFeaturesResponse>("/api/platform/features");
        Assert.NotNull(features);
        Assert.False(features!.LiveTradingEnabled);
    }

    [Fact]
    public async Task Trader_cannot_post_paper_agent_command()
    {
        var email = $"trader-{Guid.NewGuid():N}@test.local";
        var register = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new AuthRegisterRequest(email, "password-123", "Trader"));
        register.EnsureSuccessStatusCode();
        var session = await register.Content.ReadFromJsonAsync<AuthSessionResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/paper-agent/command");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.Token);
        request.Content = JsonContent.Create(new PaperAgentCommandRequest("confirm"));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_deployment_readiness_is_green_when_configured()
    {
        var token = await TestAuthHelper.LoginAdminAsync(
            _client,
            "admin@test.local",
            TestAuthHelper.DefaultAdminPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/deployment-readiness");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<DeploymentReadinessReport>();
        Assert.NotNull(report);
        Assert.NotEqual(DeploymentOverallStatus.Red, report!.Overall);
        Assert.Contains(report.Checks, c => c.Id == "live_trading" && c.Status == DeploymentCheckStatus.Pass);
        Assert.Contains(report.Checks, c => c.Id == "authentication" && c.Status == DeploymentCheckStatus.Pass);
    }

    private sealed record PlatformFeaturesResponse(bool LiveTradingEnabled);
}
