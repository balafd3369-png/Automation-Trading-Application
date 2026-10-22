using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class EquitySymbolCatalogTests
{
    [Fact]
    public void Search_finds_tataelxsi_by_symbol()
    {
        var env = new TestWebHostEnvironment
        {
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "backend")),
        };
        var catalog = new EquitySymbolCatalog(env, NullLogger<EquitySymbolCatalog>.Instance);
        var hits = catalog.Search("TATAELXSI", 5);
        Assert.Contains(hits, h => h.Name.Equals("TATAELXSI", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public required string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
