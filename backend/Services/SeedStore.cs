using System.Text.Json;

namespace TradeWithBeast.Services;

public sealed class SeedStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    public SeedStore(string databasePath)
    {
        _path = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
    }

    public IReadOnlyList<SeedSymbol> LoadOrSeed()
    {
        if (File.Exists(_path))
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<List<SeedSymbol>>(json, JsonOptions) ?? [];
            var names = loaded.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = false;
            foreach (var seed in DefaultSeeds())
            {
                if (names.Add(seed.Name))
                {
                    loaded.Add(seed);
                    added = true;
                }
            }

            if (added)
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(loaded, JsonOptions));
            }

            return loaded;
        }

        var seeds = DefaultSeeds();
        File.WriteAllText(_path, JsonSerializer.Serialize(seeds, JsonOptions));
        return seeds;
    }

    private static List<SeedSymbol> DefaultSeeds()
    {
        var seeds = new List<SeedSymbol>
        {
            new("NIFTY", 25340m, 24910m, 25220m, 24980m, 25100m, 25180m),
            new("SENSEX", 77240m, 76110m, 76980m, 76340m, 76620m, 76290m),
            new("BANKNIFTY", 52240m, 51210m, 51980m, 51340m, 51620m, 51290m),
            new("FINNIFTY", 24840m, 24110m, 24620m, 24280m, 24450m, 24310m),
        };

        foreach (var name in TradeWithBeast.Broker.IndexUniverse.Equities)
        {
            if (seeds.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            seeds.Add(new SeedSymbol(name, 110m, 90m, 108m, 92m, 100m, 100m));
        }

        return seeds;
    }
}

public sealed record SeedSymbol(
    string Name,
    decimal HighTwoDaysAgo,
    decimal LowTwoDaysAgo,
    decimal YesterdayHigh,
    decimal YesterdayLow,
    decimal TodayOpen,
    decimal LastSeed);
