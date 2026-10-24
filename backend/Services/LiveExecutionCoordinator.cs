using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class LiveExecutionCoordinator : IHostedService, IDisposable, IZoneLiveAutoGateway
{
    public const string ArmAcknowledgement = "I UNDERSTAND LIVE ORDERS USE REAL MONEY";
    public const string ZoneAutoLiveAcknowledgement = "GO LIVE ZONE CSV ON REAL MONEY";
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private readonly object _gate = new();
    private readonly BrokerConnectionManager _brokers;
    private IBrokerOrderClient BrokerOrders => _brokers.Orders;
    private readonly IPaperProposalSource _paper;
    private readonly TradeHistoryStore _history;
    private readonly LiveExecutionOptions _options;
    private readonly TradingPlatformOptions _platform;
    private readonly ILogger<LiveExecutionCoordinator> _log;
    private Timer? _timer;
    private DateTimeOffset? _armedUntil;
    private bool _zoneAutoLive;
    private string _zoneAutoLiveProduct = "MIS";
    private int _zoneLiveBusy;
    private IReadOnlyList<BrokerPosition> _positions = [];
    private IReadOnlyList<BrokerOrder> _orders = [];
    private LiveBrokerPnlSnapshot? _brokerPnl;
    private IReadOnlyList<LiveEodPrompt> _eodPrompts = [];
    private string _reconciliation = "NOT RUN";
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
    private int _busy;

    public LiveExecutionCoordinator(
        BrokerConnectionManager brokers,
        IPaperProposalSource paper,
        TradeHistoryStore history,
        LiveExecutionOptions options,
        TradingPlatformOptions platform,
        ILogger<LiveExecutionCoordinator> log)
    {
        _brokers = brokers;
        _paper = paper;
        _history = history;
        _options = options;
        _platform = platform;
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
            if (!_brokers.HasSession)
            {
                _reconciliation = "Connect m.Stock before arming live trading.";
                return SnapshotUnsafe();
            }
            if (!string.Equals(acknowledgement.Trim(), ArmAcknowledgement, StringComparison.Ordinal))
            {
                _reconciliation = $"Type exactly: {ArmAcknowledgement}";
                return SnapshotUnsafe();
            }
            _armedUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ArmMinutes, 1, 120));
            _zoneAutoLive = false;
            _paper.SetAutoAdmitTrades(false);
            _reconciliation = "ARMED; awaiting separately confirmed proposal";
            _updatedAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public LiveExecutionSnapshot ArmZoneAutoLive(string acknowledgement, string product)
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
            if (!_brokers.HasSession)
            {
                _reconciliation = "Connect m.Stock before going live.";
                return SnapshotUnsafe();
            }
            if (!string.Equals(acknowledgement.Trim(), ZoneAutoLiveAcknowledgement, StringComparison.Ordinal))
            {
                _reconciliation = $"Type exactly: {ZoneAutoLiveAcknowledgement}";
                return SnapshotUnsafe();
            }
            product = product.Trim().ToUpperInvariant();
            if (product is not ("MIS" or "NRML"))
            {
                _reconciliation = "Product must be MIS or NRML.";
                return SnapshotUnsafe();
            }
            _armedUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ArmMinutes, 1, 120));
            _zoneAutoLive = true;
            _zoneAutoLiveProduct = product;
            _paper.SetAutoAdmitTrades(true);
            _reconciliation =
                $"ZONE AUTO LIVE ({product}): B1/S1/S2 zone signals on {string.Join(", ", ZoneBoardLiveScope.Indices)} "
                + "submit to the broker without per-trade confirm. Use Disarm to stop.";
            _updatedAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public LiveExecutionSnapshot Disarm()
    {
        lock (_gate)
        {
            _armedUntil = null;
            _zoneAutoLive = false;
            _paper.SetAutoAdmitTrades(true);
            _reconciliation = "DISARMED";
            _updatedAt = DateTimeOffset.UtcNow;
            return SnapshotUnsafe();
        }
    }

    public bool IsZoneAutoLiveActive()
    {
        lock (_gate) return IsZoneAutoLiveActiveUnsafe();
    }

    public bool ShouldAutoExecuteZoneLive(string strategy, string underlying)
    {
        lock (_gate)
        {
            if (!IsZoneAutoLiveActiveUnsafe()) return false;
            return ZoneBoardLiveScope.Qualifies(strategy, underlying);
        }
    }

    public async Task ExecuteZoneProposalAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _zoneLiveBusy, 1) == 1) return;
        try
        {
            string product;
            lock (_gate)
            {
                if (!IsZoneAutoLiveActiveUnsafe())
                {
                    return;
                }
                product = _zoneAutoLiveProduct;
            }

            var snapshot = await ConfirmAsync(proposalId, product, cancellationToken);
            if (snapshot.PendingProposal is not null
                && snapshot.PendingProposal.Id.Equals(proposalId, StringComparison.Ordinal))
            {
                _paper.ReleasePendingProposal(proposalId, "Zone live submission did not complete.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _zoneLiveBusy, 0);
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
            if (!_brokers.HasSession) return WithStatusUnsafe("Connect your broker before confirming a live trade.");
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

        if (!ValidateZoneLivePreflight(proposal, out var preflightReason))
        {
            lock (_gate) return WithStatusUnsafe(preflightReason);
        }

        if (!InstrumentExecutionPolicy.LiveBrokerRoutingAllowed(proposal, _platform.FuturesEnabled))
        {
            lock (_gate) return WithStatusUnsafe(InstrumentExecutionPolicy.FuturesDisabledLiveMessage);
        }

        _history.SaveLiveTrade(proposal, product);
        _history.SetLiveTradeStatus(proposal.Id, "SUBMITTING");
        var legs = (proposal.Legs ?? []).OrderBy(leg => leg.Side == "BUY" ? 0 : 1).ToList();
        var completed = new List<(PaperTradeLegProposal Leg, LiveOrderSnapshot Order)>();

        if (proposal.Strategy is "NAKED_INDEX_OPTION_SHORT" or "NAKED_STOCK_OPTION_SHORT")
        {
            var naked = legs.SingleOrDefault();
            if (naked is null || naked.Side != "SELL" || naked.InstrumentType != "OPTION")
            {
                return Fail(proposal.Id, "Invalid naked option proposal.");
            }
            var marginOrder = BuildOrder(proposal.Id, naked, product, "ENTRY");
            var margin = await BrokerOrders.CalculateMarginAsync(marginOrder, ct);
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
            var placed = await BrokerOrders.PlaceOrderAsync(request, ct);
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
                if (settled.PendingQuantity() > 0) await BrokerOrders.CancelOrderAsync(settled.BrokerOrderId, ct);
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
                var result = await BrokerOrders.ConvertPositionAsync(
                    position.TradingSymbol, position.Exchange, side, Math.Abs(position.Quantity), "MIS", "NRML", ct);
                if (!result.Success)
                {
                    converted = false;
                    reason = result.Message;
                    break;
                }
            }
            await RefreshBrokerBookAsync(ct);
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
            if (_brokers.HasSession)
            {
                await RefreshBrokerBookAsync(ct);
            }

            if (!_options.Enabled) return;
            if (!_brokers.HasSession) return;
            IReadOnlyList<BrokerOrder> orders;
            lock (_gate) orders = _orders;
            if (orders.Count == 0)
            {
                var ordersResult = await BrokerOrders.GetOrdersAsync(ct);
                if (!ordersResult.Success)
                {
                    lock (_gate) _reconciliation = Exact("ORDER RECONCILIATION FAILED", ordersResult.Message);
                    return;
                }
                orders = ordersResult.Data ?? [];
            }

            var byId = orders.ToDictionary(order => order.OrderId, StringComparer.OrdinalIgnoreCase);
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

    private async Task RefreshBrokerBookAsync(CancellationToken ct)
    {
        var positionsResult = await BrokerOrders.GetPositionsAsync(ct);
        var ordersResult = await BrokerOrders.GetOrdersAsync(ct);
        lock (_gate)
        {
            if (positionsResult.Success)
            {
                _positions = positionsResult.Data ?? [];
                _brokerPnl = ComputeBrokerPnl(_positions);
            }

            if (ordersResult.Success)
            {
                _orders = ordersResult.Data ?? [];
            }

            if (positionsResult.Success && ordersResult.Success && !_options.Enabled)
            {
                _reconciliation = "BROKER BOOK SYNCED";
            }
            else if (!positionsResult.Success)
            {
                _reconciliation = Exact("POSITION SYNC FAILED", positionsResult.Message);
            }
            else if (!ordersResult.Success)
            {
                _reconciliation = Exact("ORDER SYNC FAILED", ordersResult.Message);
            }

            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    internal static LiveBrokerPnlSnapshot ComputeBrokerPnl(IReadOnlyList<BrokerPosition> positions)
    {
        decimal unrealised = 0;
        decimal realised = 0;
        decimal openCost = 0;
        foreach (var position in positions)
        {
            if (position.Quantity != 0)
            {
                unrealised += position.Pnl;
                openCost += Math.Abs(position.AveragePrice * position.Quantity);
            }
            else
            {
                realised += position.Pnl;
            }
        }

        var total = unrealised + realised;
        decimal? unrealisedPercent = openCost > 0
            ? decimal.Round(unrealised / openCost * 100m, 2)
            : null;
        return new(total, realised, unrealised, unrealisedPercent);
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
            var placed = await BrokerOrders.PlaceOrderAsync(request, ct);
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
            var placed = await BrokerOrders.PlaceOrderAsync(request, ct);
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
            var result = await BrokerOrders.GetOrdersAsync(ct);
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
            exchange = leg.InstrumentType switch
            {
                "OPTION" => leg.Underlying == "SENSEX" ? "BFO" : "NFO",
                "FUTURE" => leg.Underlying == "SENSEX" ? "BFO" : "NFO",
                _ => "NSE",
            };
        }
        var tag = $"TWB-{tradeId[..Math.Min(8, tradeId.Length)]}-{purpose[..Math.Min(3, purpose.Length)]}-{leg.Id[^Math.Min(3, leg.Id.Length)..]}";
        return new(
            exchange,
            leg.Symbol,
            leg.Side,
            leg.Quantity,
            product,
            "LIMIT",
            price,
            0,
            "DAY",
            "regular",
            tag,
            leg.Underlying,
            leg.Expiry?.UtcDateTime,
            leg.Strike,
            leg.OptionRight,
            leg.InstrumentType);
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
        var zoneAuto = IsZoneAutoLiveActiveUnsafe();
        var session = CurrentSessionUnsafe();
        var brokerConnected = _brokers.HasSession;
        var state = ResolveState(session, brokerConnected, armed, zoneAuto);
        return new(
            _options.Enabled,
            brokerConnected,
            session,
            armed,
            armed ? _armedUntil : null,
            zoneAuto,
            _zoneAutoLiveProduct,
            state,
            armed ? _paper.Snapshot().PendingTrade : null,
            trades,
            _positions,
            _orders,
            _brokerPnl,
            _history.RecentLiveEvents(),
            _eodPrompts,
            _reconciliation,
            _updatedAt);
    }

    private MarketSessionSnapshot CurrentSessionUnsafe() =>
        _options.TreatMarketAsOpen
            ? new("OPEN", true, "Market is open for live entries until 15:10 IST.", null)
            : MarketSession.Evaluate(DateTimeOffset.UtcNow);

    private string ResolveState(MarketSessionSnapshot session, bool brokerConnected, bool armed, bool zoneAuto)
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
        if (zoneAuto) return "ZONE AUTO LIVE";
        return armed ? "ARMED" : "DISARMED";
    }

    private bool IsArmedUnsafe() => _armedUntil is not null && _armedUntil > DateTimeOffset.UtcNow;
    private bool IsZoneAutoLiveActiveUnsafe() => _zoneAutoLive && IsArmedUnsafe();

    private bool ValidateZoneLivePreflight(PaperTradeProposal proposal, out string reason)
    {
        reason = "";
        if (!ZoneBoardLiveScope.Qualifies(proposal.Strategy, proposal.Underlying))
        {
            return true;
        }

        var openOnUnderlying = _history.ReadLiveTrades(true)
            .Count(trade => trade.Status is "OPEN" or "OPEN_NRML" or "SUBMITTING"
                && trade.Proposal.Underlying.Equals(proposal.Underlying, StringComparison.OrdinalIgnoreCase));
        if (openOnUnderlying >= 1)
        {
            reason = $"Zone live already has an open broker trade on {proposal.Underlying}.";
            return false;
        }

        var legs = proposal.Legs ?? [];
        var estimatedDebit = legs
            .Where(leg => leg.Side == "BUY")
            .Sum(leg => Math.Max(0m, leg.IndicativePrice) * leg.Quantity);
        if (estimatedDebit <= 0)
        {
            reason = "Zone live entry requires positive option premium estimates.";
            return false;
        }

        var funds = _paper.Snapshot().BrokerAvailableFunds;
        if (funds is not null && estimatedDebit > funds)
        {
            reason =
                $"Insufficient broker funds for zone live entry: need about ₹{estimatedDebit:N0}, "
                + $"available ₹{funds:N0}.";
            return false;
        }

        return true;
    }
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
