using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class BrokerReconciliationEngine
{
    public sealed record PositionKey(string Symbol, string Product);

    public sealed record CompareInput(
        IReadOnlyList<TradeHistoryStore.StoredLiveTrade> LiveTrades,
        IReadOnlyList<BrokerPosition> BrokerPositions,
        IReadOnlyList<BrokerOrder> BrokerOrders);

    public sealed record CompareResult(
        int PositionMatches,
        int PositionMismatches,
        int OrderMatches,
        int OrderMismatches,
        IReadOnlyList<BrokerReconciliationMismatch> Mismatches,
        IReadOnlyList<string> SuggestedActions);

    public static CompareResult Compare(CompareInput input)
    {
        var appPositions = BuildApplicationPositions(input.LiveTrades);
        var brokerPositions = BuildBrokerPositions(input.BrokerPositions);
        var mismatches = new List<BrokerReconciliationMismatch>();
        var positionMatches = 0;
        var positionMismatches = 0;

        var keys = appPositions.Keys.Union(brokerPositions.Keys).ToHashSet();
        foreach (var key in keys)
        {
            var hasApp = appPositions.TryGetValue(key, out var app);
            var hasBroker = brokerPositions.TryGetValue(key, out var broker);
            if (hasApp && hasBroker && app!.Quantity == broker!.Quantity && app.Product == broker.Product)
            {
                positionMatches++;
                continue;
            }

            positionMismatches++;
            if (hasApp && !hasBroker)
            {
                mismatches.Add(new(
                    "POSITION",
                    key.Symbol,
                    FormatAppPosition(app!),
                    "Qty 0",
                    "Missing broker position",
                    "Refresh broker positions"));
            }
            else if (!hasApp && hasBroker)
            {
                mismatches.Add(new(
                    "POSITION",
                    key.Symbol,
                    "No position",
                    FormatBrokerPosition(broker!),
                    "Extra broker position",
                    "Review open position"));
            }
            else if (hasApp && hasBroker)
            {
                mismatches.Add(new(
                    "POSITION",
                    key.Symbol,
                    FormatAppPosition(app!),
                    FormatBrokerPosition(broker!),
                    PositionMismatchReason(app!, broker!),
                    "Review open position"));
            }
        }

        var orderMatches = 0;
        var orderMismatches = 0;
        var brokerById = input.BrokerOrders
            .Where(o => !string.IsNullOrWhiteSpace(o.OrderId))
            .GroupBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var trade in input.LiveTrades)
        {
            foreach (var order in trade.Orders.Where(o => !string.IsNullOrWhiteSpace(o.BrokerOrderId)))
            {
                var appStatus = NormalizeOrderStatus(order.Status);
                if (!brokerById.TryGetValue(order.BrokerOrderId, out var brokerOrder))
                {
                    if (!IsTerminalAppOrder(appStatus))
                    {
                        orderMismatches++;
                        mismatches.Add(new(
                            "ORDER",
                            order.Symbol,
                            $"Status {appStatus}, Qty {order.FilledQuantity}/{order.Quantity}",
                            "Not found at broker",
                            "Missing broker order",
                            "Refresh orders"));
                    }

                    continue;
                }

                var brokerStatus = NormalizeOrderStatus(brokerOrder.Status);
                var statusMatch = StatusEquivalent(appStatus, brokerStatus);
                var fillMatch = order.FilledQuantity == brokerOrder.FilledQuantity
                    || (IsComplete(appStatus) && brokerOrder.FilledQuantity >= order.Quantity);
                if (statusMatch && fillMatch)
                {
                    orderMatches++;
                    continue;
                }

                orderMismatches++;
                var reason = !statusMatch
                    ? "Order status mismatch"
                    : "Partial fill mismatch";
                mismatches.Add(new(
                    "ORDER",
                    order.Symbol,
                    $"Status {appStatus}, filled {order.FilledQuantity}/{order.Quantity}",
                    $"Status {brokerStatus}, filled {brokerOrder.FilledQuantity}/{brokerOrder.Quantity}",
                    reason,
                    "Refresh orders"));
            }
        }

        foreach (var trade in input.LiveTrades)
        {
            ValidateLiveTrade(trade, appPositions, brokerPositions, mismatches);
        }

        var suggestions = BuildSuggestions(mismatches);
        return new(positionMatches, positionMismatches, orderMatches, orderMismatches, mismatches, suggestions);
    }

    internal static Dictionary<PositionKey, AppPositionState> BuildApplicationPositions(
        IReadOnlyList<TradeHistoryStore.StoredLiveTrade> trades)
    {
        var map = new Dictionary<PositionKey, AppPositionState>();
        foreach (var trade in trades.Where(t => !IsFailedTrade(t.Status)))
        {
            foreach (var order in trade.Orders)
            {
                var delta = SignedPositionDelta(order);
                if (delta == 0)
                {
                    continue;
                }

                var key = new PositionKey(NormalizeSymbol(order.Symbol), NormalizeProduct(order.Product));
                if (!map.TryGetValue(key, out var current))
                {
                    current = new AppPositionState(key.Symbol, key.Product, 0);
                }

                map[key] = current with { Quantity = current.Quantity + delta };
            }
        }

        return map.Where(pair => pair.Value.Quantity != 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    internal static Dictionary<PositionKey, BrokerPositionState> BuildBrokerPositions(
        IReadOnlyList<BrokerPosition> positions) =>
        positions
            .Where(p => p.Quantity != 0)
            .GroupBy(p => new PositionKey(NormalizeSymbol(p.TradingSymbol), NormalizeProduct(p.Product)))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    var qty = g.Sum(p => p.Quantity);
                    return new BrokerPositionState(
                        g.Key.Symbol,
                        g.Key.Product,
                        qty,
                        first.OvernightQuantity,
                        qty > 0 ? "BUY" : "SELL");
                });

    private static void ValidateLiveTrade(
        TradeHistoryStore.StoredLiveTrade trade,
        Dictionary<PositionKey, AppPositionState> appPositions,
        Dictionary<PositionKey, BrokerPositionState> brokerPositions,
        List<BrokerReconciliationMismatch> mismatches)
    {
        if (!IsOpenTrade(trade.Status))
        {
            if (trade.Status is "CLOSED")
            {
                foreach (var leg in trade.Proposal.Legs ?? [])
                {
                    var key = new PositionKey(NormalizeSymbol(leg.Symbol), NormalizeProduct(trade.Product));
                    if (brokerPositions.TryGetValue(key, out var broker) && broker.Quantity != 0)
                    {
                        mismatches.Add(new(
                            "LIVE_TRADE",
                            leg.Symbol,
                            "Position closed",
                            FormatBrokerPosition(broker),
                            "Closed trade still open at broker",
                            "Review open position"));
                    }
                }
            }

            return;
        }

        var expectedKeys = (trade.Proposal.Legs ?? [])
            .Select(leg => new PositionKey(NormalizeSymbol(leg.Symbol), NormalizeProduct(trade.Product)))
            .Distinct()
            .ToList();
        foreach (var key in expectedKeys)
        {
            var hasBroker = brokerPositions.TryGetValue(key, out var broker);
            var hasApp = appPositions.TryGetValue(key, out var app);
            if (IsOpenTrade(trade.Status) && !hasBroker && hasApp && app!.Quantity != 0)
            {
                mismatches.Add(new(
                    "LIVE_TRADE",
                    key.Symbol,
                    FormatAppPosition(app!),
                    "Qty 0",
                    "Open live trade without broker position",
                    "Refresh broker positions"));
            }
        }
    }

    private static List<string> BuildSuggestions(IReadOnlyList<BrokerReconciliationMismatch> mismatches)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mismatches)
        {
            if (!string.IsNullOrWhiteSpace(item.SuggestedAction))
            {
                set.Add(item.SuggestedAction);
            }
        }

        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static int SignedPositionDelta(LiveOrderSnapshot order)
    {
        var qty = EffectiveFilledQuantity(order);
        if (qty == 0)
        {
            return 0;
        }

        var isBuy = order.TransactionType.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var purpose = order.Purpose.ToUpperInvariant();
        if (purpose is "ENTRY" or "COMPENSATION")
        {
            return isBuy ? qty : -qty;
        }

        if (purpose is "EXIT")
        {
            return isBuy ? -qty : qty;
        }

        return isBuy ? qty : -qty;
    }

    internal static int EffectiveFilledQuantity(LiveOrderSnapshot order)
    {
        if (order.FilledQuantity > 0)
        {
            return order.FilledQuantity;
        }

        return IsComplete(NormalizeOrderStatus(order.Status)) ? order.Quantity : 0;
    }

    internal static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    internal static string NormalizeProduct(string product) =>
        string.IsNullOrWhiteSpace(product) ? "MIS" : product.Trim().ToUpperInvariant();

    internal static bool IsOpenTrade(string status) =>
        status is "OPEN" or "OPEN_NRML" or "SUBMITTING" or "EXITING";

    internal static bool IsFailedTrade(string status) => status is "FAILED";

    internal static string NormalizeOrderStatus(string status)
    {
        var normalized = (status ?? "").Trim().ToUpperInvariant();
        if (normalized.Contains("COMPLETE", StringComparison.Ordinal))
        {
            return "COMPLETE";
        }

        if (normalized.Contains("REJECT", StringComparison.Ordinal))
        {
            return "REJECTED";
        }

        if (normalized.Contains("CANCEL", StringComparison.Ordinal))
        {
            return "CANCELLED";
        }

        if (normalized is "SUBMITTED" or "OPEN" or "TRIGGER PENDING" or "PENDING")
        {
            return "SUBMITTED";
        }

        return normalized;
    }

    private static bool IsTerminalAppOrder(string status) =>
        status is "COMPLETE" or "REJECTED" or "CANCELLED" or "FAILED";

    private static bool IsComplete(string status) => status is "COMPLETE";

    private static bool StatusEquivalent(string app, string broker) =>
        app == broker
        || (app is "COMPLETE" && broker is "COMPLETE")
        || (app is "REJECTED" && broker is "REJECTED")
        || (app is "CANCELLED" && broker is "CANCELLED");

    private static string PositionMismatchReason(AppPositionState app, BrokerPositionState broker)
    {
        if (app.Quantity != broker.Quantity)
        {
            return "Quantity mismatch";
        }

        if (!string.Equals(app.Product, broker.Product, StringComparison.OrdinalIgnoreCase))
        {
            return "Product type mismatch";
        }

        return "Position mismatch";
    }

    private static string FormatAppPosition(AppPositionState app) =>
        $"Qty {app.Quantity} ({app.Side}), {app.Product}";

    private static string FormatBrokerPosition(BrokerPositionState broker) =>
        $"Qty {broker.Quantity} ({broker.Side}), {broker.Product}, open qty {broker.OpenQuantity}";

    internal sealed record AppPositionState(string Symbol, string Product, int Quantity)
    {
        public string Side => Quantity > 0 ? "BUY" : "SELL";
    }

    internal sealed record BrokerPositionState(
        string Symbol,
        string Product,
        int Quantity,
        int OpenQuantity,
        string Side);
}
