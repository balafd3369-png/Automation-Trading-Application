using System.Net.Http.Headers;
using System.Net.Http.Json;
using TradeWithBeast.Tests.TestSupport;

namespace TradeWithBeast.Tests.Integration;

public sealed class SystemHealthAdminApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemHealthAdminApiTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Anonymous_requests_to_admin_system_health_are_rejected()
    {
        var response = await _client.GetAsync("/api/admin/system-health/live");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_read_phase2_health_endpoints()
    {
        var token = await TestAuthHelper.LoginAdminAsync(_client, IntegrationTestAppConfiguration.AdminEmail, TestAuthHelper.DefaultAdminPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.True((await _client.GetAsync("/api/admin/system-health/live")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/brokers")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/market-data")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/signalr")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/workers")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/components")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/system-health/components/database")).IsSuccessStatusCode);
        Assert.True((await _client.PostAsync("/api/admin/system-health/reports/comprehensive", null)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Phase1_live_endpoint_schema_remains_available()
    {
        var token = await TestAuthHelper.LoginAdminAsync(_client, IntegrationTestAppConfiguration.AdminEmail, TestAuthHelper.DefaultAdminPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = await _client.GetFromJsonAsync<Phase1LiveShape>("/api/admin/system-health/live");
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.InstanceId));
        Assert.NotNull(payload.Database);
        Assert.NotNull(payload.Workers);
    }

    private sealed class Phase1LiveShape
    {
        public string InstanceId { get; set; } = "";
        public object? Database { get; set; }
        public object[]? Workers { get; set; }
    }
}