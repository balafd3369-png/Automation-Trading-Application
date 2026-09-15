using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class LiveExecutionCoordinator : IHostedService, IDisposable
{
    public const string ArmAcknowledgement = "I UNDERSTAND LIVE ORDERS USE REAL MONEY";
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private readonly object _gate = new();
    private readonly IMStockOrderClient _broker;
    private readonly MStockAuthClient _auth;
    private readonly IPaperProposalSource _paper;
    private readonly TradeHistoryStore _history;
    private readonly LiveExecutionOptions _options;
    private readonly ILogger<LiveExecutionCoordinator> _log;
    private Timer? _timer;
    private DateTimeOffset? _armedUntil;
    private IReadOnlyList<BrokerPosition> _positions = [];
    private IReadOnlyList<LiveEodPrompt> _eodPrompts = [];
    private string _reconciliation = "NOT RUN";
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private int _busy;

    public LiveExecutionCoordinator(
        IMStockOrderClient broker,
        MStockAuthClient auth,
        IPaperProposalSource paper,
        TradeHistoryStore history,
        LiveExecutionOptions options,
        ILogger<LiveExecutionCoordinator> log)
    {
        _broker = broker;
        _auth = auth;
        _paper = paper;
        _history = history;
        _options = options;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(
            _ => _ = ReconcileAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(Math.Max(2, _options.ReconcileSeconds)));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    public LiveExecutionSnapshot Arm(string acknowledgement)
    {
        lock (_gate)
        {
            if (!_options.Enabled)
            {
                _reconciliation = "Live trading is disabled in backend configuration.";
                return SnapshotUnsafe();
            }
            var session = CurrentSessionUnsafe();
            if (!session.IsOpen)
            {
                _reconciliation = session.Message;
                return SnapshotUnsafe();
            }
            if (!_auth.HasSession)
            {
                _reconciliation = "Connect m.Stock OTP before arming live trading.";
                return SnapshotUnsafe();
            }
            if (!string.Equals(acknowledgement.Trim(), ArmAcknowledgement, StringComparison.Ordinal))
            {
                _reconciliation = $"Type exactly: {ArmAcknowledgement}";
                return SnapshotUnsafe();
            }
            _armedUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ArmMinutes, 1, 120));
            _reconciliation = "ARMED; awaiting separately confirmed proposal";
            _updatedAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public LiveExecutionSnapshot Disarm()
    {
        lock (_gate)
        {
            _armedUntil = null;
            _reconciliation = "DISARMED";
            _updatedAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public async Task<LiveExecutionSnapshot> ConfirmAsync(
        string proposalId,
        string product,
        CancellationToken ct)
    {
        PaperTradeProposal? proposal;
        lock (_gate)
        {
            if (!_options.Enabled) return WithStatusUnsafe("Live execution is disabled by configuration.");
            if (!CurrentSessionUnsafe().IsOpen) return WithStatusUnsafe(CurrentSessionUnsafe().Message);
            if (!_auth.HasSession) return WithStatusUnsafe("Connect m.Stock OTP before confirming a live trade.");
            if (!IsArmedUnsafe()) return WithStatusUnsafe("Live execution is not armed.");
            proposal = _paper.Snapshot().PendingTrade;
            if (proposal is null || !proposal.Id.Equals(proposalId, StringComparison.Ordinal))
            {
                return WithStatusUnsafe("The confirmed proposal is missing, changed, or expired.");
            }
        }

        product = product.Trim().ToUpperInvariant();
        if (product is not ("MIS" or "NRML"))
        {
            lock (_gate) return WithStatusUnsafe("Product must be MIS or NRML.");
        }
        if (proposal.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            lock (_gate) return WithStatusUnsafe("The proposal expired before live submission.");
        }

        _history.SaveLiveTrade(proposal, product);
        _history.SetLiveTradeStatus(proposal.Id, "SUBMITTING");
        var legs = (proposal.Legs ?? []).OrderBy(leg => leg.Side == "BUY" ? 0 : 1).ToList();
        var completed = new List<(PaperTradeLegProposal Leg, LiveOrderSnapshot Order)>();

        if (proposal.Strategy == "NAKED_INDEX_OPTION_SHORT")
        {
            var naked = legs.SingleOrDefault();
            if (naked is null || naked.Side != "SELL" || naked.InstrumentType != "OPTION")
            {
                return Fail(proposal.Id, "Invalid naked option proposal.");
            }
            var marginOrder = BuildOrder(proposal.Id, naked, product, "ENTRY");
            var margin = await _broker.CalculateMarginAsync(marginOrder, ct);
            if (!margin.Success)
            {
                return Fail(proposal.Id, $"Margin check rejected: {margin.Message}");
            }
            var funds = _paper.Snapshot().BrokerAvailableFunds;
            if (funds is not null && margin.Data is not null && margin.Data.TotalMargin > funds)
            {
                return Fail(
                    proposal.Id,
                    $"Insufficient margin: broker estimate ₹{margin.Data.TotalMargin:N2}, available ₹{funds:N2}.");
            }
        }

        foreach (var leg in legs)
        {
            var request = BuildOrder(proposal.Id, leg, product, "ENTRY");
            var placed = await _broker.PlaceOrderAsync(request, ct);
            var localId = $"{proposal.Id}:{leg.Id}:ENTRY";
            if (!placed.Success || string.IsNullOrWhiteSpace(placed.Data))
            {
                await CompensateAsync(proposal.Id, completed, product, ct);
                return Fail(proposal.Id, Exact("Broker rejected order", placed.Message, placed.ErrorType));
            }
            var submitted = new LiveOrderSnapshot(
                localId, proposal.Id, leg.Id, placed.Data, request.Exchange, leg.Symbol, leg.Side,
                product, "ENTRY", "SUBMITTED", leg.Quantity, 0, 0, placed.Message, request.Tag, DateTimeOffset.UtcNow);
            _history.UpsertLiveOrder(submitted);
            var settled = await WaitForTerminalAsync(submitted, ct);
            if (settled.Status != "COMPLETE" || settled.FilledQuantity != settled.Quantity)
            {
                if (settled.PendingQuantity() > 0) await _broker.CancelOrderAsync(settled.BrokerOrderId, ct);
                if (settled.FilledQuantity > 0) completed.Add((leg with { Quantity = settled.FilledQuantity }, settled));
                await CompensateAsync(proposal.Id, completed, product, ct);
                return Fail(
                    proposal.Id,
                    Exact($"Order {settled.Status}", settled.BrokerMessage, $"filled {settled.FilledQuantity}/{settled.Quantity}"));
            }
            completed.Add((leg, settled));
        }

        _history.SetLiveTradeStatus(proposal.Id, "OPEN");
        _paper.CompleteExternalProposal(proposal.Id, "Live order filled by broker.");
        await ReconcileAsync(ct);
        return Snapshot();
    }

    public async Task<LiveExecutionSnapshot> EmergencyExitAsync(string tradeId, CancellationToken ct)
    {
        var trade = _history.ReadLiveTrades(true).FirstOrDefault(item => item.Id == tradeId);
        if (trade is null)
        {
            lock (_gate) return WithStatusUnsafe("Live trade was not found or is already final.");
        }
        await ExitTradeAsync(trade, "EMERGENCY EXIT", ct);
        await ReconcileAsync(ct);
        return Snapshot();
    }

    public async Task<LiveExecutionSnapshot> HandleEodAsync(
        string tradeId,
        string action,
        CancellationToken ct)
    {
        var trade = _history.ReadLiveTrades(true).FirstOrDefault(item => item.Id == tradeId);
        if (trade is null)
        {
            lock (_gate) return WithStatusUnsafe("The EOD trade is no longer active.");
        }
        if (action.Equals("HOLD", StringComparison.OrdinalIgnoreCase))
        {
            var converted = true;
            var reason = "";
            foreach (var position in _positions.Where(position =>
                         trade.Proposal.Legs?.Any(leg => leg.Symbol == position.TradingSymbol) == true
                         && position.Quantity != 0))
            {
                var side = position.Quantity > 0 ? "BUY" : "SELL";
                var result = await _broker.ConvertPositionAsync(
                    position.TradingSymbol, position.Exchange, side, Math.Abs(position.Quantity), "MIS", "NRML", ct);
                if (!result.Success)
                {
                    converted = false;
                    reason = result.Message;
                    break;
                }
            }
            await ReconcileBrokerOnlyAsync(ct);
            var symbols = trade.Proposal.Legs?.Select(leg => leg.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            converted = converted && _positions.Where(position => symbols.Contains(position.TradingSymbol) && position.Quantity != 0)
                .All(position => position.Product == "NRML");
            if (converted)
            {
                _history.SetLiveTradeStatus(tradeId, "OPEN_NRML", "Broker position verified as NRML.");
                return Snapshot();
            }
            _history.SetLiveTradeStatus(tradeId, "CONVERSION_FAILED", Exact("MIS to NRML was not verified", reason));
        }
        await ExitTradeAsync(trade, "EOD EXIT", ct);
        await ReconcileAsync(ct);
        return Snapshot();
    }

    public LiveExecutionSnapshot Snapshot()
    {
        lock (_gate) return SnapshotUnsafe();
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!_options.Enabled) return;
            await ReconcileBrokerOnlyAsync(ct);
            var ordersResult = await _broker.GetOrdersAsync(ct);
            if (!ordersResult.Success)
            {
                lock (_gate) _reconciliation = Exact("ORDER RECONCILIATION FAILED", ordersResult.Message);
                return;
            }
            var byId = (ordersResult.Data ?? []).ToDictionary(order => order.OrderId, StringComparer.OrdinalIgnoreCase);
            foreach (var trade in _history.ReadLiveTrades(true))
            {
                foreach (var local in trade.Orders.Where(order => !string.IsNullOrWhiteSpace(order.BrokerOrderId)))
                {
                    if (byId.TryGetValue(local.BrokerOrderId, out var brokerOrder))
                    {
                        _history.UpsertLiveOrder(FromBroker(local, brokerOrder));
                    }
                }
            }
            await ApplyEodPolicyAsync(ct);
            lock (_gate)
            {
                _reconciliation = "HEALTHY";
                _updatedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Live reconciliation failed.");
            lock (_gate) _reconciliation = $"FAILED: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task ReconcileBrokerOnlyAsync(CancellationToken ct)
    {
        var result = await _broker.GetPositionsAsync(ct);
        if (!result.Success) throw new InvalidOperationException(Exact("position reconciliation failed", result.Message));
        lock (_gate)
        {
            _positions = result.Data ?? [];
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task ApplyEodPolicyAsync(CancellationToken ct)
    {
        var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var mis = _history.ReadLiveTrades(true).Where(trade => trade.Product == "MIS" && trade.Status == "OPEN").ToList();
        if (nowIst.TimeOfDay >= new TimeSpan(15, 0, 0) && nowIst.TimeOfDay < new TimeSpan(15, 10, 0))
        {
            lock (_gate)
            {
                _eodPrompts = mis.Select(trade => new LiveEodPrompt(
                    trade.Id, trade.Proposal.Underlying, trade.Product,
                    new DateTimeOffset(nowIst.Date.AddHours(15).AddMinutes(10), nowIst.Offset),
                    "Choose HOLD to attempt verified NRML conversion, or EXIT. No response exits automatically.")).ToList();
            }
        }
        if (nowIst.TimeOfDay >= new TimeSpan(15, 10, 0))
        {
            foreach (var trade in mis) await ExitTradeAsync(trade, "EOD NO RESPONSE", ct);
            lock (_gate) _eodPrompts = [];
        }
    }

    private async Task ExitTradeAsync(TradeHistoryStore.StoredLiveTrade trade, string reason, CancellationToken ct)
    {
        _history.SetLiveTradeStatus(trade.Id, "EXITING", reason);
        var entryOrders = trade.Orders.Where(order => order.Purpose == "ENTRY" && order.FilledQuantity > 0).ToList();
        foreach (var entry in entryOrders.OrderBy(order => order.TransactionType == "SELL" ? 0 : 1))
        {
            var side = entry.TransactionType == "BUY" ? "SELL" : "BUY";
            var leg = trade.Proposal.Legs?.FirstOrDefault(item => item.Id == entry.LegId);
            if (leg is null) continue;
            var request = BuildOrder(trade.Id, leg with { Side = side, Quantity = entry.FilledQuantity }, entry.Product, "EXIT");
            var placed = await _broker.PlaceOrderAsync(request, ct);
            var snapshot = new LiveOrderSnapshot(
                $"{trade.Id}:{entry.LegId}:EXIT", trade.Id, entry.LegId, placed.Data ?? "",
                request.Exchange, entry.Symbol, side, entry.Product, "EXIT",
                placed.Success ? "SUBMITTED" : "REJECTED", entry.FilledQuantity, 0, 0,
                placed.Message, request.Tag, DateTimeOffset.UtcNow);
            _history.UpsertLiveOrder(snapshot);
            if (!placed.Success || string.IsNullOrWhiteSpace(placed.Data))
            {
                _history.SetLiveTradeStatus(trade.Id, "EXIT_FAILED", Exact("Exit rejected", placed.Message, placed.ErrorType));
                return;
            }
            var settled = await WaitForTerminalAsync(snapshot, ct);
            if (settled.Status != "COMPLETE")
            {
                _history.SetLiveTradeStatus(trade.Id, "EXIT_FAILED", Exact("Exit not filled", settled.BrokerMessage));
                return;
            }
        }
        _history.SetLiveTradeStatus(trade.Id, "CLOSED", reason);
    }

    private async Task CompensateAsync(
        string tradeId,
        IReadOnlyList<(PaperTradeLegProposal Leg, LiveOrderSnapshot Order)> completed,
        string product,
        CancellationToken ct)
    {
        foreach (var item in completed.Reverse())
        {
            var side = item.Leg.Side == "BUY" ? "SELL" : "BUY";
            var leg = item.Leg with { Side = side, Quantity = item.Order.FilledQuantity };
            var request = BuildOrder(tradeId, leg, product, "COMPENSATION");
            var placed = await _broker.PlaceOrderAsync(request, ct);
            _history.UpsertLiveOrder(new(
                $"{tradeId}:{leg.Id}:COMPENSATION", tradeId, leg.Id, placed.Data ?? "", request.Exchange,
                leg.Symbol, side, product, "COMPENSATION", placed.Success ? "SUBMITTED" : "REJECTED",
                leg.Quantity, 0, 0, placed.Message, request.Tag, DateTimeOffset.UtcNow));
        }
    }

    private async Task<LiveOrderSnapshot> WaitForTerminalAsync(LiveOrderSnapshot submitted, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_options.OrderTimeoutSeconds, 3, 60));
        var latest = submitted;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500, ct);
            var result = await _broker.GetOrdersAsync(ct);
            var order = result.Data?.FirstOrDefault(item => item.OrderId == submitted.BrokerOrderId);
            if (order is null) continue;
            latest = FromBroker(submitted, order);
            _history.UpsertLiveOrder(latest);
            if (IsTerminal(order.Status)) return latest;
        }
        return latest with { Status = "TIMEOUT", BrokerMessage = "Broker fill was not confirmed before timeout.", UpdatedAt = DateTimeOffset.UtcNow };
    }

    private BrokerOrderRequest BuildOrder(
        string tradeId,
        PaperTradeLegProposal leg,
        string product,
        string purpose)
    {
        var protection = Math.Clamp(_options.PriceProtectionPercent, 0.05m, 2m) / 100m;
        var rawPrice = leg.Side == "BUY"
            ? leg.IndicativePrice * (1m + protection)
            : leg.IndicativePrice * (1m - protection);
        var price = Math.Max(0.05m, decimal.Round(rawPrice / 0.05m, 0, MidpointRounding.AwayFromZero) * 0.05m);
        var exchange = leg.Quote?.Exchange;
        if (string.IsNullOrWhiteSpace(exchange))
        {
            exchange = leg.InstrumentType == "OPTION"
                ? leg.Underlying == "SENSEX" ? "BFO" : "NFO"
                : "NSE";
        }
        var tag = $"TWB-{tradeId[..Math.Min(8, tradeId.Length)]}-{purpose[..Math.Min(3, purpose.Length)]}-{leg.Id[^Math.Min(3, leg.Id.Length)..]}";
        return new(exchange, leg.Symbol, leg.Side, leg.Quantity, product, "LIMIT", price, 0, "DAY", "regular", tag);
    }

    private static LiveOrderSnapshot FromBroker(LiveOrderSnapshot local, BrokerOrder order) =>
        local with
        {
            Status = NormalizeStatus(order.Status),
            FilledQuantity = order.FilledQuantity,
            AveragePrice = order.AveragePrice,
            BrokerMessage = Exact(order.StatusMessageRaw, order.StatusMessage),
            UpdatedAt = order.UpdatedAt ?? DateTimeOffset.UtcNow,
        };

    private LiveExecutionSnapshot Fail(string tradeId, string reason)
    {
        _history.SetLiveTradeStatus(tradeId, "FAILED", reason);
        lock (_gate) return WithStatusUnsafe(reason);
    }

    private LiveExecutionSnapshot WithStatusUnsafe(string status)
    {
        _reconciliation = status;
        _updatedAt = DateTimeOffset.UtcNow;
        return SnapshotUnsafe();
    }

    private LiveExecutionSnapshot SnapshotUnsafe()
    {
        var stored = _history.ReadLiveTrades();
        var trades = stored.Select(trade => new LiveTradeSnapshot(
            trade.Id, trade.Proposal.Strategy, trade.Proposal.Underlying, trade.Product, trade.Status,
            trade.FailureReason, trade.CreatedAt, trade.Orders)).ToList();
        var armed = IsArmedUnsafe();
        var session = CurrentSessionUnsafe();
        var brokerConnected = _auth.HasSession;
        var state = ResolveState(session, brokerConnected, armed);
        return new(
            _options.Enabled,
            brokerConnected,
            session,
            armed,
            armed ? _armedUntil : null,
            state,
            armed ? _paper.Snapshot().PendingTrade : null,
            trades,
            _positions,
            _history.RecentLiveEvents(),
            _eodPrompts,
            _reconciliation,
            _updatedAt);
    }

    private MarketSessionSnapshot CurrentSessionUnsafe() =>
        _options.TreatMarketAsOpen
            ? new("OPEN", true, "Market is open for live entries until 15:10 IST.", null)
            : MarketSession.Evaluate(DateTimeOffset.UtcNow);

    private string ResolveState(MarketSessionSnapshot session, bool brokerConnected, bool armed)
    {
        if (!_options.Enabled) return "DISABLED";
        if (!brokerConnected) return "BROKER OFFLINE";
        if (!session.IsOpen) return session.Phase switch
        {
            "WEEKEND" => "WEEKEND",
            "PRE_OPEN" => "PRE-OPEN",
            "CLOSED" => "MARKET CLOSED",
            "SQUARE_OFF" => "SQUARE-OFF",
            _ => "MARKET CLOSED",
        };
        return armed ? "ARMED" : "DISARMED";
    }

    private bool IsArmedUnsafe() => _armedUntil is not null && _armedUntil > DateTimeOffset.UtcNow;
    private static bool IsTerminal(string status) =>
        NormalizeStatus(status) is "COMPLETE" or "REJECTED" or "CANCELLED";

    private static string NormalizeStatus(string status)
    {
        var value = status.Trim().ToUpperInvariant();
        if (value is "COMPLETE" or "COMPLETED" or "FILLED") return "COMPLETE";
        if (value.Contains("REJECT")) return "REJECTED";
        if (value.Contains("CANCEL")) return "CANCELLED";
        if (value.Contains("OPEN") || value.Contains("PENDING") || value.Contains("TRIGGER")) return "OPEN";
        return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value;
    }

    private static string Exact(params string?[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}

internal static class LiveOrderSnapshotExtensions
{
    public static int PendingQuantity(this LiveOrderSnapshot order) =>
        Math.Max(0, order.Quantity - order.FilledQuantity);
}
