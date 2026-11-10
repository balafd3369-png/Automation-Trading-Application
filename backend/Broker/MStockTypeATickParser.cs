using System.Buffers.Binary;

namespace TradeWithBeast.Broker;

/// <summary>Parses Type A binary tick packets per Mirae m.Stock market-data WebSocket docs.</summary>
public static class MStockTypeATickParser
{
    public const int IndexPacketBytes = 48;
    public const int QuotePacketBytes = 200;

    public static IReadOnlyList<MStockWebSocketTick> ParseMessage(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return [];
        }

        var packetCount = BinaryPrimitives.ReadInt16LittleEndian(payload);
        var totalPacketBytes = BinaryPrimitives.ReadInt16LittleEndian(payload[2..]);
        var offset = 4;
        var list = new List<MStockWebSocketTick>(Math.Max((int)packetCount, 1));

        for (var i = 0; i < packetCount && offset < payload.Length; i++)
        {
            var size = i == 0 && totalPacketBytes > 0
                ? totalPacketBytes
                : Math.Min(payload.Length - offset, QuotePacketBytes);
            if (size <= 0)
            {
                break;
            }

            var slice = payload.Slice(offset, Math.Min(size, payload.Length - offset));
            if (TryParsePacket(slice, out var tick))
            {
                list.Add(tick);
            }

            offset += size;
        }

        if (list.Count == 0 && payload.Length >= 8)
        {
            // Some feeds send a single packet without a multi-packet header.
            var tail = payload.Length is IndexPacketBytes or QuotePacketBytes
                ? payload
                : payload[4..];
            if (TryParsePacket(tail, out var single))
            {
                list.Add(single);
            }
        }

        return list;
    }

    public static bool TryParsePacket(ReadOnlySpan<byte> packet, out MStockWebSocketTick tick)
    {
        tick = null!;
        if (packet.Length < 8)
        {
            return false;
        }

        var token = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (packet.Length >= IndexPacketBytes
            && (IndexUniverse.TryResolveBoardByInstrumentToken(token, "NSE", out _)
                || IndexUniverse.TryResolveBoardByInstrumentToken(token, "BSE", out _)))
        {
            return TryParseIndexPacket(packet, token, out tick);
        }

        if (packet.Length >= 64)
        {
            return TryParseQuotePacket(packet, token, out tick);
        }

        var last = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[4..]));
        if (last <= 0)
        {
            return false;
        }

        var exchange = ResolveExchange(token);
        tick = new MStockWebSocketTick(token, last, 0, 0, 0, 0, exchange, null);
        return true;
    }

    private static bool TryParseIndexPacket(ReadOnlySpan<byte> packet, int token, out MStockWebSocketTick tick)
    {
        tick = null!;
        if (packet.Length < IndexPacketBytes)
        {
            return false;
        }

        var last = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[4..]));
        var high = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[8..]));
        var low = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[12..]));
        var open = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[16..]));
        var close = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[20..]));
        var exchangeTs = UnixFromBrokerInt(BinaryPrimitives.ReadInt32LittleEndian(packet[28..]));
        if (last <= 0)
        {
            return false;
        }

        tick = new MStockWebSocketTick(
            token,
            last,
            open,
            high,
            low,
            close,
            ResolveExchange(token),
            exchangeTs);
        return true;
    }

    private static bool TryParseQuotePacket(ReadOnlySpan<byte> packet, int token, out MStockWebSocketTick tick)
    {
        tick = null!;
        var last = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[4..]));
        if (last <= 0)
        {
            return false;
        }

        var open = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[28..]));
        var high = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[32..]));
        var low = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[36..]));
        var close = PriceFromPaise(BinaryPrimitives.ReadInt32LittleEndian(packet[40..]));
        var exchangeTs = UnixFromBrokerInt(BinaryPrimitives.ReadInt32LittleEndian(packet[60..]));
        tick = new MStockWebSocketTick(
            token,
            last,
            open,
            high,
            low,
            close,
            ResolveExchange(token),
            exchangeTs);
        return true;
    }

    private static string ResolveExchange(int token)
    {
        if (IndexUniverse.TryResolveBoardByInstrumentToken(token, "BSE", out _))
        {
            return "BSE";
        }

        return "NSE";
    }

    public static decimal PriceFromPaise(int raw)
    {
        if (raw == 0)
        {
            return 0;
        }

        var rupees = raw * 0.01m;
        if (rupees is > 0 and < 50_000_000)
        {
            return rupees;
        }

        return raw;
    }

    private static DateTimeOffset? UnixFromBrokerInt(int raw)
    {
        if (raw <= 0)
        {
            return null;
        }

        // Epoch seconds (10 digits) or milliseconds (13 digits).
        return raw > 2_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(raw)
            : DateTimeOffset.FromUnixTimeSeconds(raw);
    }
}
