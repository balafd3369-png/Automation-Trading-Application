using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Additive guards for broker routing. Does not alter paper simulation or strategy signal generation.
/// </summary>
public static class InstrumentExecutionPolicy
{
    public const string FuturesDisabledLiveMessage =
        "Live futures routing is disabled (TradingPlatform:FuturesEnabled=false).";

    public static bool ProposalContainsFutureLeg(PaperTradeProposal proposal) =>
        (proposal.Legs ?? []).Any(leg =>
            InstrumentTypeNormalizer.Parse(leg.InstrumentType) == InstrumentKind.Future);

    /// <summary>
    /// When futures are disabled, block live broker submission for FUTURE legs only.
    /// All OPTION / STOCK / INDEX paths behave as before.
    /// </summary>
    public static bool LiveBrokerRoutingAllowed(PaperTradeProposal proposal, bool futuresEnabled)
    {
        if (futuresEnabled)
        {
            return true;
        }

        return !ProposalContainsFutureLeg(proposal);
    }
}
