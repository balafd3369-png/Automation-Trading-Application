using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;

namespace TradeWithBeast.Tests.Broker;

public sealed class MStockOrderClientTests
{
    [Fact]
    public async Task Place_order_uses_type_a_contract_and_reads_order_id()
    {
        HttpRequestMessage? captured = null;
        string? content = null;
        var client = CreateClient(async request =>
        {
            captured = request;
            content = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """{"status":"success","data":{"order_id":"12345"}}""");
        });

        var result = await client.PlaceOrderAsync(
            new("NFO", "NIFTY26SEP25000CE", "SELL", 65, "MIS", "LIMIT", 100m, Tag: "TWB-test"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("12345", result.Data);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/openapi/typea/orders/regular", captured.RequestUri!.AbsoluteUri);
        Assert.Contains("Authorization", captured.Headers.Select(header => header.Key));
        Assert.Contains("transaction_type=SELL", content);
        Assert.Contains("product=MIS", content);
    }

    [Fact]
    public async Task Http_200_error_envelope_preserves_exact_broker_reason()
    {
        var client = CreateClient(_ => Task.FromResult(Json(
            HttpStatusCode.OK,
            """{"status":"error","message":"Insufficient collateral margin","error_type":"InputException","data":null}""")));

        var result = await client.PlaceOrderAsync(
            new("NFO", "NIFTY26SEP25000CE", "SELL", 65, "MIS"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Insufficient collateral margin", result.Message);
        Assert.Equal("InputException", result.ErrorType);
    }

    private static MStockOrderClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mstock-order-session-{Guid.NewGuid():N}.json");
        var sessions = new MStockSessionStore(path);
        sessions.SaveToken("access-token");
        var http = new HttpClient(new StubHandler(responder));
        var factory = new StubFactory(http);
        return new(
            factory,
            new MStockOptions { ApiKey = "api-key", RestBaseUrl = "https://api.mstock.trade" },
            sessions,
            NullLogger<MStockOrderClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
