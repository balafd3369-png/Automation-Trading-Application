using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Read-only startup audit: never resubmits orders.</summary>
public sealed class OrderStartupSafetyScanner
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETE", "COMPLETED", "FILLED", "REJECTED", "CANCELLED", "CANCELED",
    };

    private static readonly HashSet<string> UncertainStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNKNOWN", "TIMEOUT", "SUBMITTED", "OPEN", "PENDING", "TRIGGER PENDING",
    };

    private readonly TradeHistoryStore _history;
    private readonly BrokerConnectionManager _brokers;
    private readonly ILogger<OrderStartupSafetyScanner> _log;

    public OrderStartupSafetyScanner(
        TradeHistoryStore history,
        BrokerConnectionManager brokers,
        ILogger<OrderStartupSafetyScanner> log)
    {
        _history = history;
        _brokers = brokers;
        _log = log;
    }

    public async Task<OrderStartupAuditSummary> RunAsync(CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var findings = new List<OrderSafetyAuditFinding>();
        var reconciled = 0;
        var orders = _history.ListNonTerminalLiveOrders();
        IReadOnlyList<BrokerOrder>? brokerOrders = null;
        if (_brokers.HasSession)
        {
            try
            {
                var result = await _brokers.Orders.GetOrdersAsync(ct);
                if (result.Success)
                {
                    brokerOrders = result.Data ?? [];
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Startup order audit could not read broker order book.");
            }
        }

        foreach (var order in orders)
        {
            if (TerminalStatuses.Contains(BrokerLiveOrderSettlement.NormalizeStatus(order.Status)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(order.BrokerOrderId))
            {
                findings.Add(Finding(order, "MISSING_BROKER_ID", "Local order has no broker order id — do not resubmit without investigation."));
                continue;
            }

            if (UncertainStatuses.Contains(BrokerLiveOrderSettlement.NormalizeStatus(order.Status)))
            {
                var broker = brokerOrders?.FirstOrDefault(o => o.OrderId == order.BrokerOrderId);
                if (broker is null)
                {
                    findings.Add(Finding(order, "BROKER_NOT_FOUND", "Broker order book has no matching order — state uncertain."));
                    continue;
                }

                var normalized = BrokerLiveOrderSettlement.NormalizeStatus(broker.Status);
                if (!string.Equals(normalized, order.Status, StringComparison.OrdinalIgnoreCase))
                {
                    _history.UpsertLiveOrder(order with
                    {
                        Status = normalized,
                        FilledQuantity = broker.FilledQuantity,
                        AveragePrice = broker.AveragePrice,
                        BrokerMessage = broker.StatusMessage ?? broker.Status,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    });
                    reconciled++;
                    if (!TerminalStatuses.Contains(normalized))
                    {
                        findings.Add(Finding(order, "STILL_OPEN", $"Reconciled to broker status {normalized}; still non-terminal."));
                    }
                }
                else
                {
                    findings.Add(Finding(order, "UNCERTAIN", $"Order remains in uncertain status {order.Status}."));
                }
            }
        }

        var summary = new OrderStartupAuditSummary(
            Guid.NewGuid().ToString("N"),
            findings.Count,
            reconciled,
            findings.Count(f => f.FindingKind is "BROKER_NOT_FOUND" or "MISSING_BROKER_ID" or "UNCERTAIN"),
            correlationId,
            DateTimeOffset.UtcNow,
            findings);
        _log.LogInformation(
            "Startup order audit {AuditId}: {Findings} finding(s), {Reconciled} reconciled.",
            summary.Id,
            summary.FindingCount,
            summary.ReconciledCount);
        return summary;
    }

    private static OrderSafetyAuditFinding Finding(LiveOrderSnapshot order, string kind, string message) =>
        new(
            order.Id,
            order.TradeId,
            order.Symbol,
            order.Status,
            string.IsNullOrWhiteSpace(order.BrokerOrderId) ? null : order.BrokerOrderId,
            kind,
            message,
            DateTimeOffset.UtcNow);
}