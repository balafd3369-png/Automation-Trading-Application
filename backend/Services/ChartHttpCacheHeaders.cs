using Microsoft.Net.Http.Headers;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>Cache-Control / ETag headers so CDNs (Cloudflare, nginx) can edge-cache chart GETs.</summary>
public static class ChartHttpCacheHeaders
{
    public static void Apply(HttpResponse response, ChartLoadDepth depth, ChartResponse chart)
    {
        var maxAge = depth switch { ChartLoadDepth.Fast => 15, ChartLoadDepth.Mid => 30, _ => 45 };
        var stale  = depth switch { ChartLoadDepth.Fast => 90, ChartLoadDepth.Mid => 120, _ => 180 };
        response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(maxAge),
            MustRevalidate = false,
        };
        response.Headers["CDN-Cache-Control"] = $"max-age={maxAge}, stale-while-revalidate={stale}";
        response.Headers["Vary"] = "Accept-Encoding";

        if (chart.Candles.Count > 0)
        {
            var tail = chart.Candles[^1];
            var tag =
                $"\"twb-{chart.Name.ToUpperInvariant()}-{tail.Time}-{tail.Close:0.####}-{chart.Candles.Count}\"";
            response.Headers.ETag = tag;
            response.Headers["X-Chart-Depth"] = depth switch { ChartLoadDepth.Fast => "fast", ChartLoadDepth.Mid => "mid", _ => "full" };
        }
    }
}
