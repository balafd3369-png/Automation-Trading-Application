using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Models;
using TradeWithBeast.Configuration;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class MultiUserIsolationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"multi-user-{Guid.NewGuid():N}.db");

    [Fact]
    public void User_A_cannot_see_User_B_manual_positions()
    {
        var store = CreateHistoryStore();
        SeedManualTrade(store, "user-a", "SYM-A");
        SeedManualTrade(store, "user-b", "SYM-B");

        using (store.RunAsUser("user-a"))
        {
            var trades = store.QueryManualPaperTrades(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1),
                10_000);
            Assert.All(trades, t => Assert.Equal("SYM-A", t.Underlying));
            Assert.DoesNotContain(trades, t => t.Underlying == "SYM-B");
        }
    }

    [Fact]
    public void User_A_cannot_see_User_B_notifications()
    {
        var config = Config();
        var accessor = new CurrentUserAccessor();
        var ops = new OperationsStore(
            config,
            new DatabaseOptions(),
            accessor,
            new SystemIdentityOptions { AllowAnonymousLocalUser = true },
            NullLogger<OperationsStore>.Instance);
        accessor.SetUser("user-a", UserRoles.Trader);
        ops.AppendNotification("info", "Test", "A only");
        accessor.SetUser("user-b", UserRoles.Trader);
        ops.AppendNotification("info", "Test", "B only");

        accessor.SetUser("user-a", UserRoles.Trader);
        var listA = ops.ListNotifications(20);
        accessor.SetUser("user-b", UserRoles.Trader);
        var listB = ops.ListNotifications(20);
        Assert.Contains(listA, n => n.Message == "A only");
        Assert.DoesNotContain(listA, n => n.Message == "B only");
        Assert.Contains(listB, n => n.Message == "B only");
        Assert.DoesNotContain(listB, n => n.Message == "A only");
    }

    [Fact]
    public void Admin_can_list_all_users()
    {
        var users = new UserIdentityStore(
            Config(),
            new DatabaseOptions(),
            new SystemIdentityOptions { AdminEmail = $"admin-{Guid.NewGuid():N}@test.local" },
            NullLogger<UserIdentityStore>.Instance);
        users.CreateUser($"trader-{Guid.NewGuid():N}@test.local", PasswordHasher.Hash("password-123"), "Trader", UserRoles.Trader);
        Assert.True(users.ListUsers().Count >= 2);
    }

    [Fact]
    public void User_A_cannot_load_User_B_manual_order_by_id()
    {
        var store = CreateHistoryStore();
        string? orderBId = null;
        using (store.RunAsUser("user-b"))
        {
            store.UpsertManualOrder(new ManualOrderSnapshot(
                "order-b",
                "PAPER",
                TradeSourceWire.Manual,
                "PENDING",
                "RELIANCE",
                "RELIANCE",
                "STOCK",
                "BUY",
                "LIMIT",
                "MIS",
                "NSE",
                10,
                100m,
                0,
                0,
                "",
                "",
                "",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
            orderBId = "order-b";
        }

        using (store.RunAsUser("user-a"))
        {
            Assert.Null(store.GetManualOrder(orderBId!));
        }
    }

    [Fact]
    public void User_A_cannot_see_User_B_journal_notes()
    {
        var config = Config();
        var accessor = new CurrentUserAccessor();
        var ops = new OperationsStore(
            config,
            new DatabaseOptions(),
            accessor,
            new SystemIdentityOptions { AllowAnonymousLocalUser = true },
            NullLogger<OperationsStore>.Instance);
        accessor.SetUser("user-a", UserRoles.Trader);
        ops.SaveJournalNotes("trade-1", "secret A");
        accessor.SetUser("user-b", UserRoles.Trader);
        ops.SaveJournalNotes("trade-1", "secret B");
        accessor.SetUser("user-a", UserRoles.Trader);
        Assert.Equal("secret A", ops.GetJournalNotes("trade-1"));
        accessor.SetUser("user-b", UserRoles.Trader);
        Assert.Equal("secret B", ops.GetJournalNotes("trade-1"));
    }

    [Fact]
    public void Trader_role_is_not_admin()
    {
        ICurrentUserAccessor accessor = new CurrentUserAccessor();
        accessor.SetUser("x", UserRoles.Trader);
        Assert.False(accessor.IsAdmin);
    }

    private void SeedManualTrade(TradeHistoryStore store, string userId, string symbol)
    {
        using (store.RunAsUser(userId))
        {
            var proposal = new PaperTradeProposal(
                $"{userId}-trade",
                "STOCK",
                symbol,
                "LONG",
                10,
                100m,
                90m,
                110m,
                "test",
                DateTimeOffset.UtcNow.AddHours(1),
                "MANUAL",
                symbol,
                0,
                0,
                0,
                1000,
                0,
                null,
                [new($"{userId}-leg", symbol, symbol, "STOCK", "BUY", 1, 10, 100m)]);
            var legs = new List<PaperPositionLegSnapshot>
            {
                new($"{userId}-leg", symbol, "BUY", 10, 100m, 100m, 0m),
            };
            store.SaveManualPaperTradeOpen(proposal, legs, DateTimeOffset.UtcNow);
        }
    }

    private TradeHistoryStore CreateHistoryStore() =>
        new(Config(), new DatabaseOptions(), new CurrentUserAccessor(), new SystemIdentityOptions(), NullLogger<TradeHistoryStore>.Instance);

    private IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TradeHistory:DatabasePath"] = _dbPath })
            .Build();

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
