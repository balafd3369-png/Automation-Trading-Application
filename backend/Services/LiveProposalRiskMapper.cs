using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

internal static class LiveProposalRiskMapper
{
    public static StrategyCandidate ToCandidate(PaperTradeProposal proposal)
    {
        var legs = (proposal.Legs ?? []).Select(ToLeg).ToList();
        var qty = Math.Max(1, proposal.Quantity);
        var maxRiskPerUnit = proposal.MaxRisk > 0 ? proposal.MaxRisk / qty : 0m;
        var maxRewardPerUnit = proposal.MaxReward > 0 ? proposal.MaxReward / qty : 0m;
        if (maxRiskPerUnit <= 0 && legs.Count > 0)
        {
            maxRiskPerUnit = Math.Max(0.01m, legs[0].Price);
        }

        if (maxRewardPerUnit <= 0)
        {
            maxRewardPerUnit = maxRiskPerUnit;
        }

        return new(
            proposal.Strategy,
            string.IsNullOrWhiteSpace(proposal.Underlying) ? proposal.Symbol : proposal.Underlying,
            proposal.Direction,
            proposal.EvidenceScore,
            proposal.Evidence ?? [],
            legs,
            proposal.StopLoss,
            proposal.Target,
            maxRiskPerUnit,
            maxRewardPerUnit,
            proposal.DataTimestamp ?? DateTimeOffset.UtcNow);
    }

    private static StrategyLeg ToLeg(PaperTradeLegProposal leg) =>
        new(
            leg.Symbol,
            leg.Underlying,
            leg.InstrumentType,
            leg.Side,
            Math.Max(1, leg.Ratio),
            leg.IndicativePrice,
            leg.Strike,
            leg.OptionRight,
            leg.Expiry,
            leg.Quote);
}
