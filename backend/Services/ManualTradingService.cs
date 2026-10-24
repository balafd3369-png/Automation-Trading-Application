using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class ManualTradingService
{
    private const decimal PriceProtectionPercent = 0.25m;
    private readonly TradeHistoryStore _history;
    private readonly ManualOrderCapitalValidator _capital;
    private readonly MockMarketService _market;
    private readonly PaperTradingAgent _paper;
    private readonly BrokerConnectionManager _brokers;
    private readonly TradingPlatformOptions _platform;
    private readonly LiveExecutionOptions _liveOptions;
    private readonly ILogger<ManualTradingService> _log;

    public ManualTradingService(
        TradeHistoryStore history,
        ManualOrderCapitalValidator capital,
        MockMarketService market,
        PaperTradingAgent paper,
        BrokerConnectionManager brokers,
        TradingPlatformOptions platform,
        LiveExecutionOptions liveOptions,
        ILogger<ManualTradingService> log)
    {
        _history = history;
        _capital = capital;
        _market = market;
        _paper = paper;
        _brokers = brokers;
        _platform = platform;
        _liveOptions = liveOptions;
        _log = log;
    }

    public ManualTradingSnapshot GetSnapshot(string executionMode)
    {
        var mode = NormalizeMode(executionMode);
        var now = DateTimeOffset.UtcNow;
        var capital = mode == "PAPER" ? _capital.BuildPaperSummary() : _capital.BuildLiveSummary();
        RefreshManualPaperMarks();
        var capitalPerTrade = _paper.Snapshot().Config?.AllocatedCapital is > 0
            ? _paper.Snapshot().Config!.AllocatedCapital / 10
            : 100_000m;
        var history = _history.QueryManualPaperTrades(now.AddMonths(-3), now, capitalPerTrade);
        var positions = history
            .Where(t => t.Status == "OPEN")
            .Select(ToManualPosition)
            .ToList();
        var closedHistory = history.Where(t => t.Status != "OPEN").ToList();
        var orders = _history.ListManualOrders(mode, includeTerminal: true);
        var holdings = BuildHoldings(positions, mode);
        return new(mode, capital, orders, positions, holdings, closedHistory, DateTimeOffset.UtcNow);
    }

    public async Task<(bool Success, string Message, ManualOrderSnapshot? Order)> PlaceOrderAsync(
        ManualTradingPlaceOrderRequest request,
        CancellationToken ct)
    {
        var mode = NormalizeMode(request.ExecutionMode);
        var side = NormalizeSide(request.TransactionType);
        var orderType = NormalizeOrderType(request.OrderType);
        var instrument = InstrumentTypeNormalizer.ToApiValue(InstrumentTypeNormalizer.Parse(request.InstrumentType));
        var symbol = request.Symbol.Trim();
        var underlying = string.IsNullOrWhiteSpace(request.Underlying) ? symbol : request.Underlying.Trim();
        if (string.IsNullOrWhiteSpace(symbol) || request.Quantity <= 0)
        {
            return (false, "Symbol and positive quantity are required.", null);
        }

        var quantityCheck = TradingQuantityRules.Validate(
            request.InstrumentType,
            underlying,
            symbol,
            request.Quantity);
        if (!quantityCheck.Valid)
        {
            return (false, quantityCheck.Message, null);
        }

        var reference = await ResolveReferencePriceAsync(symbol, underlying, instrument, ct);
        if (reference is null or <= 0)
        {
            return (false, $"No reference price for {symbol}. Open it on the board or connect the broker.", null);
        }

        var fillPrice = ResolveFillPrice(orderType, side, reference.Value, request.LimitPrice);
        if (fillPrice is null)
        {
            return await PlacePendingLimitAsync(request, mode, side, orderType, instrument, symbol, underlying, reference.Value, ct);
        }

        var required = PaperTradeEconomics.CapitalDeployed(request.Quantity, fillPrice.Value);
        var capitalCheck = mode == "PAPER"
            ? _capital.ValidatePaper(required)
            : _capital.ValidateLive(required, useMarginEstimate: false);
        if (!capitalCheck.Allowed)
        {
            return (false, capitalCheck.Message, null);
        }

        if (mode == "PAPER")
        {
            return PlacePaperFill(request, side, instrument, symbol, underlying, fillPrice.Value, required);
        }

        return await PlaceLiveOrderAsync(request, side, orderType, instrument, symbol, underlying, fillPrice.Value, ct);
    }

    public (bool Success, string Message) CancelOrder(string orderId)
    {
        var order = _history.GetManualOrder(orderId);
        if (order is null)
        {
            return (false, "Order not found.");
        }

        if (order.Status is "COMPLETE" or "CANCELLED" or "REJECTED")
        {
            return (false, $"Order is already {order.Status}.");
        }

        var now = DateTimeOffset.UtcNow;
        _history.UpsertManualOrder(order with { Status = "CANCELLED", Message = "Cancelled by user.", UpdatedAt = now });
        return (true, "Order cancelled.");
    }

    public async Task<(bool Success, string Message)> ClosePositionAsync(
        ManualTradingClosePositionRequest request,
        CancellationToken ct)
    {
        var mode = NormalizeMode(request.ExecutionMode);
        var trade = _history.GetManualPaperTrade(request.TradeId);
        if (trade is null || trade.Status != "OPEN")
        {
            return (false, "Manual paper position not found or already closed.");
        }

        if (mode != "PAPER")
        {
            return (false, "Live manual position exit uses broker positions — close from the live book or place a sell order.");
        }

        RefreshManualPaperMarks();
        trade = _history.GetManualPaperTrade(request.TradeId)!;
        var leg = trade.Legs.FirstOrDefault();
        if (leg is null)
        {
            return (false, "Position has no legs.");
        }

        var qty = request.Quantity ?? leg.Quantity;
        if (qty <= 0 || qty > leg.Quantity)
        {
            return (false, "Invalid close quantity.");
        }

        var exitPrice = leg.CurrentPrice ?? leg.EntryPrice ?? leg.ProposedPrice;
        var pnl = PaperTradeEconomics.RealizedPnl(trade.Direction, leg.EntryPrice ?? leg.ProposedPrice, exitPrice, qty);
        var legs = new List<PaperPositionLegSnapshot>
        {
            new(leg.Id, leg.Symbol, leg.Side, qty, leg.EntryPrice ?? leg.ProposedPrice, exitPrice, pnl, leg.Strike, leg.OptionRight, leg.Expiry),
        };
        _history.CloseMultiLegAtomic(trade.Id, legs, "manual close", pnl, DateTimeOffset.UtcNow);
        return (true, $"Closed {qty} @ ₹{exitPrice:N2} (P&L ₹{pnl:N2}).");
    }

    private (bool Success, string Message, ManualOrderSnapshot? Order) PlacePaperFill(
        ManualTradingPlaceOrderRequest request,
        string side,
        string instrument,
        string symbol,
        string underlying,
        decimal fillPrice,
        decimal required)
    {
        var tradeId = Guid.NewGuid().ToString("N");
        var legId = $"{tradeId}-leg-1";
        var direction = side == "BUY" ? "LONG" : "SHORT";
        var proposal = new PaperTradeProposal(
            tradeId,
            instrument,
            symbol,
            direction,
            request.Quantity,
            fillPrice,
            0,
            0,
            "Manual paper order",
            DateTimeOffset.UtcNow.AddHours(8),
            TradeHistoryStore.ManualStrategyId,
            underlying,
            CapitalRequired: required,
            Legs:
            [
                new PaperTradeLegProposal(legId, symbol, underlying, instrument, side, 1, request.Quantity, fillPrice,
                    request.Strike, request.OptionRight, request.Expiry),
            ]);
        var legs = new List<PaperPositionLegSnapshot>
        {
            new(legId, symbol, side, request.Quantity, fillPrice, fillPrice, 0, request.Strike, request.OptionRight, request.Expiry),
        };
        var now = DateTimeOffset.UtcNow;
        _history.SaveManualPaperTradeOpen(proposal, legs, now);
        var order = new ManualOrderSnapshot(
            Guid.NewGuid().ToString("N"),
            "PAPER",
            TradeSourceWire.Manual,
            "COMPLETE",
            symbol,
            underlying,
            instrument,
            side,
            NormalizeOrderType(request.OrderType),
            request.Product,
            ResolveExchange(instrument, underlying),
            request.Quantity,
            request.LimitPrice,
            request.Quantity,
            fillPrice,
            null,
            tradeId,
            "Paper fill simulated.",
            now,
            now);
        _history.UpsertManualOrder(order);
        return (true, $"Paper {side} {request.Quantity} {symbol} @ ₹{fillPrice:N2}.", order);
    }

    private async Task<(bool Success, string Message, ManualOrderSnapshot? Order)> PlacePendingLimitAsync(
        ManualTradingPlaceOrderRequest request,
        string mode,
        string side,
        string orderType,
        string instrument,
        string symbol,
        string underlying,
        decimal reference,
        CancellationToken ct)
    {
        if (orderType != "LIMIT" || request.LimitPrice is not > 0)
        {
            return (false, "Limit price is required for unfilled limit orders.", null);
        }

        var required = PaperTradeEconomics.CapitalDeployed(request.Quantity, request.LimitPrice.Value);
        var capitalCheck = mode == "PAPER"
            ? _capital.ValidatePaper(required)
            : _capital.ValidateLive(required, useMarginEstimate: false);
        if (!capitalCheck.Allowed)
        {
            return (false, capitalCheck.Message, null);
        }

        var now = DateTimeOffset.UtcNow;
        var order = new ManualOrderSnapshot(
            Guid.NewGuid().ToString("N"),
            mode,
            TradeSourceWire.Manual,
            "PENDING",
            symbol,
            underlying,
            instrument,
            side,
            orderType,
            request.Product,
            ResolveExchange(instrument, underlying),
            request.Quantity,
            request.LimitPrice,
            0,
            0,
            null,
            null,
            $"Waiting for market (ref ₹{reference:N2}, limit ₹{request.LimitPrice:N2}).",
            now,
            now);
        _history.UpsertManualOrder(order);
        await Task.CompletedTask;
        return (true, "Limit order queued (paper). It will fill when price is reachable on refresh.", order);
    }

    private async Task<(bool Success, string Message, ManualOrderSnapshot? Order)> PlaceLiveOrderAsync(
        ManualTradingPlaceOrderRequest request,
        string side,
        string orderType,
        string instrument,
        string symbol,
        string underlying,
        decimal referencePrice,
        CancellationToken ct)
    {
        if (!_liveOptions.Enabled)
        {
            return (false, "Live trading is disabled in configuration.", null);
        }

        if (!_brokers.HasSession)
        {
            return (false, "Connect m.Stock before placing live manual orders.", null);
        }

        var proposal = new PaperTradeProposal(
            Guid.NewGuid().ToString("N"),
            instrument,
            symbol,
            side == "BUY" ? "LONG" : "SHORT",
            request.Quantity,
            referencePrice,
            0,
            0,
            "Manual live order",
            DateTimeOffset.UtcNow.AddMinutes(30),
            TradeHistoryStore.ManualStrategyId,
            underlying,
            Legs:
            [
                new PaperTradeLegProposal(
                    "leg-1", symbol, underlying, instrument, side, 1, request.Quantity, referencePrice,
                    request.Strike, request.OptionRight, request.Expiry),
            ]);

        if (!InstrumentExecutionPolicy.LiveBrokerRoutingAllowed(proposal, _platform.FuturesEnabled))
        {
            return (false, InstrumentExecutionPolicy.FuturesDisabledLiveMessage, null);
        }

        if (side == "SELL" && instrument == "OPTION")
        {
            var marginOrder = BuildBrokerOrder(proposal.Id, proposal.Legs![0], request.Product, referencePrice, orderType, request.LimitPrice);
            var margin = await _brokers.Orders.CalculateMarginAsync(marginOrder, ct);
            if (margin.Success && margin.Data is not null)
            {
                var check = _capital.ValidateLive(margin.Data.TotalMargin, useMarginEstimate: true);
                if (!check.Allowed)
                {
                    return (false, check.Message, null);
                }
            }
        }

        var brokerRequest = BuildBrokerOrder(proposal.Id, proposal.Legs![0], request.Product, referencePrice, orderType, request.LimitPrice);
        var placed = await _brokers.Orders.PlaceOrderAsync(brokerRequest, ct);
        var now = DateTimeOffset.UtcNow;
        if (!placed.Success || string.IsNullOrWhiteSpace(placed.Data))
        {
            return (false, placed.Message, null);
        }

        _history.SaveManualLiveTrade(proposal, request.Product);
        var order = new ManualOrderSnapshot(
            Guid.NewGuid().ToString("N"),
            "LIVE",
            TradeSourceWire.Manual,
            "SUBMITTED",
            symbol,
            underlying,
            instrument,
            side,
            orderType,
            request.Product,
            brokerRequest.Exchange,
            request.Quantity,
            request.LimitPrice,
            0,
            0,
            placed.Data,
            proposal.Id,
            placed.Message,
            now,
            now);
        _history.UpsertManualOrder(order);
        _log.LogInformation("Manual live order {OrderId} broker={BrokerId} {Side} {Qty} {Symbol}", order.Id, placed.Data, side, request.Quantity, symbol);
        return (true, "Live order submitted to m.Stock.", order);
    }

    private BrokerOrderRequest BuildBrokerOrder(
        string tradeId,
        PaperTradeLegProposal leg,
        string product,
        decimal referencePrice,
        string orderType,
        decimal? limitPrice)
    {
        var protection = PriceProtectionPercent / 100m;
        decimal price;
        if (orderType == "LIMIT" && limitPrice is > 0)
        {
            price = limitPrice.Value;
        }
        else
        {
            var raw = leg.Side == "BUY"
                ? referencePrice * (1m + protection)
                : referencePrice * (1m - protection);
            price = Math.Max(0.05m, decimal.Round(raw / 0.05m, 0, MidpointRounding.AwayFromZero) * 0.05m);
        }

        var exchange = ResolveExchange(leg.InstrumentType, leg.Underlying);
        var tag = $"TWB-MAN-{tradeId[..Math.Min(6, tradeId.Length)]}";
        return new(
            exchange,
            leg.Symbol,
            leg.Side,
            leg.Quantity,
            product,
            orderType == "LIMIT" && limitPrice is > 0 ? "LIMIT" : "LIMIT",
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

    private void RefreshManualPaperMarks()
    {
        var board = _market.Snapshot();
        var now = DateTimeOffset.UtcNow;
        var capitalPerTrade = _paper.Snapshot().Config?.AllocatedCapital is > 0
            ? _paper.Snapshot().Config!.AllocatedCapital / 10
            : 100_000m;
        var open = _history.QueryManualPaperTrades(now.AddMonths(-3), now, capitalPerTrade)
            .Where(t => t.Status == "OPEN")
            .ToList();
        foreach (var trade in open)
        {
            var legs = trade.Legs.Select(leg =>
            {
                var mark = board.Symbols.FirstOrDefault(s =>
                    s.Name.Equals(leg.Symbol, StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals(trade.Underlying, StringComparison.OrdinalIgnoreCase))?.Last ?? leg.CurrentPrice ?? leg.ProposedPrice;
                var entry = leg.EntryPrice ?? leg.ProposedPrice;
                var legPnl = PaperTradeEconomics.LegPnl(leg with { CurrentPrice = mark });
                return new PaperPositionLegSnapshot(
                    leg.Id, leg.Symbol, leg.Side, leg.Quantity, entry, mark, legPnl,
                    leg.Strike, leg.OptionRight, leg.Expiry);
            }).ToList();
            _history.UpdateLegMarks(trade.Id, legs);
        }

    }

    private async Task<decimal?> ResolveReferencePriceAsync(
        string symbol,
        string underlying,
        string instrument,
        CancellationToken ct)
    {
        var board = _market.Snapshot();
        var hit = board.Symbols.FirstOrDefault(s =>
            s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase)
            || s.Name.Equals(underlying, StringComparison.OrdinalIgnoreCase));
        if (hit?.Last is > 0)
        {
            return hit.Last;
        }

        var ensured = await _market.EnsureSymbolAsync(symbol, ct);
        return ensured?.Last is > 0 ? ensured.Last : null;
    }

    private static decimal? ResolveFillPrice(string orderType, string side, decimal reference, decimal? limit)
    {
        if (orderType == "MARKET")
        {
            return reference;
        }

        if (limit is not > 0)
        {
            return null;
        }

        var marketable = side == "BUY" ? reference <= limit : reference >= limit;
        return marketable ? reference : null;
    }

    private static ManualPositionSnapshot ToManualPosition(PaperTradeHistorySnapshot trade)
    {
        var leg = trade.Legs.FirstOrDefault();
        var entry = leg is null ? trade.EntryPrice : PaperTradeEconomics.LegEntryPrice(leg);
        var mark = leg?.CurrentPrice ?? entry;
        var pnl = leg is null ? trade.Pnl ?? 0 : PaperTradeEconomics.LegPnl(leg);
        return new(
            trade.Id,
            trade.TradeSource,
            leg?.Symbol ?? trade.Underlying,
            trade.Underlying,
            leg?.InstrumentType ?? "STOCK",
            trade.Direction,
            trade.Quantity > 0 ? trade.Quantity : leg?.Quantity ?? 0,
            entry,
            mark,
            pnl,
            trade.CapitalRequired,
            "MIS",
            trade.OpenedAt ?? trade.CreatedAt);
    }

    private static IReadOnlyList<ManualHoldingSnapshot> BuildHoldings(
        IReadOnlyList<ManualPositionSnapshot> positions,
        string mode)
    {
        if (mode != "PAPER")
        {
            return [];
        }

        return positions
            .Where(p => p.InstrumentType.Equals("STOCK", StringComparison.OrdinalIgnoreCase) && p.Direction == "LONG")
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var qty = g.Sum(x => x.Quantity);
                var avg = g.Average(x => x.EntryPrice);
                var last = g.Last().CurrentPrice;
                return new ManualHoldingSnapshot(g.Key, "NSE", qty, avg, last, last * qty, (last - avg) * qty);
            })
            .ToList();
    }

    private static string NormalizeMode(string mode) =>
        mode.Equals("LIVE", StringComparison.OrdinalIgnoreCase) ? "LIVE" : "PAPER";

    private static string NormalizeSide(string side) =>
        side.Equals("SELL", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";

    private static string NormalizeOrderType(string orderType) =>
        orderType.Equals("LIMIT", StringComparison.OrdinalIgnoreCase) ? "LIMIT" : "MARKET";

    private static string ResolveExchange(string instrument, string underlying) =>
        instrument switch
        {
            "OPTION" or "FUTURE" => underlying.Equals("SENSEX", StringComparison.OrdinalIgnoreCase) ? "BFO" : "NFO",
            _ => "NSE",
        };
}
