using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface ISystemHealthBoardProbeSource
{
    BoardResponse Snapshot();
}