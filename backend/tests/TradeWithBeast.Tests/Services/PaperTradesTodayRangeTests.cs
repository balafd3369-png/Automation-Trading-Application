using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class PaperTradesTodayRangeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"today-range-{Guid.NewGuid():N}.db");

    [Fact]
    public void Today_range_falls_back_to_previous_ist_session_when_current_day_empty()
    {
        var store = CreateStore();
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var sessionLocal = new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Unspecified);
        var opened = new DateTimeOffset(sessionLocal, ist.GetUtcOffset(sessionLocal));
        var expiry = opened.AddDays(2);
        var proposal = new PaperTradeProposal(
            "zone-today-fallback",
            "MULTI_LEG",
            "NIFTY",
            "BULLISH",
            65,
            40m,
            20m,
            80m,
            "B1 break test",
            opened.AddMinutes(1),
            ZoneBoardPattern.B1BreakLong,
            "NIFTY",
            90m,
            2_600m,
            3_900m,
            50_000m,
            1.5m,
            ["breadth ok"],
            [new("zone-today-fallback-leg", "NIFTY CE", "NIFTY", "OPTION", "BUY", 1, 65, 100m, 25000m, "CE", expiry)],
            opened);

        using (store.RunAsUser(TradeHistoryStore.LocalUserId))
        {
            store.SaveMultiProposal(proposal);
            store.OpenMultiLegAtomic(
                proposal.Id,
                [new PaperPositionLegSnapshot("zone-today-fallback-leg", "NIFTY CE", "BUY", 65, 101m, 101m, 0, 25000m, "CE", expiry)],
                opened);
            FixTradeTimestamps(_dbPath, proposal.Id, opened);
        }

        var nowLocal = new DateTime(2026, 10, 31, 0, 30, 0, DateTimeKind.Unspecified);
        var now = new DateTimeOffset(nowLocal, ist.GetUtcOffset(nowLocal));
        PaperTradeHistoryResponse response;
        using (store.RunAsUser(TradeHistoryStore.LocalUserId))
        {
            response = store.QueryPaperTrades("today", now, 10_000m);
        }

        Assert.NotNull(response.RangeNote);
        Assert.Contains("2026-10-30", response.RangeNote!, StringComparison.Ordinal);
        Assert.Contains(response.Trades, t => t.Strategy == ZoneBoardPattern.B1BreakLong);
    }

    private static void FixTradeTimestamps(string dbPath, string tradeId, DateTimeOffset opened)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        var text = opened.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "UPDATE paper_trades SET created_at=$at, opened_at=$at, updated_at=$at WHERE id=$id;";
        command.Parameters.AddWithValue("$at", text);
        command.Parameters.AddWithValue("$id", tradeId);
        command.ExecuteNonQuery();
    }

    private TradeHistoryStore CreateStore()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TradeHistory:DatabasePath"] = _dbPath,
                ["SystemIdentity:AllowAnonymousLocalUser"] = "true",
            })
            .Build();
        return new TradeHistoryStore(config, NullLogger<TradeHistoryStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // ignore
        }
    }
}
