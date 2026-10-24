namespace TradeWithBeast.Models;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Trader = "Trader";
    public const string Viewer = "Viewer";
}

public sealed record UserRecord(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsActive);

public sealed record UserSession(
    string Token,
    string UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record PaperAccountRecord(
    string UserId,
    decimal StartingCapital,
    decimal AvailableCapital,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    int TotalTrades,
    DateTimeOffset UpdatedAt);

public sealed record AuthUserView(
    string Id,
    string Email,
    string DisplayName,
    string Role);

public sealed record AuthLoginRequest(string Email, string Password);
public sealed record AuthRegisterRequest(string Email, string Password, string DisplayName);
public sealed record AuthChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record AuthSessionResponse(string Token, DateTimeOffset ExpiresAt, AuthUserView User);

public sealed record UserDashboardSummary(
    decimal PaperCapital,
    decimal AvailableCapital,
    int OpenPositions,
    int ClosedPositions,
    int TradeCount,
    decimal WinRate,
    decimal RealizedPnL,
    decimal UnrealizedPnL,
    decimal? BestTradePnL,
    decimal? WorstTradePnL,
    DateTimeOffset UpdatedAt);

public sealed record UserAnalyticsSummary(
    int TotalTrades,
    decimal WinRate,
    decimal? ProfitFactor,
    decimal NetPnL,
    decimal? BestTradePnL,
    decimal? WorstTradePnL,
    decimal? BestDayPnL,
    decimal? WorstDayPnL);

public sealed record AdminPlatformDashboard(
    int TotalUsers,
    int ActiveUsers,
    int OnlineUsers,
    int TodaysLogins,
    int PaperTradesToday,
    int OpenPaperPositions,
    bool BrokerConnected,
    string BrokerStatus,
    IReadOnlyList<AdminTopUserRow> TopUsers,
    DateTimeOffset UpdatedAt);

public sealed record AdminTopUserRow(
    string UserId,
    string DisplayName,
    string Email,
    decimal NetPnL,
    int TradeCount);

public sealed record AdminUserActivityRow(
    string UserId,
    string Email,
    string DisplayName,
    string Role,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LastActivityAt,
    int RecentTradeCount,
    decimal RecentNetPnL);

public sealed record UserLoginHistoryEntry(
    string Id,
    string UserId,
    string Email,
    DateTimeOffset At,
    string? IpAddress);
