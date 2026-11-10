using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public interface IMarketBoardSnapshot
{
    BoardCounts GetBoardCounts();
}
