using System.Text.Json;

namespace TradeWithBeast.Broker;

internal static class MStockJson
{
    public static bool IsOtpSendAcknowledged(JsonElement root)
    {
        if (IsStatusSuccess(root))
        {
            return true;
        }

        if (root.TryGetProperty("message", out var msg))
        {
            var text = msg.GetString() ?? "";
            if (text.Contains("otp", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("fail", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsStatusSuccess(JsonElement root)
    {
        if (TryReadStatusSuccess(root))
        {
            return true;
        }

        return root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && TryReadStatusSuccess(data);
    }

    public static string? ReadMessage(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msg))
        {
            return msg.GetString();
        }

        return null;
    }

    private static bool TryReadStatusSuccess(JsonElement element)
    {
        if (!element.TryGetProperty("status", out var status))
        {
            return false;
        }

        return status.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(status.GetString()?.Trim(), "success", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.GetString()?.Trim(), "ok", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
