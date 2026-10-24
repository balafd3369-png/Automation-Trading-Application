using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class OperationsSuiteService
{
    public const string EmergencyConfirmPhrase = "CONFIRM EMERGENCY ACTION";

    private readonly PaperTradingAgent _paperAgent;
    private readonly IPaperProposalSource _paper;
    private readonly LiveExecutionCoordinator _live;
    private readonly TradeHistoryStore _history;
    private readonly ManualTradingService _manual;
    private readonly BrokerConnectionManager _brokers;
    private readonly OperationsStore _ops;
    private readonly BrokerReconciliationService _reconciliation;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<OperationsSuiteService> _log;

    public OperationsSuiteService(
        PaperTradingAgent paperAgent,
        IPaperProposalSource paper,
        LiveExecutionCoordinator live,
        TradeHistoryStore history,
        ManualTradingService manual,
        BrokerConnectionManager brokers,
        OperationsStore ops,
        BrokerReconciliationService reconciliation,
        ICurrentUserAccessor currentUser,
        ILogger<OperationsSuiteService> log)
    {
        _paperAgent = paperAgent;
        _paper = paper;
        _live = live;
        _history = history;
        _manual = manual;
        _brokers = brokers;
        _ops = ops;
        _reconciliation = reconciliation;
        _currentUser = currentUser;
        _log = log;
    }

    public OperationsRiskDashboard GetRiskDashboard()
    {
        var paper = _paperAgent.Snapshot();
        var live = _live.Snapshot();
        var config = paper.Config;
        var allocated = config?.AllocatedCapital ?? 0;
        var manualDeployed = _history.SumOpenManualDeployedCapital();
        var strategyDeployed = paper.Positions.Sum(p => p.CapitalRequired);
        var capitalUsed = manualDeployed + strategyDeployed;
        var available = Math.Max(0, allocated - capitalUsed);

        var exposure = BuildExposure(paper, live);
        var positionRisk = BuildPositionRisk(paper, exposure.TotalExposure);
        var mode = ResolveTradingMode(live);
        var limits = new OperationsRiskLimitsSummary(
            config?.MaxDailyLoss ?? 0,
            config?.MaxOpenPositions ?? 0,
            allocated,
            Math.Max(0, allocated - capitalUsed),
            paper.Positions.Count + CountOpenManual());

        var dailyPnl = paper.TotalPnl;
        var health = ComputeHealth(config, paper, limits, dailyPnl);

        return new OperationsRiskDashboard(
            new OperationsAccountRiskSummary(
                allocated,
                available,
                capitalUsed,
                paper.BrokerAvailableFunds,
                paper.BrokerUtilizedFunds,
                dailyPnl,
                paper.RealizedPnl,
                paper.UnrealizedPnl),
            exposure,
            positionRisk,
            mode,
            ModeLabel(mode),
            limits,
            health,
            HealthLabel(health),
            DateTimeOffset.UtcNow);
    }

    public async Task<OperationsBrokerPortfolioSnapshot> GetBrokerPortfolioAsync(CancellationToken ct)
    {
        var live = _live.Snapshot();
        var connected = _brokers.HasSession;
        decimal? available = null;
        decimal? utilized = null;
        if (connected)
        {
            var funds = await _brokers.Market.GetFundSummaryAsync(ct);
            if (funds is not null)
            {
                available = funds.AvailableBalance;
                utilized = funds.AmountUtilized;
            }
        }

        var holdings = live.BrokerPositions
            .Where(p => p.Product.Equals("CNC", StringComparison.OrdinalIgnoreCase) && p.Quantity != 0)
            .Select(p => new BrokerHoldingRow(
                p.TradingSymbol,
                p.Exchange,
                Math.Abs(p.Quantity),
                p.AveragePrice,
                p.LastPrice,
                Math.Abs(p.Quantity) * p.LastPrice,
                p.Pnl))
            .ToList();

        var syncHealth = connected
            ? (DateTimeOffset.UtcNow - live.UpdatedAt).TotalSeconds > 30 ? "Delayed" : "Healthy"
            : "Disconnected";

        return new OperationsBrokerPortfolioSnapshot(
            connected,
            connected ? BrokerIds.MStock : "",
            syncHealth,
            live.UpdatedAt,
            available,
            utilized,
            available,
            holdings,
            live.BrokerPositions,
            live.BrokerOrders);
    }

    public OperationsBrokerHealthWidget GetBrokerHealth()
    {
        var live = _live.Snapshot();
        var connected = live.BrokerConnected;
        var age = connected ? DateTimeOffset.UtcNow - live.UpdatedAt : TimeSpan.MaxValue;
        var status = !connected
            ? OperationsBrokerHealthStatus.Disconnected
            : age.TotalSeconds > 30
                ? OperationsBrokerHealthStatus.Delayed
                : OperationsBrokerHealthStatus.Healthy;

        var today = DateTimeOffset.UtcNow.Date;
        var orders = live.BrokerOrders;
        var todayOrders = orders.Count(o => o.UpdatedAt?.UtcDateTime.Date == today);
        var failed = orders.Count(o =>
            o.Status.Contains("REJECT", StringComparison.OrdinalIgnoreCase)
            || o.Status.Contains("FAIL", StringComparison.OrdinalIgnoreCase));
        var open = orders.Count(o =>
            !o.Status.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase)
            && !o.Status.Contains("CANCEL", StringComparison.OrdinalIgnoreCase));

        return new OperationsBrokerHealthWidget(
            status,
            status switch
            {
                OperationsBrokerHealthStatus.Healthy => "Healthy",
                OperationsBrokerHealthStatus.Delayed => "Delayed",
                _ => "Disconnected",
            },
            connected,
            live.UpdatedAt,
            open,
            failed,
            todayOrders,
            connected ? (int?)Math.Clamp((int)age.TotalMilliseconds, 0, 60_000) : null,
            live.UpdatedAt);
    }

    public OperationsOrdersBook GetOrdersBook()
    {
        var paper = _paperAgent.Snapshot();
        var live = _live.Snapshot();
        var manual = _history.ListManualOrders(null, includeTerminal: true);
        var rows = new List<OperationsOrderRow>();

        foreach (var order in manual)
        {
            rows.Add(MapManualOrder(order));
        }

        foreach (var order in live.BrokerOrders)
        {
            rows.Add(new OperationsOrderRow(
                order.OrderId,
                order.TradingSymbol,
                order.TransactionType,
                order.Quantity,
                order.Price,
                order.OrderType,
                order.Status,
                order.UpdatedAt,
                TradeSourceWire.Manual,
                "LIVE"));
        }

        foreach (var trade in live.Trades)
        {
            foreach (var order in trade.Orders)
            {
                rows.Add(new OperationsOrderRow(
                    order.BrokerOrderId,
                    order.Symbol,
                    order.TransactionType,
                    order.Quantity,
                    order.AveragePrice,
                    "LIMIT",
                    order.Status,
                    order.UpdatedAt,
                    TradeSourceWire.Strategy,
                    "LIVE"));
            }
        }

        return PartitionOrders(rows);
    }

    public OperationsPositionsBook GetPositionsBook()
    {
        var paper = _paperAgent.Snapshot();
        var live = _live.Snapshot();
        var capitalPerTrade = _paperAgent.AllocatedCapitalPerTrade();
        var now = DateTimeOffset.UtcNow;
        var manualTrades = _history.QueryManualPaperTrades(now.AddMonths(-6), now, capitalPerTrade);
        var paperHistory = _history.QueryPaperTrades("3m", now, capitalPerTrade).Trades;

        var open = new List<OperationsPositionRow>();
        var closed = new List<OperationsPositionRow>();

        foreach (var pos in paper.Positions)
        {
            open.Add(new OperationsPositionRow(
                pos.Id,
                pos.Symbol,
                pos.InstrumentType,
                pos.Quantity,
                pos.EntryPrice,
                pos.CurrentPrice,
                pos.Pnl,
                TradeSourceWire.Strategy,
                "OPEN",
                "PAPER"));
        }

        foreach (var trade in manualTrades.Where(t => t.Status == "OPEN"))
        {
            var leg = trade.Legs.FirstOrDefault();
            open.Add(new OperationsPositionRow(
                trade.Id,
                leg?.Symbol ?? trade.Underlying,
                leg?.InstrumentType ?? "STOCK",
                trade.Quantity > 0 ? trade.Quantity : leg?.Quantity ?? 0,
                trade.EntryPrice,
                leg?.CurrentPrice ?? trade.EntryPrice,
                trade.Pnl ?? 0,
                TradeSourceWire.Manual,
                "OPEN",
                "PAPER"));
        }

        foreach (var trade in paperHistory.Where(t => t.Status == "CLOSED" && !IsManual(t)))
        {
            closed.Add(MapHistoryPosition(trade, TradeSourceWire.Strategy));
        }

        foreach (var trade in manualTrades.Where(t => t.Status == "CLOSED"))
        {
            closed.Add(MapHistoryPosition(trade, TradeSourceWire.Manual));
        }

        foreach (var pos in live.BrokerPositions.Where(p => p.Quantity != 0))
        {
            open.Add(new OperationsPositionRow(
                pos.TradingSymbol,
                pos.TradingSymbol,
                InferInstrument(pos.TradingSymbol),
                pos.Quantity,
                pos.AveragePrice,
                pos.LastPrice,
                pos.Pnl,
                TradeSourceWire.Strategy,
                "OPEN",
                "LIVE"));
        }

        return new OperationsPositionsBook(open, closed);
    }

    public IReadOnlyList<OperationsHoldingRow> GetHoldings()
    {
        var live = _live.Snapshot();
        var manual = _manual.GetSnapshot("PAPER").Holdings;
        var broker = live.BrokerPositions
            .Where(p => p.Product.Equals("CNC", StringComparison.OrdinalIgnoreCase) && p.Quantity != 0)
            .Select(p => new OperationsHoldingRow(
                p.TradingSymbol,
                Math.Abs(p.Quantity),
                p.AveragePrice,
                Math.Abs(p.Quantity) * p.LastPrice,
                p.Pnl,
                TradeSourceWire.Strategy))
            .ToList();

        if (broker.Count > 0)
        {
            return broker;
        }

        return manual
            .Select(h => new OperationsHoldingRow(h.Symbol, h.Quantity, h.AveragePrice, h.MarketValue, h.Pnl, TradeSourceWire.Manual))
            .ToList();
    }

    public IReadOnlyList<OperationsJournalEntry> QueryJournal(OperationsJournalQuery query)
    {
        var capitalPerTrade = _paperAgent.AllocatedCapitalPerTrade();
        var now = DateTimeOffset.UtcNow;
        var from = ParseDate(query.From) ?? now.AddMonths(-3);
        var to = ParseDate(query.To) ?? now.AddDays(1);
        var trades = _history.QueryPaperTradesBetween(from, to, "custom", capitalPerTrade).Trades;
        var liveTrades = _history.ReadLiveTrades(activeOnly: false);

        var entries = new List<OperationsJournalEntry>();
        foreach (var trade in trades)
        {
            var source = IsManual(trade) ? TradeSourceWire.Manual : TradeSourceWire.Strategy;
            if (!MatchesJournalFilter(trade, source, query, "PAPER"))
            {
                continue;
            }

            entries.Add(MapJournal(trade, source, "PAPER"));
        }

        foreach (var live in liveTrades)
        {
            var proposal = live.Proposal;
            var source = string.Equals(proposal.Strategy, TradeHistoryStore.ManualStrategyId, StringComparison.OrdinalIgnoreCase)
                ? TradeSourceWire.Manual
                : TradeSourceWire.Strategy;
            if (!string.IsNullOrWhiteSpace(query.Source)
                && !string.Equals(query.Source, source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query.Symbol)
                && !proposal.Symbol.Contains(query.Symbol, StringComparison.OrdinalIgnoreCase)
                && !proposal.Underlying.Contains(query.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new OperationsJournalEntry(
                live.Id,
                source,
                proposal.Strategy,
                proposal.InstrumentType,
                proposal.Symbol,
                proposal.Direction.StartsWith("SHORT", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY",
                proposal.IndicativePrice,
                null,
                null,
                live.CreatedAt,
                null,
                _ops.GetJournalNotes(live.Id),
                "LIVE",
                live.Status));
        }

        return entries.OrderByDescending(e => e.OpenTime ?? e.CloseTime ?? DateTimeOffset.MinValue).ToList();
    }

    public IReadOnlyList<OperationsTimelineEntry> GetTimeline(OperationsTimelineQuery query)
    {
        var items = new List<OperationsTimelineEntry>();
        foreach (var audit in _ops.ListAudit(150))
        {
            items.Add(new OperationsTimelineEntry(audit.Id, audit.Category, audit.EventType, audit.Message, audit.At));
        }

        foreach (var evt in _history.RecentEvents(80))
        {
            items.Add(new OperationsTimelineEntry(
                evt.Id,
                "PAPER",
                evt.Type,
                evt.Reason,
                evt.At));
        }

        foreach (var evt in _history.RecentLiveEvents(80))
        {
            items.Add(new OperationsTimelineEntry(evt.Id, "LIVE", evt.Type, evt.Message, evt.At));
        }

        var decisions = _paperAgent.Snapshot().RecentDecisions ?? [];
        foreach (var decision in decisions.Take(40))
        {
            items.Add(new OperationsTimelineEntry(
                decision.Id,
                "STRATEGY",
                decision.Accepted ? "Strategy Evaluated (accepted)" : "Strategy Evaluated (rejected)",
                $"{decision.Strategy} {decision.Underlying} {decision.Direction}",
                decision.EvaluatedAt));
        }

        var filtered = string.IsNullOrWhiteSpace(query.Category)
            ? items
            : items.Where(i => i.Category.Contains(query.Category, StringComparison.OrdinalIgnoreCase)).ToList();

        return filtered
            .OrderByDescending(i => i.At)
            .Take(Math.Clamp(query.Limit, 10, 300))
            .ToList();
    }

    public IReadOnlyList<OperationsAuditEntry> GetAuditLog(int limit = 200) =>
        _ops.ListAudit(limit, _currentUser.IsAdmin ? "" : null);

    public IReadOnlyList<OperationsNotification> GetNotifications(int limit = 100, bool unreadOnly = false) =>
        _ops.ListNotifications(limit, unreadOnly, _currentUser.IsAdmin ? "" : null);

    public BrokerReconciliationSnapshot GetReconciliationDashboard() => _reconciliation.GetSnapshot();

    public IReadOnlyList<BrokerReconciliationHistoryEntry> GetReconciliationHistory(int limit = 50) =>
        _ops.ListReconciliationHistory(limit);

    public void MarkNotificationRead(string id) => _ops.MarkNotificationRead(id);

    public void SaveJournalNotes(string tradeId, string notes) => _ops.SaveJournalNotes(tradeId, notes);

    public async Task<OperationsEmergencyResult> ExecuteEmergencyAsync(
        OperationsEmergencyRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(request.Confirmation?.Trim(), EmergencyConfirmPhrase, StringComparison.Ordinal))
        {
            return new(false, $"Type exactly: {EmergencyConfirmPhrase}", null);
        }

        var action = request.Action.Trim().ToUpperInvariant();
        try
        {
            switch (action)
            {
                case "STOP_STRATEGY":
                    await _paperAgent.HandleCommandAsync("stop", ct);
                    return Audit("RISK", "Emergency", "Strategy paper automation stopped.");
                case "PAUSE_AUTO":
                    _paper.SetAutoAdmitTrades(false);
                    return Audit("RISK", "Emergency", "Auto-admit paused (manual confirms still possible).");
                case "DISARM_LIVE":
                    _live.Disarm();
                    Notify("info", "LiveDisarmed", "Live trading disarmed.");
                    return Audit("RISK", "Emergency", "Live trading disarmed.");
                case "EXIT_ALL_PAPER":
                    _paperAgent.EmergencyFlattenOpenPositions("operations emergency flatten");
                    await CloseAllManualPaperAsync(ct);
                    return Audit("RISK", "Emergency", "All paper positions flattened.");
                case "EXIT_ALL_LIVE":
                    foreach (var trade in _history.ReadLiveTrades(true))
                    {
                        await _live.EmergencyExitAsync(trade.Id, ct);
                    }

                    return Audit("RISK", "Emergency", "Emergency exit requested for all active live trades.");
                case "CANCEL_OPEN_ORDERS":
                    if (!_brokers.HasSession)
                    {
                        return new(false, "Broker not connected.", null);
                    }

                    var orders = await _brokers.Orders.GetOrdersAsync(ct);
                    if (!orders.Success || orders.Data is null)
                    {
                        return new(false, orders.Message, null);
                    }

                    var cancelled = 0;
                    foreach (var order in orders.Data.Where(o => IsOpenBrokerOrder(o.Status)))
                    {
                        var result = await _brokers.Orders.CancelOrderAsync(order.OrderId, ct);
                        if (result.Success)
                        {
                            cancelled++;
                        }
                    }

                    return Audit("BROKER", "Emergency", $"Cancelled {cancelled} open broker order(s).");
                default:
                    return new(false, "Unknown emergency action.", null);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Emergency action {Action} failed", action);
            return new(false, ex.Message, null);
        }
    }

    private async Task CloseAllManualPaperAsync(CancellationToken ct)
    {
        var snap = _manual.GetSnapshot("PAPER");
        foreach (var pos in snap.Positions)
        {
            await _manual.ClosePositionAsync(new ManualTradingClosePositionRequest("PAPER", pos.TradeId), ct);
        }
    }

    private OperationsEmergencyResult Audit(string category, string type, string message)
    {
        var entry = _ops.AppendAudit(category, type, message);
        var note = _ops.AppendNotification("warning", type, message);
        _log.LogWarning("Operations audit: {Message}", message);
        return new(true, message, entry);
    }

    private void Notify(string severity, string type, string message) => _ops.AppendNotification(severity, type, message);

    private static OperationsExposureSummary BuildExposure(PaperAgentSnapshot paper, LiveExecutionSnapshot live)
    {
        decimal stock = 0;
        decimal option = 0;
        decimal future = 0;
        foreach (var pos in paper.Positions)
        {
            var cap = pos.CapitalRequired > 0 ? pos.CapitalRequired : pos.EntryPrice * pos.Quantity;
            AddExposure(pos.InstrumentType, cap, ref stock, ref option, ref future);
        }

        foreach (var pos in live.BrokerPositions.Where(p => p.Quantity != 0))
        {
            var cap = Math.Abs(pos.Quantity) * pos.LastPrice;
            AddExposure(InferInstrument(pos.TradingSymbol), cap, ref stock, ref option, ref future);
        }

        return new(stock + option + future, stock, option, future);
    }

    private static void AddExposure(string instrumentType, decimal cap, ref decimal stock, ref decimal option, ref decimal future)
    {
        var kind = InstrumentTypeNormalizer.Parse(instrumentType);
        switch (kind)
        {
            case InstrumentKind.Stock:
            case InstrumentKind.Index:
                stock += cap;
                break;
            case InstrumentKind.Future:
                future += cap;
                break;
            default:
                option += cap;
                break;
        }
    }

    private static OperationsPositionRiskSummary BuildPositionRisk(PaperAgentSnapshot paper, decimal totalExposure)
    {
        var openRisk = paper.Positions.Sum(p => p.MaxRisk > 0 ? p.MaxRisk : p.CapitalRequired);
        var maxRisk = paper.Positions.Select(p => p.MaxRisk).DefaultIfEmpty(0).Max();
        var largest = paper.Positions.OrderByDescending(p => p.CapitalRequired).FirstOrDefault();
        var loser = paper.Positions.OrderBy(p => p.Pnl).FirstOrDefault();
        return new OperationsPositionRiskSummary(
            paper.Positions.Count,
            openRisk,
            maxRisk,
            largest?.Symbol,
            largest?.CapitalRequired ?? 0,
            loser is { Pnl: < 0 } ? loser.Symbol : null,
            loser is { Pnl: < 0 } ? loser.Pnl : 0);
    }

    private int CountOpenManual()
    {
        var snap = _manual.GetSnapshot("PAPER");
        return snap.Positions.Count;
    }

    private static OperationsTradingMode ResolveTradingMode(LiveExecutionSnapshot live)
    {
        if (live.ZoneAutoLiveArmed)
        {
            return OperationsTradingMode.ZoneAutoLiveArmed;
        }

        if (live.Armed)
        {
            return OperationsTradingMode.ManualLiveArmed;
        }

        return OperationsTradingMode.Disarmed;
    }

    private static string ModeLabel(OperationsTradingMode mode) =>
        mode switch
        {
            OperationsTradingMode.ZoneAutoLiveArmed => "Zone Auto Live Armed",
            OperationsTradingMode.ManualLiveArmed => "Manual Live Armed",
            _ => "Disarmed",
        };

    private static OperationsRiskHealth ComputeHealth(
        PaperAgentConfig? config,
        PaperAgentSnapshot paper,
        OperationsRiskLimitsSummary limits,
        decimal dailyPnl)
    {
        if (config is null)
        {
            return OperationsRiskHealth.Watch;
        }

        var lossRatio = config.MaxDailyLoss > 0 ? Math.Abs(Math.Min(0, dailyPnl)) / config.MaxDailyLoss : 0;
        var posRatio = config.MaxOpenPositions > 0 ? limits.OpenPositions / (decimal)config.MaxOpenPositions : 0;
        var capRatio = config.AllocatedCapital > 0
            ? (config.AllocatedCapital - limits.RemainingCapacity) / config.AllocatedCapital
            : 0;
        var stress = Math.Max(lossRatio, Math.Max(posRatio, capRatio));
        return stress >= 0.85m
            ? OperationsRiskHealth.RiskLimitApproaching
            : stress >= 0.6m
                ? OperationsRiskHealth.Watch
                : OperationsRiskHealth.Healthy;
    }

    private static string HealthLabel(OperationsRiskHealth health) =>
        health switch
        {
            OperationsRiskHealth.Healthy => "Healthy",
            OperationsRiskHealth.Watch => "Watch",
            _ => "Risk Limit Approaching",
        };

    private static bool IsManual(PaperTradeHistorySnapshot trade) =>
        string.Equals(trade.TradeSource, TradeSourceWire.Manual, StringComparison.OrdinalIgnoreCase)
        || string.Equals(trade.Strategy, TradeHistoryStore.ManualStrategyId, StringComparison.OrdinalIgnoreCase);

    private static OperationsOrderRow MapManualOrder(ManualOrderSnapshot order) =>
        new(
            order.Id,
            order.Symbol,
            order.TransactionType,
            order.Quantity,
            order.LimitPrice ?? order.AveragePrice,
            order.OrderType,
            order.Status,
            order.UpdatedAt,
            TradeSourceWire.Manual,
            order.ExecutionMode);

    private static OperationsOrdersBook PartitionOrders(IReadOnlyList<OperationsOrderRow> rows)
    {
        static bool IsCancelled(string s) => s.Contains("CANCEL", StringComparison.OrdinalIgnoreCase);
        static bool IsRejected(string s) => s.Contains("REJECT", StringComparison.OrdinalIgnoreCase);
        static bool IsComplete(string s) => s.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase);
        return new(
            rows.Where(r => !IsComplete(r.Status) && !IsCancelled(r.Status) && !IsRejected(r.Status)).ToList(),
            rows.Where(r => IsComplete(r.Status)).ToList(),
            rows.Where(r => IsCancelled(r.Status)).ToList(),
            rows.Where(r => IsRejected(r.Status)).ToList());
    }

    private static OperationsPositionRow MapHistoryPosition(PaperTradeHistorySnapshot trade, string source) =>
        new(
            trade.Id,
            trade.Underlying,
            trade.Legs.FirstOrDefault()?.InstrumentType ?? "OPTION",
            trade.Quantity,
            trade.EntryPrice,
            trade.ExitPrice ?? trade.EntryPrice,
            trade.Pnl ?? 0,
            source,
            trade.Status,
            "PAPER");

    private static bool MatchesJournalFilter(
        PaperTradeHistorySnapshot trade,
        string source,
        OperationsJournalQuery query,
        string mode)
    {
        if (!string.IsNullOrWhiteSpace(query.Source)
            && !string.Equals(query.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Strategy)
            && !trade.Strategy.Contains(query.Strategy, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Symbol)
            && !trade.Underlying.Contains(query.Symbol, StringComparison.OrdinalIgnoreCase)
            && trade.Legs.All(l => !l.Symbol.Contains(query.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private OperationsJournalEntry MapJournal(PaperTradeHistorySnapshot trade, string source, string mode) =>
        new(
            trade.Id,
            source,
            trade.Strategy,
            trade.Legs.FirstOrDefault()?.InstrumentType ?? "OPTION",
            trade.Underlying,
            trade.Direction.StartsWith("SHORT", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY",
            trade.EntryPrice,
            trade.ExitPrice,
            trade.Pnl,
            trade.OpenedAt ?? trade.CreatedAt,
            trade.ClosedAt,
            _ops.GetJournalNotes(trade.Id),
            mode,
            trade.Status);

    private static DateTimeOffset? ParseDate(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : DateTimeOffset.TryParse(raw, out var dt) ? dt : null;

    private static string InferInstrument(string symbol) =>
        symbol.Contains("FUT", StringComparison.OrdinalIgnoreCase) ? "FUTURE"
        : symbol.Contains("CE", StringComparison.OrdinalIgnoreCase) || symbol.Contains("PE", StringComparison.OrdinalIgnoreCase) ? "OPTION"
        : "STOCK";

    private static bool IsOpenBrokerOrder(string status) =>
        !status.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase)
        && !status.Contains("CANCEL", StringComparison.OrdinalIgnoreCase);
}
