using System.Net.Http.Json;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Integration;

public sealed class PaperAgentApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PaperAgentApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Snapshot_starts_stopped_with_ten_lakh_allocation()
    {
        var snapshot = await _client.GetFromJsonAsync<PaperAgentSnapshot>("/api/paper-agent");

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.PaperMode);
        Assert.False(snapshot.Running);
        Assert.Equal(1_000_000m, snapshot.Config!.AllocatedCapital);
    }

    [Fact]
    public async Task Confirm_without_pending_trade_does_not_execute()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/paper-agent/command",
            new PaperAgentCommandRequest("confirm"));
        var snapshot = await response.Content.ReadFromJsonAsync<PaperAgentSnapshot>();

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.PendingTrade);
        Assert.Empty(snapshot.Positions);
        Assert.Contains("no pending trade", snapshot.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configuration_starts_scanning_but_does_not_open_unapproved_trade()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/paper-agent/command",
            new PaperAgentCommandRequest("use 1 lakh, target 5000, max loss 1500"));
        var snapshot = await response.Content.ReadFromJsonAsync<PaperAgentSnapshot>();

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Running);
        Assert.Empty(snapshot.Positions);
        Assert.Null(snapshot.PendingTrade);

        await _client.PostAsJsonAsync(
            "/api/paper-agent/command",
            new PaperAgentCommandRequest("stop paper agent"));
    }

    [Fact]
    public async Task History_returns_local_user_and_requested_range()
    {
        var history = await _client.GetFromJsonAsync<TradeHistoryResponse>(
            "/api/paper-agent/history?range=3m");

        Assert.NotNull(history);
        Assert.Equal("local-user", history!.UserId);
        Assert.Equal("3m", history.Range);
    }

    [Theory]
    [InlineData("/api/paper-agent/decisions")]
    [InlineData("/api/paper-agent/market-context")]
    [InlineData("/api/paper-agent/positions")]
    [InlineData("/api/paper-agent/events")]
    [InlineData("/api/paper-agent/performance")]
    [InlineData("/api/paper-agent/trades?range=1m")]
    public async Task Extended_paper_agent_contracts_are_available(string path)
    {
        using var response = await _client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Live_execution_is_default_off_and_cannot_be_armed()
    {
        var snapshot = await _client.GetFromJsonAsync<LiveExecutionSnapshot>("/api/live-agent");
        Assert.NotNull(snapshot);
        Assert.False(snapshot!.FeatureEnabled);
        Assert.False(snapshot.Armed);

        using var response = await _client.PostAsJsonAsync(
            "/api/live-agent/arm",
            new ArmLiveRequest(LiveExecutionCoordinator.ArmAcknowledgement));
        response.EnsureSuccessStatusCode();
        var after = await response.Content.ReadFromJsonAsync<LiveExecutionSnapshot>();
        Assert.NotNull(after);
        Assert.False(after!.Armed);
        Assert.Contains("disabled", after.ReconciliationStatus, StringComparison.OrdinalIgnoreCase);
    }
}
