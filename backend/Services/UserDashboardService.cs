using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class UserDashboardService
{
    private readonly TradeHistoryStore _history;
    private readonly UserIdentityStore _users;
    private readonly ICurrentUserAccessor _current;

    public UserDashboardService(
        TradeHistoryStore history,
        UserIdentityStore users,
        ICurrentUserAccessor current)
    {
        _history = history;
        _users = users;
        _current = current;
    }

    public UserDashboardSummary GetDashboard()
    {
        var userId = RequireUser();
        var account = _users.GetPaperAccount(userId);
        var now = DateTimeOffset.UtcNow;
        var trades = _history.QueryManualPaperTrades(now.AddYears(-2), now, account.StartingCapital / 10m);
        var open = trades.Where(t => t.Status == "OPEN").ToList();
        var closed = trades.Where(t => t.Status == "CLOSED").ToList();
        var realized = closed.Sum(t => t.Pnl ?? 0);
        var unrealized = open.Sum(t => t.Pnl ?? 0);
        var wins = closed.Count(t => (t.Pnl ?? 0) > 0);
        var winRate = closed.Count > 0 ? wins * 100m / closed.Count : 0;
        var best = closed.Count > 0 ? closed.Max(t => t.Pnl ?? 0) : (decimal?)null;
        var worst = closed.Count > 0 ? closed.Min(t => t.Pnl ?? 0) : (decimal?)null;
        SyncPaperAccount(userId, account, realized, unrealized, closed.Count);
        return new(
            account.StartingCapital,
            account.AvailableCapital,
            open.Count,
            closed.Count,
            trades.Count,
            decimal.Round(winRate, 2),
            realized,
            unrealized,
            best,
            worst,
            DateTimeOffset.UtcNow);
    }

    public UserAnalyticsSummary GetAnalytics()
    {
        var userId = RequireUser();
        var account = _users.GetPaperAccount(userId);
        var now = DateTimeOffset.UtcNow;
        var trades = _history.QueryManualPaperTrades(now.AddYears(-5), now, account.StartingCapital / 10m);
        var closed = trades.Where(t => t.Status == "CLOSED").ToList();
        var wins = closed.Where(t => (t.Pnl ?? 0) > 0).ToList();
        var losses = closed.Where(t => (t.Pnl ?? 0) < 0).ToList();
        var grossWin = wins.Sum(t => t.Pnl ?? 0);
        var grossLoss = Math.Abs(losses.Sum(t => t.Pnl ?? 0));
        decimal? profitFactor = grossLoss > 0 ? grossWin / grossLoss : null;
        var byDay = closed
            .Where(t => t.ClosedAt is not null)
            .GroupBy(t => t.ClosedAt!.Value.UtcDateTime.Date)
            .Select(g => g.Sum(t => t.Pnl ?? 0))
            .ToList();
        return new(
            trades.Count,
            closed.Count > 0 ? decimal.Round(wins.Count * 100m / closed.Count, 2) : 0,
            profitFactor is null ? null : decimal.Round(profitFactor.Value, 2),
            closed.Sum(t => t.Pnl ?? 0),
            closed.Count > 0 ? closed.Max(t => t.Pnl ?? 0) : null,
            closed.Count > 0 ? closed.Min(t => t.Pnl ?? 0) : null,
            byDay.Count > 0 ? byDay.Max() : null,
            byDay.Count > 0 ? byDay.Min() : null);
    }

    private void SyncPaperAccount(
        string userId,
        PaperAccountRecord account,
        decimal realized,
        decimal unrealized,
        int totalTrades)
    {
        var deployed = _history.SumOpenManualDeployedCapital();
        var available = Math.Max(0, account.StartingCapital + realized - deployed);
        _users.UpdatePaperAccount(account with
        {
            AvailableCapital = available,
            RealizedPnL = realized,
            UnrealizedPnL = unrealized,
            TotalTrades = totalTrades,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    private string RequireUser()
    {
        if (string.IsNullOrWhiteSpace(_current.UserId))
        {
            throw new InvalidOperationException("User context required.");
        }

        return _current.UserId;
    }
}
