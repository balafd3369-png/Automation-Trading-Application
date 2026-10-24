using Microsoft.Extensions.Configuration;

namespace TradeWithBeast.Services;

/// <summary>
/// Exchange lot sizes for index and equity F&amp;O underlyings. Loaded from configuration (not hardcoded in UI).
/// </summary>
public static class InstrumentLotMetadata
{
    private static IReadOnlyDictionary<string, int> _indexLots =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, int> _equityFnoLots =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static void Configure(IConfiguration configuration)
    {
        var section = configuration.GetSection("InstrumentLots");
        _indexLots = ReadLotMap(section.GetSection("Index"));
        _equityFnoLots = ReadLotMap(section.GetSection("EquityFno"));

        if (_indexLots.Count == 0)
        {
            _indexLots = DefaultIndexLots();
        }
    }

    public static int LotSize(string underlying)
    {
        if (string.IsNullOrWhiteSpace(underlying))
        {
            return 1;
        }

        var key = underlying.Trim();
        if (_indexLots.TryGetValue(key, out var indexLot))
        {
            return indexLot;
        }

        if (_equityFnoLots.TryGetValue(key, out var equityLot))
        {
            return equityLot;
        }

        return 1;
    }

    public static bool IsIndexUnderlying(string underlying)
    {
        if (string.IsNullOrWhiteSpace(underlying))
        {
            return false;
        }

        return _indexLots.ContainsKey(underlying.Trim());
    }

    private static Dictionary<string, int> ReadLotMap(IConfigurationSection section)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            if (int.TryParse(child.Value, out var lot) && lot > 0)
            {
                map[child.Key] = lot;
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<string, int> DefaultIndexLots() =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["NIFTY"] = 75,
            ["BANKNIFTY"] = 30,
            ["FINNIFTY"] = 60,
            ["SENSEX"] = 20,
        };
}
