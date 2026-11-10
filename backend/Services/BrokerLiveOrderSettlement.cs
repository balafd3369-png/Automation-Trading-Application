using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Poll broker order book until terminal status (shared by manual and strategy live).</summary>
public sealed class BrokerLiveOrderSettlement
{
    private readonly LiveExecutionOptions _options;

    public BrokerLiveOrderSettlement(LiveExecutionOptions options) => _options = options;

    public async Task<BrokerOrderTerminalResult> WaitForTerminalAsync(
        IBrokerOrderClient broker,
        string brokerOrderId,
        int orderQuantity,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_options.OrderTimeoutSeconds, 3, 60));
        BrokerOrder? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(500, ct);
            var result = await broker.GetOrdersAsync(ct);
            latest = result.Data?.FirstOrDefault(item => item.OrderId == brokerOrderId);
            if (latest is not null && IsTerminal(latest.Status))
            {
                break;
            }
        }

        if (latest is null)
        {
            return new(false, "UNKNOWN", 0, orderQuantity, "Broker order was not found before timeout.");
        }

        var status = NormalizeStatus(latest.Status);
        if (!IsTerminal(latest.Status))
        {
            return new(
                false,
                "TIMEOUT",
                latest.FilledQuantity,
                latest.Quantity > 0 ? latest.Quantity : orderQuantity,
                "Broker fill was not confirmed before timeout.");
        }

        var message = string.IsNullOrWhiteSpace(latest.StatusMessage) ? latest.Status : latest.StatusMessage;
        return new(
            status == "COMPLETE" && latest.FilledQuantity >= Math.Max(1, latest.Quantity > 0 ? latest.Quantity : orderQuantity),
            status,
            latest.FilledQuantity,
            latest.Quantity > 0 ? latest.Quantity : orderQuantity,
            message ?? status);
    }

    public static bool IsTerminal(string status) =>
        NormalizeStatus(status) is "COMPLETE" or "REJECTED" or "CANCELLED";

    public static string NormalizeStatus(string status)
    {
        var value = status.Trim().ToUpperInvariant();
        if (value is "COMPLETE" or "COMPLETED" or "FILLED") return "COMPLETE";
        if (value.Contains("REJECT")) return "REJECTED";
        if (value.Contains("CANCEL")) return "CANCELLED";
        if (value.Contains("OPEN") || value.Contains("PENDING") || value.Contains("TRIGGER")) return "OPEN";
        return string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value;
    }
}

public sealed record BrokerOrderTerminalResult(
    bool FullyFilled,
    string Status,
    int FilledQuantity,
    int OrderQuantity,
    string Message);
