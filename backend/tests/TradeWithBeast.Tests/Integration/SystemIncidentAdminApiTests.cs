using System.Net.Http.Headers;
using TradeWithBeast.Tests.TestSupport;

namespace TradeWithBeast.Tests.Integration;

public sealed class SystemIncidentAdminApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemIncidentAdminApiTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Anonymous_incident_api_is_rejected()
    {
        var response = await _client.GetAsync("/api/admin/incidents");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_query_incident_endpoints()
    {
        var token = await TestAuthHelper.LoginAdminAsync(_client, IntegrationTestAppConfiguration.AdminEmail, TestAuthHelper.DefaultAdminPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.True((await _client.GetAsync("/api/admin/incidents")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/incidents/active")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/incidents/statistics")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/alerts")).IsSuccessStatusCode);
    }
}