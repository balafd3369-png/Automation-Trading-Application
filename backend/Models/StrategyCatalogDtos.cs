namespace TradeWithBeast.Models;

public sealed record StrategyCatalogEntry(
    string Id,
    string Name,
    string Author,
    string Summary,
    string Timeframe,
    string Session,
    string Status,
    IReadOnlyList<string> Tags,
    StrategyPlaybook Playbook);

public sealed record StrategyPlaybook(
    string Intent,
    IReadOnlyList<StrategyPhase> Phases,
    StrategyTradePlan PrimarySetup,
    StrategyTradePlan? AlternateSetup,
    StrategyRiskRules Risk);

public sealed record StrategyPhase(
    string Title,
    string TimeIst,
    string Description);

public sealed record StrategyTradePlan(
    string Name,
    string Direction,
    string ValidFromIst,
    string Trigger,
    string Target,
    string StopLoss,
    string Reentry);

public sealed record StrategyRiskRules(
    string MaxAttemptsPerDay,
    string Notes);
