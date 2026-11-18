using System.Net;
using System.Net.Http.Json;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Integration;

public sealed class BrokerAuthorizationApiTests : IClassFixture<AuthRequiredWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BrokerAuthorizationApiTests(AuthRequiredWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Trader_broker_otp_send_returns_403()
    {
        var token = await RegisterTraderAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/broker/otp/send");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { username = "x", password = "y" });
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Trader_broker_session_clear_returns_403()
    {
        var token = await RegisterTraderAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/broker/session/clear");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Trader_can_read_shared_broker_session_status()
    {
        var token = await RegisterTraderAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/broker/session");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> RegisterTraderAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new AuthRegisterRequest($"trader-{Guid.NewGuid():N}@test.local", "password-123", "Trader"));
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        return session!.Token;
    }
}