namespace TradeWithBeast.Services;

public interface IZoneLiveAutoGateway
{
    bool IsZoneAutoLiveActive();

    bool ShouldAutoExecuteZoneLive(string strategy, string underlying);

    Task ExecuteZoneProposalAsync(string proposalId, CancellationToken cancellationToken = default);
}
