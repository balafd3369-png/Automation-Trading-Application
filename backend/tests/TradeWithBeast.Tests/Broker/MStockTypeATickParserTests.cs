using System.Buffers.Binary;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class MStockTypeATickParserTests
{
    [Fact]
    public void TryParsePacket_parses_index_packet_layout()
    {
        Span<byte> packet = stackalloc byte[MStockTypeATickParser.IndexPacketBytes];
        BinaryPrimitives.WriteInt32LittleEndian(packet, 26000);
        BinaryPrimitives.WriteInt32LittleEndian(packet[4..], 24_350_50); // 24350.50 INR
        BinaryPrimitives.WriteInt32LittleEndian(packet[8..], 24_400_00);
        BinaryPrimitives.WriteInt32LittleEndian(packet[12..], 24_300_00);
        BinaryPrimitives.WriteInt32LittleEndian(packet[16..], 24_320_00);
        BinaryPrimitives.WriteInt32LittleEndian(packet[20..], 24_310_00);

        Assert.True(MStockTypeATickParser.TryParsePacket(packet, out var tick));
        Assert.Equal(26000, tick.InstrumentToken);
        Assert.Equal(24350.50m, tick.Last);
        Assert.Equal(24400m, tick.High);
        Assert.Equal(24300m, tick.Low);
    }

    [Fact]
    public void PriceFromPaise_converts_two_decimal_places()
    {
        Assert.Equal(22500.75m, MStockTypeATickParser.PriceFromPaise(2_250_075));
    }
}
