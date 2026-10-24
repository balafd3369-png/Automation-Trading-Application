using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class AdminPlatformService
{
    private readonly UserIdentityStore _users;
    private readonly TradeHistoryStore _history;
    private readonly BrokerConnectionManager _brokers;
    private readonly MarketDataDistributor _market;

    public AdminPlatformService(
        UserIdentityStore users,
        TradeHistoryStore history,
        BrokerConnectionManager brokers,
        MarketDataDistributor market)
    {
        _users = users;
        _history = history;
        _brokers = brokers;
        _market = market;
    }

    public AdminPlatformDashboard GetDashboard()
    {
        var allUsers = _users.ListUsers();
        var today = DateTimeOffset.UtcNow.Date;
        var loginsToday = _users.ListLoginHistory(500).Count(e => e.At.UtcDateTime.Date == today);
        var paperToday = _history.CountManualPaperTradesOnDate(DateTimeOffset.UtcNow);
        var openPositions = _history.CountOpenManualPaperPositionsGlobal();
        var top = BuildTopUsers(allUsers);
        var feed = _market.GetStatus();
        return new(
            allUsers.Count,
            allUsers.Count(u => u.IsActive),
            _users.CountOnlineUsers(TimeSpan.FromMinutes(15)),
            loginsToday,
            paperToday,
            openPositions,
            feed.AdminBrokerConnected,
            feed.AdminBrokerConnected ? "Connected" : "Disconnected",
            top,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<AdminUserActivityRow> GetUserActivity() => _users.ListUserActivity();

    public IReadOnlyList<UserLoginHistoryEntry> GetLoginHistory(int limit = 200) =>
        _users.ListLoginHistory(limit);

    private IReadOnlyList<AdminTopUserRow> BuildTopUsers(IReadOnlyList<UserRecord> users)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<AdminTopUserRow>();
        foreach (var user in users)
        {
            var account = _users.GetPaperAccount(user.Id);
            using (_history.RunAsUser(user.Id))
            {
                var trades = _history.QueryManualPaperTrades(now.AddMonths(-3), now, account.StartingCapital / 10m);
                var closed = trades.Where(t => t.Status == "CLOSED").ToList();
                rows.Add(new(
                    user.Id,
                    user.DisplayName,
                    user.Email,
                    closed.Sum(t => t.Pnl ?? 0),
                    trades.Count));
            }
        }

        return rows.OrderByDescending(r => r.NetPnL).Take(10).ToList();
    }
}
