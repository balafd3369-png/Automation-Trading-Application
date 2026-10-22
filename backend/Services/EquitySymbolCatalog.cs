using System.Text.Json;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class EquitySymbolCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _cachePath;
    private readonly ILogger<EquitySymbolCatalog> _log;
    private List<CatalogEntry> _entries = [];
    private bool _loaded;

    public EquitySymbolCatalog(IWebHostEnvironment env, ILogger<EquitySymbolCatalog> log)
    {
        _cachePath = Path.Combine(env.ContentRootPath, "data", "nse-equity-catalog.json");
        _log = log;
    }

    public IReadOnlyList<SymbolSearchHit> Search(string query, int limit = 24)
    {
        EnsureLoaded();
        var q = query.Trim();
        if (q.Length == 0 || limit <= 0)
        {
            return [];
        }

        var lower = q.ToLowerInvariant();
        var upper = q.ToUpperInvariant();
        var scored = new List<(int Score, CatalogEntry Entry)>();

        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                var score = ScoreMatch(entry, lower, upper);
                if (score < 0)
                {
                    continue;
                }

                scored.Add((score, entry));
            }
        }

        return scored
            .OrderBy(row => row.Score)
            .ThenBy(row => row.Entry.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(row => new SymbolSearchHit(row.Entry.Symbol, row.Entry.Company, "NSE"))
            .ToArray();
    }

    public bool Contains(string symbol)
    {
        EnsureLoaded();
        if (TradeWithBeast.Broker.IndexUniverse.IsIndex(symbol))
        {
            return true;
        }

        var key = symbol.Trim();
        lock (_gate)
        {
            return _entries.Any(e => e.Symbol.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _entries = LoadFromDisk();
            _loaded = true;
        }

        if (_entries.Count == 0)
        {
            _log.LogWarning("NSE equity catalog is empty at {Path}. Symbol search will be limited.", _cachePath);
        }
        else
        {
            _log.LogInformation("Loaded {Count} NSE EQ symbols for search.", _entries.Count);
        }
    }

    private List<CatalogEntry> LoadFromDisk()
    {
        if (!File.Exists(_cachePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            var rows = JsonSerializer.Deserialize<List<CatalogEntry>>(json, JsonOptions) ?? [];
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Symbol))
                .Select(r => new CatalogEntry(r.Symbol.Trim().ToUpperInvariant(), (r.Company ?? "").Trim()))
                .DistinctBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to read NSE equity catalog from {Path}", _cachePath);
            return [];
        }
    }

    private static int ScoreMatch(CatalogEntry entry, string lowerQuery, string upperQuery)
    {
        var symbol = entry.Symbol;
        var company = entry.Company;

        if (symbol.Equals(upperQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (symbol.StartsWith(upperQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 10 + symbol.Length;
        }

        if (symbol.Contains(upperQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 40 + symbol.Length;
        }

        if (!string.IsNullOrWhiteSpace(company)
            && company.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 80 + company.Length;
        }

        return -1;
    }

    private sealed record CatalogEntry(string Symbol, string Company);
}
