using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class TrapFallFormationService
{
    private readonly MockMarketService _market;
    private readonly ZoneBoardSessionTracker _sessions;
    private static readonly TimeZoneInfo India =
        TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public TrapFallFormationService(MockMarketService market, ZoneBoardSessionTracker sessions)
    {
        _market = market;
        _sessions = sessions;
    }

    public TrapFallFormationResponse Scan()
    {
        var istDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India).Date);
        var board = _market.Snapshot();
        return TrapFallFormationScanner.Scan(board, _sessions, istDay);
    }
}
