using System.Net.Http.Headers;
using TradeWithBeast.Tests.TestSupport;

namespace TradeWithBeast.Tests.Integration;

public sealed class SystemNotificationAdminApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemNotificationAdminApiTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Anonymous_notification_api_is_rejected()
    {
        var response = await _client.GetAsync("/api/admin/notifications");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_query_notification_settings_and_policies()
    {
        var token = await TestAuthHelper.LoginAdminAsync(_client, IntegrationTestAppConfiguration.AdminEmail, TestAuthHelper.DefaultAdminPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.True((await _client.GetAsync("/api/admin/notifications")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/notification-settings")).IsSuccessStatusCode);
        Assert.True((await _client.GetAsync("/api/admin/escalation-policies")).IsSuccessStatusCode);
    }
}