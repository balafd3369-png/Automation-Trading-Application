using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TradeWithBeast.Models;

namespace TradeWithBeast.Tests.Integration;

public sealed class BoardApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BoardApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBoard_returns_symbols_and_awaiting_feed()
    {
        using var response = await _client.GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var board = await response.Content.ReadFromJsonAsync<BoardResponse>();
        Assert.NotNull(board);
        Assert.NotEmpty(board!.Symbols);
        Assert.Contains(board.Symbols, s => s.Name.Equals("NIFTY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("m.Stock", board.Feed, StringComparison.OrdinalIgnoreCase);
        Assert.True(board.Counts.Buying + board.Counts.Selling + board.Counts.RangeBound > 0);
    }

    [Fact]
    public async Task GetChart_for_nifty_returns_candles_and_zone_lines()
    {
        using var response = await _client.GetAsync("/api/board/chart/NIFTY?interval=5minute");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chart = await response.Content.ReadFromJsonAsync<ChartResponse>();
        Assert.NotNull(chart);
        Assert.Equal("NIFTY", chart!.Name, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(chart.Candles);
        Assert.True(chart.Last > 0);
        Assert.True(chart.B1 > chart.S1);
    }

    [Fact]
    public async Task GetChart_for_unknown_symbol_returns_not_found()
    {
        using var response = await _client.GetAsync("/api/board/chart/NOT_A_SYMBOL?interval=day");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBrokerSession_returns_disconnected_without_otp()
    {
        using var response = await _client.GetAsync("/api/broker/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("m.Stock Type A", doc.RootElement.GetProperty("broker").GetString());
        Assert.True(doc.RootElement.TryGetProperty("availableFunds", out var available));
        Assert.Equal(JsonValueKind.Null, available.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("utilizedFunds", out var utilized));
        Assert.Equal(JsonValueKind.Null, utilized.ValueKind);
    }

    [Fact]
    public async Task Board_symbols_include_pressure_mark()
    {
        var board = await _client.GetFromJsonAsync<BoardResponse>("/api/board");
        Assert.NotNull(board);
        var nifty = board!.Symbols.First(s => s.Name.Equals("NIFTY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nifty.Mark, new[] { "BUYING", "SELLING", "RANGE BOUND" });
        Assert.True(nifty.B2 >= nifty.S2);
    }
}
