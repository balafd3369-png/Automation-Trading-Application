using System.Text.Json;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class MStockJsonTests
{
    [Fact]
    public void IsOtpSendAcknowledged_accepts_success_status_and_otp_message()
    {
        using var ok = JsonDocument.Parse("""{"status":"success"}""");
        using var message = JsonDocument.Parse("""{"status":"error","message":"OTP sent to mobile"}""");
        Assert.True(MStockJson.IsOtpSendAcknowledged(ok.RootElement));
        Assert.True(MStockJson.IsOtpSendAcknowledged(message.RootElement));
    }

    [Fact]
    public void IsStatusSuccess_is_case_insensitive()
    {
        using var doc = JsonDocument.Parse("""{"status":"Success"}""");
        Assert.True(MStockJson.IsStatusSuccess(doc.RootElement));
    }
}
