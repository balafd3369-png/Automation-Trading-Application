using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Capital checks for discretionary manual orders (does not use <see cref="PortfolioRiskManager"/>).
/// </summary>
public sealed class ManualOrderCapitalValidator
{
    private readonly TradeHistoryStore _history;
    private readonly PaperTradingAgent _paper;
    private readonly UserIdentityStore _users;
    private readonly ICurrentUserAccessor _current;

    public ManualOrderCapitalValidator(
        TradeHistoryStore history,
        PaperTradingAgent paper,
        UserIdentityStore users,
        ICurrentUserAccessor current)
    {
        _history = history;
        _paper = paper;
        _users = users;
        _current = current;
    }

    public ManualCapitalValidationResult ValidatePaper(decimal requiredCapital)
    {
        decimal allocated;
        decimal available;
        var deployed = _history.SumOpenManualDeployedCapital();
        if (_current.IsAuthenticated)
        {
            var account = _users.GetPaperAccount(_current.UserId!);
            allocated = account.StartingCapital;
            available = Math.Max(0, account.AvailableCapital - deployed);
            if (available < 0)
            {
                available = Math.Max(0, account.StartingCapital - deployed);
            }
        }
        else
        {
            var config = _paper.Snapshot().Config;
            allocated = config?.AllocatedCapital ?? 0;
            if (allocated <= 0)
            {
                return new(false, "Start the paper agent with allocated capital before manual paper trading.", requiredCapital, 0);
            }

            available = Math.Max(0, allocated - deployed);
        }

        if (allocated <= 0)
        {
            return new(false, "Paper account is not configured.", requiredCapital, 0);
        }
        if (requiredCapital > available + PaperTradeEconomics.AssertionTolerance)
        {
            return new(
                false,
                $"Insufficient paper capital: need ₹{requiredCapital:N2}, available ₹{available:N2} (allocated ₹{allocated:N2}, manual open ₹{deployed:N2}).",
                requiredCapital,
                available);
        }

        return new(true, "OK", requiredCapital, available);
    }

    public ManualCapitalValidationResult ValidateLive(decimal requiredCapital, bool useMarginEstimate)
    {
        var funds = _paper.Snapshot().BrokerAvailableFunds;
        if (funds is null)
        {
            return new(false, "Connect m.Stock and refresh broker funds before live manual orders.", requiredCapital, 0);
        }

        var available = funds.Value;
        if (requiredCapital > available + PaperTradeEconomics.AssertionTolerance)
        {
            var hint = useMarginEstimate ? " (margin estimate)" : "";
            return new(
                false,
                $"Insufficient broker funds{hint}: need ₹{requiredCapital:N2}, available ₹{available:N2}.",
                requiredCapital,
                available);
        }

        return new(true, "OK", requiredCapital, available);
    }

    public ManualCapitalSummary BuildPaperSummary()
    {
        var snapshot = _paper.Snapshot();
        if (_current.IsAuthenticated)
        {
            var account = _users.GetPaperAccount(_current.UserId!);
            var deployed = _history.SumOpenManualDeployedCapital();
            return new(account.StartingCapital, deployed, account.AvailableCapital, snapshot.BrokerAvailableFunds);
        }

        var allocated = snapshot.Config?.AllocatedCapital ?? 0;
        var deployedLegacy = _history.SumOpenManualDeployedCapital();
        var availableLegacy = Math.Max(0, allocated - deployedLegacy);
        return new(allocated, deployedLegacy, availableLegacy, snapshot.BrokerAvailableFunds);
    }

    public ManualCapitalSummary BuildLiveSummary()
    {
        var snapshot = _paper.Snapshot();
        var funds = snapshot.BrokerAvailableFunds;
        return new(null, 0, funds ?? 0, funds);
    }
}
